"""
실시간 자막(중간 받아쓰기)용 Vosk 래퍼

- 최종 판정은 Whisper(cast_service)가 하고, 이 모듈은 말하는 도중의 미리보기 글자만 만든다.
- Whisper 와 달리 진짜 스트리밍 방식이다. 새로 들어온 소리만 이어서 처리하므로 조각마다 결과를 낼 수 있다.
- 모델은 서버 시작 시 한 번만 불러온다 (voice_runtime.startup).
  사용자 캐시(%USERPROFILE%/.cache/vosk 등)에 없으면 vosk 가 alphacephei.com 에서 자동으로 내려받는다.
- 불러오기에 실패해도 예외를 밖으로 내보내지 않는다. 이때 실시간 자막만 꺼지고 판정은 정상 동작한다.
"""

import json
import logging
from pathlib import Path

import numpy as np

log = logging.getLogger("voice.vosk")

# ─────────────────────────────────────────────────────────────
# 설정값
# ─────────────────────────────────────────────────────────────

# 이름으로 고정한다. Model(lang="ko") 는 모델 목록이 바뀌면 다른 모델을 받을 수 있다.
VOSK_MODEL_NAME = "vosk-model-small-ko-0.22"

# 한국어 모델 기준 샘플레이트. 입력은 stream_session.take_new_16k() 에서 16kHz 로 바꿔 넣는다.
VOSK_SAMPLE_RATE = 16000

# True: 이 연결의 스펠 이름(+별칭)과 "[unk]"(목록 밖의 말)만 인식한다.
# False: 제한 없이 들린 말을 그대로 받아쓴다. 검증 결과를 보고 정한다.
USE_GRAMMAR = False

# Kaldi 내부 로그 단계. -1 = 끔, 0 = 기본
VOSK_LOG_LEVEL = -1


# ─────────────────────────────────────────────────────────────
# 모델 불러오기
# ─────────────────────────────────────────────────────────────


def find_cached_model() -> Path | None:
    """vosk 가 찾아보는 폴더에 모델이 이미 있는지. 로그에 '내려받기 포함' 여부를 표시하려고 확인한다."""
    try:
        from vosk import MODEL_DIRS
    except Exception:
        return None
    for d in MODEL_DIRS:
        if d is None:
            continue
        p = Path(d) / VOSK_MODEL_NAME
        if p.is_dir():
            return p
    return None


def load_model():
    """Vosk 모델을 불러온다. 실패하면 None 을 돌려준다.

    vosk 는 모델 이름을 목록에서 찾지 못하면 sys.exit(1) 을 부르므로 SystemExit 까지 잡는다.
    (잡지 않으면 서버 프로세스 전체가 종료된다)
    """
    try:
        import vosk
    except ImportError:
        log.warning("vosk 패키지가 설치되어 있지 않습니다. 설치: pip install vosk==0.3.45")
        return None

    try:
        vosk.SetLogLevel(VOSK_LOG_LEVEL)
        return vosk.Model(model_name=VOSK_MODEL_NAME)
    except (Exception, SystemExit) as e:
        log.warning("Vosk 모델을 불러오지 못했습니다: %s: %s", type(e).__name__, e)
        log.warning("  처음 실행이면 인터넷 연결을 확인하세요. 내려받다가 끊긴 경우 %s 폴더와 남은 .zip 파일을 지우고 다시 실행하세요.",
                    Path.home() / ".cache" / "vosk" / VOSK_MODEL_NAME)
        return None


def warmup(model) -> None:
    """첫 발화가 느린 것을 막기 위해 시작 시 한 번 돌려둔다 (무음 0.5초)."""
    s = VoskStream(model)
    s.accept(np.zeros(VOSK_SAMPLE_RATE // 2, dtype=np.float32))
    s.finish()


# ─────────────────────────────────────────────────────────────
# 발화 하나용 인식기
# ─────────────────────────────────────────────────────────────


def build_grammar(skills: dict | None) -> str | None:
    """스펠 이름과 별칭 + "[unk]" 로 Vosk 문법(JSON 문자열)을 만든다. 만들 수 없으면 None."""
    if not skills:
        return None
    phrases: list[str] = []
    for info in skills.values():
        for a in (info.get("aliases") or [info.get("name", "")]):
            a = str(a).strip()
            if a and a not in phrases:
                phrases.append(a)
    if not phrases:
        return None
    phrases.append("[unk]")
    return json.dumps(phrases, ensure_ascii=False)


class VoskStream:
    """발화 하나(stream_start ~ stream_end)용 인식기.

    같은 인식기를 여러 스레드가 동시에 쓰면 안 된다. websocket.py 의 feed_vosk 가 발화마다 하나의 루프에서
    차례로만 호출한다. 모델(Model)은 여러 인식기가 함께 쓴다.
    """

    def __init__(self, model, skills: dict | None = None):
        from vosk import KaldiRecognizer

        grammar = build_grammar(skills) if USE_GRAMMAR else None
        if grammar:
            self._rec = KaldiRecognizer(model, VOSK_SAMPLE_RATE, grammar)
        else:
            self._rec = KaldiRecognizer(model, VOSK_SAMPLE_RATE)
        self._done: list[str] = []   # 끝점(말 사이의 쉼)에서 확정된 구간들
        self._last = ""

    @staticmethod
    def _clean(text: str) -> str:
        # 문법을 쓸 때 목록 밖의 말은 "[unk]" 로 나오므로 자막에서는 뺀다
        return " ".join(w for w in text.split() if w != "[unk]")

    def _join(self, current: str) -> str:
        parts = [*self._done, current]
        self._last = " ".join(p for p in parts if p).strip()
        return self._last

    def accept(self, audio_16k: np.ndarray) -> str:
        """16kHz 모노 float32 조각을 넣고, 지금까지 들린 글자 전체를 돌려준다."""
        if audio_16k.size == 0:
            return self._last
        pcm = (np.clip(audio_16k, -1.0, 1.0) * 32767.0).astype("<i2").tobytes()
        if self._rec.AcceptWaveform(pcm):
            # 끝점 감지: 지금까지의 구간이 확정됨
            seg = self._clean(json.loads(self._rec.Result()).get("text", ""))
            if seg:
                self._done.append(seg)
            return self._join("")
        return self._join(self._clean(json.loads(self._rec.PartialResult()).get("partial", "")))

    def finish(self) -> str:
        """남은 소리까지 확정하고 전체 글자를 돌려준다 (stream_end 때 한 번)."""
        seg = self._clean(json.loads(self._rec.FinalResult()).get("text", ""))
        if seg:
            self._done.append(seg)
        return self._join("")
