"""
/ws/voice/streaming 에서 받는 발화 1개 (stream_start ~ stream_end).

- Unity 가 보내는 PCM16 모노 조각을 원래 샘플레이트 그대로 모아 둔다.
- [vosk] 조각이 올 때마다 아직 넘기지 않은 소리만 16kHz 로 바꿔 Vosk 에 넘긴다 (take_new_16k).
- [whisper] 받은 소리 길이가 PARTIAL_INTERVAL_SEC 늘어날 때마다 중간 받아쓰기 차례가 된다.
  중간 받아쓰기는 모아 둔 소리를 16kHz 로 바꿔 처음부터 다시 받아쓴다 (Whisper 반복 재처리).
"""

import time
from math import gcd

import numpy as np
from scipy.signal import resample_poly

from .audio_analysis import TARGET_SR

# ── 설정값 (테스트 후 조정) ──
PARTIAL_INTERVAL_SEC = 1.0      # 중간 자막 간격 (받은 소리 길이 기준). 1초로 시작해 늦으면 줄인다
MAX_STREAM_SEC = 5.0            # 발화 하나에 모을 최대 길이. 넘는 조각은 버린다
STREAM_IDLE_TIMEOUT_SEC = 2.0   # 조각이 이 시간 동안 안 오면 발화를 버린다
MIN_SAMPLE_RATE = 8000
MAX_SAMPLE_RATE = 192000
RESAMPLE_GUARD_SEC = 0.05      # [vosk] 16kHz 로 바꿀 때 끝부분은 다음 조각에 따라 조금 달라지므로 이만큼 남겨 둔다


class StreamError(ValueError):
    """stream_start 의 값이 잘못됨."""


def to_16k(x: np.ndarray, sr: int) -> np.ndarray:
    """모노 float32 배열을 16kHz 로 바꾼다 (audio_analysis.decode_wav 와 같은 방식).

    faster-whisper 는 배열을 받으면 16kHz 라고 가정하므로 반드시 거쳐야 한다.
    """
    if sr != TARGET_SR:
        g = gcd(sr, TARGET_SR)
        x = resample_poly(x, TARGET_SR // g, sr // g)
    return np.ascontiguousarray(x, dtype=np.float32)


class StreamSession:
    def __init__(self, request_id, sample_rate):
        ok = (isinstance(sample_rate, (int, float)) and not isinstance(sample_rate, bool)
              and float(sample_rate).is_integer() and MIN_SAMPLE_RATE <= sample_rate <= MAX_SAMPLE_RATE)
        if not ok:
            raise StreamError(f"sample_rate 가 올바르지 않습니다: {sample_rate!r}")

        self.request_id = request_id
        self.sample_rate = int(sample_rate)
        self.ended = False            # stream_end 를 받았는지
        self.cancelled = False        # 취소·교체·끊김으로 버려졌는지 (진행 중이던 결과도 보내지 않음)
        self.truncated = False        # 최대 길이를 넘어 조각을 버리기 시작했는지
        self.partial_running = False  # 중간 받아쓰기가 진행 중인지
        self.seq = 0                  # 시작한 중간 받아쓰기 순번 (vosk: 보낸 partial 순번)

        # [vosk] voice_runtime.PARTIAL_ENGINE == "vosk" 일 때만 쓴다
        self.vosk = None                # partial_vosk.VoskStream. 첫 소리가 들어올 때 만든다
        self.vosk_running = False       # Vosk 처리 루프가 돌고 있는지 (발화당 하나만)
        self.vosk_finished = False      # stream_end 후 남은 소리까지 확정했는지
        self.last_partial_text = ""     # 마지막으로 보낸 partial 글자 (같으면 다시 보내지 않는다)
        self._fed16 = 0                 # Vosk 에 넘긴 16kHz 샘플 수

        self._next_partial_sec = PARTIAL_INTERVAL_SEC
        self._chunks: list[np.ndarray] = []
        self._samples = 0
        self._max_samples = int(MAX_STREAM_SEC * self.sample_rate)
        self._last_chunk = time.monotonic()

    @property
    def audio_sec(self) -> float:
        """지금까지 받은 소리 길이 (초)"""
        return self._samples / self.sample_rate

    def add_pcm16(self, data: bytes) -> None:
        """PCM16 모노 리틀엔디언 조각을 더한다. 최대 길이를 넘는 부분은 버린다."""
        self._last_chunk = time.monotonic()
        room = self._max_samples - self._samples
        if room <= 0:
            self.truncated = True
            return

        n = len(data) // 2   # 홀수 바이트가 오면 마지막 1바이트는 버린다
        x = np.frombuffer(data, dtype="<i2", count=n)
        if n > room:
            x = x[:room]
            self.truncated = True
        self._chunks.append(x.astype(np.float32) / 32768.0)
        self._samples += len(x)

    def partial_due(self) -> bool:
        """중간 받아쓰기 차례인지"""
        return (not self.ended and not self.cancelled and not self.partial_running
                and self.audio_sec >= self._next_partial_sec)

    def _advance(self) -> None:
        """다음 차례를 '지금 길이 다음의 간격 배수'로 옮긴다 (밀린 차례를 한꺼번에 처리하지 않음)."""
        k = int(self.audio_sec // PARTIAL_INTERVAL_SEC) + 1
        self._next_partial_sec = k * PARTIAL_INTERVAL_SEC

    def skip_turn(self) -> None:
        """이번 차례를 건너뛴다 (모델이 바쁠 때)."""
        self._advance()

    def begin_partial(self) -> tuple[int, float, np.ndarray]:
        """중간 받아쓰기를 시작한다. (순번, 소리 길이, 지금까지 받은 원본 샘플) 을 돌려준다."""
        self.partial_running = True
        self.seq += 1
        self._advance()
        raw = np.concatenate(self._chunks) if self._chunks else np.zeros(0, dtype=np.float32)
        return self.seq, self.audio_sec, raw

    def idle_expired(self) -> bool:
        """조각이 STREAM_IDLE_TIMEOUT_SEC 동안 안 왔는지"""
        return (not self.ended and not self.cancelled
                and time.monotonic() - self._last_chunk > STREAM_IDLE_TIMEOUT_SEC)

    def take_new_16k(self, final: bool = False) -> np.ndarray:
        """[vosk] 아직 Vosk 에 넘기지 않은 소리를 16kHz 로 바꿔 돌려준다. 없으면 빈 배열.

        조각마다 따로 바꾸면 조각 경계에서 잡음이 생기므로, 모아 둔 소리 전체를 다시 바꾼 뒤
        새로 생긴 부분만 잘라 준다 (발화는 최대 MAX_STREAM_SEC 라 비용이 작다).
        16kHz 가 아니면 끝부분이 다음 조각에 따라 조금 달라지므로 final 이 아닐 때는 끝 RESAMPLE_GUARD_SEC 를 남겨 둔다.
        """
        if not self._chunks:
            return np.zeros(0, dtype=np.float32)
        x = to_16k(np.concatenate(self._chunks), self.sample_rate)
        end = len(x)
        if not final and self.sample_rate != TARGET_SR:
            end -= int(RESAMPLE_GUARD_SEC * TARGET_SR)
        if end <= self._fed16:
            return np.zeros(0, dtype=np.float32)
        out = x[self._fed16:end]
        self._fed16 = end
        return np.ascontiguousarray(out, dtype=np.float32)
