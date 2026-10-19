"""
faster-whisper 래퍼

모델은 서버 시작 시 한 번만 불러온다. 요청마다 불러오면 수 초씩 걸린다.
입력은 16kHz 모노 float32 배열이어야 한다 (audio_analysis.decode_wav 의 출력).
"""

import logging
import time
from dataclasses import dataclass, replace
from typing import Iterable

import numpy as np
from faster_whisper import WhisperModel

log = logging.getLogger("voice.stt")

# ─────────────────────────────────────────────────────────────
# Whisper 상수
# ─────────────────────────────────────────────────────────────

MODEL_SIZE = "small"      # GPU 서버라면 "large-v3"
DEVICE = "cpu"            # GPU: "cuda"
COMPUTE_TYPE = "int8"     # GPU: "float16"
CPU_THREADS = 8
BEAM_SIZE = 1             # 후보(스킬명)가 정해져 있으므로 탐색 폭이 넓을 필요가 없다
LANGUAGE = "ko"

# 클라이언트(Unity)가 VAD 로 이미 잘라서 보내므로 기본은 끈다.
# 클라이언트가 앞뒤 무음을 길게 붙여 보낸다면 True 로 바꿔 서버에서 한 번 더 걸러낸다.
USE_VAD_FILTER = False

# 다시 받아쓰기(temperature fallback) 끄기.
#   faster-whisper 기본값은 [0.0, 0.2, ..., 1.0] 이라, 결과가 반복적이거나 확신이 낮으면 최대 6번까지 다시 받아쓴다.
#   작은 소리·무음에서 지어낸 긴 문장을 여러 번 만들며 20초 이상 걸리던 원인이라 숫자 하나(한 번만 받아씀)로 둔다.
TEMPERATURE = 0.0

# 생성 길이 제한(max_new_tokens). 스펠 목록(이름 + 별칭)에서 가장 긴 이름의 토큰 수로 자동 계산한다.
#   제한 = 가장 긴 이름 토큰 수 × FACTOR + MARGIN 을 [MIN, MAX] 로 묶는다.
#   MAX 는 힌트(최대 약 224토큰) + 제한 이 Whisper 한도 448 을 넘지 않게 하는 값이다.
TOKEN_LIMIT_FACTOR = 2
TOKEN_LIMIT_MARGIN = 8     # 문장부호, 시간 표시 토큰 등 여유분
TOKEN_LIMIT_MIN = 16
TOKEN_LIMIT_MAX = 96
TOKEN_LIMIT_CACHE_SIZE = 64   # 서로 다른 스펠 목록을 이만큼까지 기억한다 (넘으면 비우고 다시 계산)


@dataclass
class SttResult:
    text: str
    no_speech: float  # 세그먼트 중 최댓값. 세그먼트가 없으면 1.0
    conf: float       # 단어 확률 평균. 단어가 없으면 0.0
    tokens: int = 0                  # 실제로 생성한 토큰 수 (서버 로그와 응답 stt.tokens)
    token_limit: int | None = None   # 적용한 생성 길이 제한. None 이면 제한 없음


@dataclass(frozen=True)
class TokenLimit:
    limit: int            # max_new_tokens 로 넘길 값
    longest: str          # 토큰 수가 가장 많은 이름
    longest_tokens: int   # 그 이름의 토큰 수
    elapsed_ms: float     # 처음 계산할 때 걸린 시간
    cached: bool = False  # True 면 저장해 둔 결과를 다시 쓴 것


class SpeechRecognizer:
    def __init__(self, initial_prompt: str | None = None):
        self.model = WhisperModel(
            MODEL_SIZE, device=DEVICE, compute_type=COMPUTE_TYPE,
            cpu_threads=CPU_THREADS,
        )
        self.initial_prompt = initial_prompt
        self._limit_cache: dict[frozenset, TokenLimit] = {}

    def count_tokens(self, text: str) -> int:
        """Whisper 토크나이저 기준 토큰 수.

        받아쓰기 결과에서는 단어 앞에 띄어쓰기가 붙는 경우가 많아, 붙인 것과 안 붙인 것 중 큰 값을 쓴다.
        """
        tok = self.model.hf_tokenizer
        t = text.strip()
        return max(len(tok.encode(t, add_special_tokens=False).ids),
                   len(tok.encode(" " + t, add_special_tokens=False).ids))

    def token_limit(self, names: Iterable[str]) -> TokenLimit:
        """스펠 이름(별칭 포함) 목록에 맞는 생성 길이 제한. 같은 이름 묶음이면 저장해 둔 결과를 쓴다 (순서 무관)."""
        key = frozenset(n.strip() for n in names if n and n.strip())
        hit = self._limit_cache.get(key)
        if hit is not None:
            return replace(hit, cached=True)

        t0 = time.perf_counter()
        longest, longest_n = "", 0
        for name in sorted(key):   # 정렬해 두면 토큰 수가 같은 이름이 여럿일 때도 항상 같은 이름이 고정된다
            n = self.count_tokens(name)
            if n > longest_n:
                longest, longest_n = name, n
        limit = min(TOKEN_LIMIT_MAX, max(TOKEN_LIMIT_MIN, longest_n * TOKEN_LIMIT_FACTOR + TOKEN_LIMIT_MARGIN))
        result = TokenLimit(limit, longest, longest_n, (time.perf_counter() - t0) * 1000)

        if len(self._limit_cache) >= TOKEN_LIMIT_CACHE_SIZE:
            self._limit_cache.clear()
        self._limit_cache[key] = result
        return result

    def transcribe(self, audio: np.ndarray, initial_prompt: str | None = None,
                   max_new_tokens: int | None = None) -> SttResult:
        """max_new_tokens: 생성 길이 제한. None 이면 제한 없음 (token_limit() 의 limit 을 넘긴다)"""
        # 요청마다 힌트를 바꿀 수 있다. 주지 않으면 생성할 때 정한 기본 힌트를 쓴다.
        prompt = self.initial_prompt if initial_prompt is None else initial_prompt

        segments, _info = self.model.transcribe(
            audio,
            language=LANGUAGE,
            beam_size=BEAM_SIZE,
            temperature=TEMPERATURE,
            word_timestamps=False,
            initial_prompt=prompt,
            condition_on_previous_text=False,
            vad_filter=USE_VAD_FILTER,
            max_new_tokens=max_new_tokens,
        )
        # segments 는 제너레이터다. 여기서 소비하지 않으면 아래 계산이 빈 채로 통과한다.
        segs = list(segments)

        text = "".join(s.text for s in segs).strip()
        no_speech = max((s.no_speech_prob for s in segs), default=1.0)
        words = [w for s in segs for w in (s.words or [])]
        conf = sum(w.probability for w in words) / len(words) if words else 0.0

        # 생성한 토큰 수. 제한에 닿았으면 말이 아닌 소리에서 글자를 지어냈을 가능성이 높다 (판정은 그대로 진행)
        tokens = sum(len(s.tokens) for s in segs)
        if max_new_tokens is not None and tokens >= max_new_tokens - 1:
            log.warning("[STT] 생성 길이 제한(%d)에 도달: 지어낸 글자일 수 있음 \"%s\"",
                        max_new_tokens, text[:40])
        return SttResult(text=text, no_speech=float(no_speech), conf=float(conf),
                         tokens=tokens, token_limit=max_new_tokens)

    def warmup(self) -> None:
        """첫 요청이 느린 것을 막기 위해 시작 시 한 번 돌려둔다."""
        self.transcribe(np.zeros(16000, dtype=np.float32))
