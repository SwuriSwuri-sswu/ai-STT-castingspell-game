"""
ws_server.py 점검용 클라이언트

    python ws_test.py                 합성 신호로 프로토콜/분석 검사
    python ws_test.py a.wav b.wav     실제 녹음 파일 판정 결과 출력
"""

import asyncio
import io
import json
import sys
import time

import numpy as np
import soundfile as sf
import websockets

URI = "ws://localhost:8765"
MAX_SIZE = 2 * 1024 * 1024


def make_wav(sr: int, channels: int, freq=220.0, amp=0.1, sec=1.5) -> bytes:
    t = np.arange(int(sr * sec)) / sr
    x = (amp * np.sin(2 * np.pi * freq * t)).astype(np.float32)
    if channels > 1:
        x = np.tile(x[:, None], (1, channels))
    buf = io.BytesIO()
    sf.write(buf, x, sr, format="WAV", subtype="PCM_16")
    return buf.getvalue()


async def request(ws, data, header_id=None):
    """(선택) 헤더 + 데이터 전송 -> (응답 dict, 왕복 ms)"""
    t0 = time.time()
    if header_id is not None:
        await ws.send(json.dumps({"type": "cast_header", "request_id": header_id}))
    await ws.send(data)
    res = json.loads(await ws.recv())
    return res, (time.time() - t0) * 1000


def report(name: str, ok: bool, res: dict, dt: float) -> bool:
    print(f"[{'PASS' if ok else 'FAIL'}] {name} ({dt:.0f}ms)")
    if not ok:
        print("       " + json.dumps(res, ensure_ascii=False))
    return ok


async def run_files(paths: list[str]):
    async with websockets.connect(URI, max_size=MAX_SIZE) as ws:
        for p in paths:
            with open(p, "rb") as f:
                res, dt = await request(ws, f.read(), header_id=p)
            print(f"--- {p} (왕복 {dt:.0f}ms)")
            print(json.dumps(res, ensure_ascii=False, indent=2))


async def run_synthetic():
    results = []
    async with websockets.connect(URI, max_size=MAX_SIZE) as ws:
        # 1) 음향 분석 정확도: 220Hz, 진폭 0.1 사인파 -> 약 -23dB, 220Hz, 1.5초
        #    사인파는 말이 아니므로 판정은 대부분 불발이어야 한다 (환각 시 발동 가능)
        for name, sr, ch in [("16kHz 모노", 16000, 1), ("44.1kHz 스테레오", 44100, 2)]:
            res, dt = await request(ws, make_wav(sr, ch))
            a = res.get("audio") or {}
            ok = (
                res.get("type") == "cast_result"
                and abs(a.get("volume_db", 99) - (-23.0)) < 1.0
                and a.get("pitch_hz") is not None and abs(a["pitch_hz"] - 220) < 3
                and abs(a.get("duration_sec", 0) - 1.5) < 0.02
                and a.get("sample_rate") == sr
            )
            results.append(report(f"사인파 {name}", ok, res, dt))
            print(f"       판정: success={res.get('success')} "
                  f"reason={res.get('reject_reason')} text=\"{res.get('stt', {}).get('text')}\"")

        # 2) request_id 가 응답에 실리는지, 그리고 다음 요청에는 남지 않는지
        res, dt = await request(ws, make_wav(16000, 1), header_id="req-42")
        results.append(report("request_id 전달", res.get("request_id") == "req-42", res, dt))
        res, dt = await request(ws, make_wav(16000, 1))
        results.append(report("request_id 1회성", res.get("request_id") is None, res, dt))

        # 3) 오류 응답
        res, dt = await request(ws, make_wav(16000, 1, sec=0.05))
        results.append(report("너무 짧음 -> TOO_SHORT", res.get("code") == "TOO_SHORT", res, dt))

        res, dt = await request(ws, make_wav(16000, 1, sec=6.0))
        results.append(report("너무 김 -> TOO_LONG", res.get("code") == "TOO_LONG", res, dt))

        res, dt = await request(ws, b"not a wav")
        results.append(report("깨진 데이터 -> INVALID_WAV", res.get("code") == "INVALID_WAV", res, dt))

        await ws.send("hello")
        res = json.loads(await ws.recv())
        results.append(report("JSON 아닌 텍스트 -> BAD_HEADER", res.get("code") == "BAD_HEADER", res, 0))

        await ws.send(json.dumps({"type": "something"}))
        res = json.loads(await ws.recv())
        results.append(report("모르는 type -> UNKNOWN_TYPE", res.get("code") == "UNKNOWN_TYPE", res, 0))

    print("\n결과:", "모두 통과" if all(results) else f"실패 {results.count(False)}건")


if __name__ == "__main__":
    if len(sys.argv) > 1:
        asyncio.run(run_files(sys.argv[1:]))
    else:
        asyncio.run(run_synthetic())