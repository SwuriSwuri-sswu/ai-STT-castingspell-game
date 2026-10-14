# 2초 녹음해 16kHz 모노 PCM16 WAV 로 저장
#   python record_wav.py test.wav
import sys

import sounddevice as sd
import soundfile as sf

name = sys.argv[1] if len(sys.argv) > 1 else "test.wav"
SR, SEC = 16000, 2

input(f"Enter 를 누르고 {SEC}초 안에 스킬명을 말하세요...")
audio = sd.rec(int(SR * SEC), samplerate=SR, channels=1, dtype="float32")
sd.wait()
sf.write(name, audio, SR, subtype="PCM_16")
print("저장:", name)