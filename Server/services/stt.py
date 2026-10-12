"""
faster-whisper 래퍼

모델은 서버 시작 시 한 번만 불러온다. 요청마다 불러오면 수 초씩 걸린다.
입력은 16kHz 모노 float32 배열이어야 한다 (audio_analysis.decode_wav 의 출력).
"""

from dataclasses import dataclass

import numpy as np
from faster_whisper import WhisperModel

# ─────────────────────────────────────────────────────────────
# Whisper 상수
# ─────────────────────────────────────────────────────────────

MODEL_SIZE = "small"      # GPU 서버라면 "large-v3"
DEVICE = "cpu"            # GPU: "cuda"
COMPUTE_TYPE = "int8"     # GPU: "float16"
CPU_THREADS = 4
BEAM_SIZE = 1             # 후보(스킬명)가 정해져 있으므로 탐색 폭이 넓을 필요가 없다
LANGUAGE = "ko"

# 클라이언트(Unity)가 VAD 로 이미 잘라서 보내므로 기본은 끈다.
# 클라이언트가 앞뒤 무음을 길게 붙여 보낸다면 True 로 바꿔 서버에서 한 번 더 걸러낸다.
USE_VAD_FILTER = False


@dataclass
class SttResult:
    text: str
    no_speech: float  # 세그먼트 중 최댓값. 세그먼트가 없으면 1.0
    conf: float       # 단어 확률 평균. 단어가 없으면 0.0


class SpeechRecognizer:
    def __init__(self, initial_prompt: str | None = None):
        self.model = WhisperModel(
            MODEL_SIZE, device=DEVICE, compute_type=COMPUTE_TYPE,
            cpu_threads=CPU_THREADS,
        )
        self.initial_prompt = initial_prompt

    def transcribe(self, audio: np.ndarray) -> SttResult:
        segments, _info = self.model.transcribe(
            audio,
            language=LANGUAGE,
            beam_size=BEAM_SIZE,
            word_timestamps=True,
            initial_prompt=self.initial_prompt,
            condition_on_previous_text=False,
            vad_filter=USE_VAD_FILTER,
        )
        # segments 는 제너레이터다. 여기서 소비하지 않으면 아래 계산이 빈 채로 통과한다.
        segs = list(segments)

        text = "".join(s.text for s in segs).strip()
        no_speech = max((s.no_speech_prob for s in segs), default=1.0)
        words = [w for s in segs for w in (s.words or [])]
        conf = sum(w.probability for w in words) / len(words) if words else 0.0
        return SttResult(text=text, no_speech=float(no_speech), conf=float(conf))

    def warmup(self) -> None:
        """첫 요청이 느린 것을 막기 위해 시작 시 한 번 돌려둔다."""
        self.transcribe(np.zeros(16000, dtype=np.float32))