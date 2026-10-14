import io
from dataclasses import dataclass, asdict
from math import gcd, log10, sqrt

import librosa
import numpy as np
import soundfile as sf
from scipy.signal import resample_poly

TARGET_SR = 16000
FRAME = 1024
HOP = 256
VOICED_RATIO = 0.2      # 최대 RMS의 20% 넘는 프레임만 '말하는 구간'으로 간주
F_MIN, F_MAX = 70, 500  # 피치 탐색 범위(Hz)


@dataclass
class AudioStats:
    volume_db: float           # dBFS (0이 최대, 보통 음수)
    pitch_hz: float | None     # 중앙 피치, 검출 실패 시 None
    duration_sec: float        # 전체 길이
    speech_sec: float          # 실제 말한 구간 길이

    def to_dict(self) -> dict:
        d = asdict(self)
        d["volume_db"] = round(d["volume_db"], 2)
        d["pitch_hz"] = round(d["pitch_hz"], 1) if d["pitch_hz"] else None
        d["duration_sec"] = round(d["duration_sec"], 3)
        d["speech_sec"] = round(d["speech_sec"], 3)
        return d


def analyze_array(x: np.ndarray, sr: int) -> AudioStats:
    """모노 float32 배열 분석."""
    duration = len(x) / sr
    if sr != TARGET_SR:
        g = gcd(sr, TARGET_SR)
        x = resample_poly(x, TARGET_SR // g, sr // g).astype(np.float32)
    if len(x) < FRAME:
        raise ValueError("오디오가 너무 짧습니다 (최소 약 64ms)")

    rms_frames = librosa.feature.rms(y=x, frame_length=FRAME, hop_length=HOP)[0]
    peak = float(rms_frames.max())
    if peak < 1e-5:  # 사실상 무음
        return AudioStats(-100.0, None, duration, 0.0)

    voiced = rms_frames > peak * VOICED_RATIO
    rms = sqrt(float(np.mean(rms_frames[voiced] ** 2)))
    volume_db = 20.0 * log10(max(rms, 1e-5))

    f0 = librosa.yin(x, fmin=F_MIN, fmax=F_MAX, sr=TARGET_SR,
                     frame_length=FRAME, hop_length=HOP)
    n = min(len(f0), len(voiced))
    f0v = f0[:n][voiced[:n]]
    pitch = float(np.median(f0v)) if f0v.size else None

    speech_sec = float(voiced.sum()) * HOP / TARGET_SR
    return AudioStats(volume_db, pitch, duration, speech_sec)


def decode_wav(data: bytes) -> tuple[np.ndarray, int]:
    """헤더 포함 wav 바이너리 -> (16kHz 모노 float32 배열, 원본 샘플레이트).

    Whisper 와 음향 분석이 같은 배열을 쓰도록 디코딩은 여기서 한 번만 한다.
    faster-whisper 는 배열을 받으면 16kHz 라고 가정하고 확인하지 않으므로
    리샘플링을 반드시 거쳐야 한다.
    """
    x, sr = sf.read(io.BytesIO(data), dtype="float32", always_2d=True)
    x = x.mean(axis=1)  # 모노
    if sr != TARGET_SR:
        g = gcd(sr, TARGET_SR)
        x = resample_poly(x, TARGET_SR // g, sr // g)
    return np.ascontiguousarray(x, dtype=np.float32), sr


def analyze_wav_bytes(data: bytes) -> dict:
    """헤더 포함 wav 바이너리 -> 분석 결과 dict. (단독 테스트용으로 유지)"""
    x, sr = decode_wav(data)
    result = analyze_array(x, TARGET_SR).to_dict()
    result["sample_rate"] = sr
    return result