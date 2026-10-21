"""
WAV 바이트 -> 디코딩 -> 받아쓰기 + 음향 분석 -> 스킬 판정 -> 응답 dict

ws_server.py 는 이 모듈의 CastService.process() 만 호출한다.
데미지는 Unity 가 계산하므로 응답에 넣지 않는다.
"""

import logging
import time

import numpy as np

from . import skill_matcher as sm
from .audio_analysis import TARGET_SR, analyze_array, decode_wav
from .stt import COMPUTE_TYPE, CPU_THREADS, DEVICE, MODEL_SIZE, SpeechRecognizer, SttResult, TokenLimit

log = logging.getLogger("voice")

# ─────────────────────────────────────────────────────────────
# 튜닝 상수
# ─────────────────────────────────────────────────────────────

PROTOCOL_VERSION = 1

# 이 값을 넘으면 "음성이 아니다"로 보고 버린다 (잡음에 대한 Whisper 환각 방어)
MAX_NO_SPEECH_PROB = 0.6

# 입력 길이 제한. 스킬명만 외치는 방식이므로 길 필요가 없다.
MIN_AUDIO_SEC = 0.1   # analyze_array 는 64ms 미만이면 예외를 낸다
MAX_AUDIO_SEC = 5.0

# 불발일 때도 음향 분석값을 응답에 넣을지 (튜닝 단계에서는 True 권장)
INCLUDE_AUDIO_ON_REJECT = True


class CastError(Exception):
    """처리 자체가 불가능한 입력. 응답 type 이 "error" 가 된다."""

    def __init__(self, code: str, message: str):
        super().__init__(message)
        self.code = code


# ─────────────────────────────────────────────────────────────
# 응답 조립
# ─────────────────────────────────────────────────────────────


def build_error(code: str, message: str, request_id=None) -> dict:
    return {
        "type": "error",
        "version": PROTOCOL_VERSION,
        "request_id": request_id,
        "code": code,
        "message": message,
    }


def _judge(stt: SttResult, skills: dict) -> tuple[str | None, sm.MatchResult | None]:
    """게이트 판정. 비용이 낮고 차단률이 높은 것부터 검사한다.

    반환: (불발 사유 또는 None, 매칭 결과 또는 None)
    """
    if not stt.text.strip():
        return "EMPTY", None
    if stt.no_speech > MAX_NO_SPEECH_PROB:
        return "NOISE", None
    m = sm.match_skill(stt.text, skills)  # EMPTY_TEXT / NO_MATCH / AMBIGUOUS
    if not m.ok:
        return m.reject_reason, m
    return None, m


# ─────────────────────────────────────────────────────────────
# 서비스
# ─────────────────────────────────────────────────────────────


class CastService:
    def __init__(self, warmup: bool = True):
        # 단계별 시간을 로그로 남긴다 (voice_runtime.startup 의 [1/2] 안에서 찍힘)
        t0 = time.perf_counter()
        self.stt = SpeechRecognizer()   # 힌트(initial_prompt)는 요청마다 이 연결의 스킬 목록으로 넘긴다
        log.info("  Whisper 모델 불러오기 (%s, %s, %s, 스레드 %d): %.2f초 (처음 실행이면 내려받기 포함)",
                 MODEL_SIZE, DEVICE, COMPUTE_TYPE, CPU_THREADS, time.perf_counter() - t0)
        if warmup:
            t1 = time.perf_counter()
            self.stt.warmup()
            log.info("  Whisper 준비 실행: %.2f초", time.perf_counter() - t1)
            t2 = time.perf_counter()
            self._warmup_analysis()
            log.info("  음향 분석(librosa) 준비 실행: %.2f초", time.perf_counter() - t2)

    @staticmethod
    def _warmup_analysis() -> None:
        """librosa 첫 호출의 지연(모듈 로딩, numba 컴파일)을 서버 시작 시 미리 치른다.

        무음은 피치 분석 전에 반환되어 컴파일이 일어나지 않으므로 사인파를 쓴다.
        """
        t = np.arange(TARGET_SR, dtype=np.float32) / TARGET_SR
        analyze_array((0.1 * np.sin(2 * np.pi * 220 * t)).astype(np.float32), TARGET_SR)

    def token_limit(self, skills: dict) -> TokenLimit:
        """이 스펠 목록에 맞는 생성 길이 제한. 판정은 별칭으로도 맞추므로 이름과 별칭을 모두 넣어 가장 긴 것을 찾는다.

        같은 목록이면 저장해 둔 값을 쓴다. 스펠 목록을 받을 때(websocket.receive_skill_list) 미리 한 번 계산된다.
        """
        phrases = [p for info in skills.values() for p in (info["name"], *info.get("aliases", []))]
        return self.stt.token_limit(phrases)

    def partial_text(self, audio: np.ndarray, skills: dict | None) -> str:
        """중간 자막(/streaming)용 받아쓰기. 판정·음향 분석 없이 들린 글자만 돌려준다.

        audio: 16kHz 모노 float32 (stream_session.to_16k 의 출력)
        잡음으로 판단되면(no_speech 가 높으면) 빈 문자열을 돌려줘, 엉뚱한 글자가 자막에 뜨지 않게 한다.
        스킬 목록을 아직 받지 못했으면(skills 가 None) 받아쓰지 않고 빈 문자열을 돌려준다.
        나중에 중간 자막만 다른 엔진(혼합 방식)으로 바꿀 때는 이 함수만 교체한다.
        """
        if not skills:
            return ""
        stt = self.stt.transcribe(audio, initial_prompt=sm.build_initial_prompt(skills),
                                  max_new_tokens=self.token_limit(skills).limit)
        if stt.no_speech > MAX_NO_SPEECH_PROB:
            return ""
        return stt.text

    def process(self, wav: bytes, request_id=None, skills: dict | None = None) -> dict:
        """동기 함수. 서버에서는 asyncio.to_thread 로 호출한다.

        skills: 이 연결에서 Unity 가 보낸 스킬 목록. 없으면(None) CastError(NO_SKILL_LIST).
        입력이 잘못됐으면 CastError 를 던진다.
        """
        if not skills:
            raise CastError("NO_SKILL_LIST", "스킬 목록(skill_list)을 받기 전입니다")
        t0 = time.perf_counter()

        # ① 디코딩 (한 번만)
        try:
            audio, src_sr = decode_wav(wav)
        except Exception as e:
            raise CastError("INVALID_WAV", f"{type(e).__name__}: {e}") from e

        dur = len(audio) / TARGET_SR
        if dur < MIN_AUDIO_SEC:
            raise CastError("TOO_SHORT", f"{dur:.3f}s < {MIN_AUDIO_SEC}s")
        if dur > MAX_AUDIO_SEC:
            raise CastError("TOO_LONG", f"{dur:.3f}s > {MAX_AUDIO_SEC}s")

        # ② 받아쓰기: 이 연결의 스킬 이름을 힌트로 주고, 생성 길이는 가장 긴 스킬 이름에 맞춰 제한한다
        t1 = time.perf_counter()
        lim = self.token_limit(skills)
        stt = self.stt.transcribe(audio, initial_prompt=sm.build_initial_prompt(skills),
                                  max_new_tokens=lim.limit)
        # 생성 토큰 수: 서버 로그에 남기고, 응답의 stt.tokens / stt.token_limit 에도 넣는다
        log.info("  [STT] %s 생성 토큰 %d/%d (%dms)", request_id or "-", stt.tokens, lim.limit,
                 round((time.perf_counter() - t1) * 1000))

        # ③ 음향 분석 (같은 배열 사용)
        t2 = time.perf_counter()
        audio_stats = analyze_array(audio, TARGET_SR).to_dict()
        audio_stats["sample_rate"] = src_sr

        # ④ 판정
        t3 = time.perf_counter()
        reason, m = _judge(stt, skills)
        success = reason is None

        skill = None
        if success:
            skill = {"id": m.skill_id, "name": skills[m.skill_id]["name"]}

        t4 = time.perf_counter()
        return {
            "type": "cast_result",
            "request_id": request_id,
            "success": success,
            "reject_reason": reason,
            "skill": skill,
            "stt": {
                "text": stt.text,
                "match": round(m.match, 3) if m else 0.0,
                "second_match": round(m.second_match, 3) if m else 0.0,
                "conf": round(stt.conf, 3),
                "no_speech": round(stt.no_speech, 3),
                "tokens": stt.tokens,             # 실제로 생성한 토큰 수
                "token_limit": stt.token_limit,   # 적용한 생성 길이 제한. tokens 가 이 값에 닿으면 지어낸 글자일 가능성이 높다
            },
            "audio": audio_stats if (success or INCLUDE_AUDIO_ON_REJECT) else None,
            "timing_ms": {
                "decode": round((t1 - t0) * 1000),
                "stt": round((t2 - t1) * 1000),
                "analysis": round((t3 - t2) * 1000),
                "total": round((t4 - t0) * 1000),
            },
        }