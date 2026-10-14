"""
음성 스킬 판정 WebSocket 서버

프로토콜
  요청  : WAV 바이너리 프레임 1개 = 발화 1개
          (선택) 그 직전에 텍스트 프레임 {"type": "cast_header", "request_id": "..."}
          를 보내면 다음 응답의 request_id 에 그 값이 실린다.
  응답  : JSON 텍스트 프레임 1개 ("cast_result" 또는 "error")

    python ws_server.py
"""

import asyncio
import json
import logging

import websockets

from cast_service import CastError, CastService, build_error

HOST = "localhost"        # 다른 PC/기기에서 접속하려면 "0.0.0.0"
PORT = 8765
MAX_MSG_BYTES = 2 * 1024 * 1024  # 44.1kHz 스테레오 PCM16 5초 ≈ 0.9MB

# 동시에 Whisper 를 돌릴 요청 수. 모델 하나를 여러 스레드가 동시에 쓰면 CPU 경합이 심하다.
MAX_CONCURRENT = 1

logging.basicConfig(level=logging.INFO, format="%(asctime)s %(levelname)s %(message)s")
log = logging.getLogger("ws_server")

service: CastService | None = None
sem: asyncio.Semaphore | None = None


async def send_json(ws, obj: dict):
    await ws.send(json.dumps(obj, ensure_ascii=False))


async def handler(ws):
    pending_id = None  # cast_header 로 받은 request_id. 다음 WAV 에 한 번만 쓴다

    async for msg in ws:
        # ── 텍스트 프레임: 헤더 ──
        if isinstance(msg, str):
            try:
                header = json.loads(msg)
            except json.JSONDecodeError:
                await send_json(ws, build_error("BAD_HEADER", "JSON 이 아닌 텍스트 프레임입니다"))
                continue
            if not isinstance(header, dict) or header.get("type") != "cast_header":
                await send_json(ws, build_error("UNKNOWN_TYPE", "알 수 없는 텍스트 메시지입니다"))
                continue
            pending_id = header.get("request_id")
            continue

        # ── 바이너리 프레임: WAV ──
        rid, pending_id = pending_id, None
        async with sem:
            try:
                result = await asyncio.to_thread(service.process, msg, rid)
            except CastError as e:
                result = build_error(e.code, str(e), rid)
            except Exception as e:
                log.exception("처리 중 예외")
                result = build_error("INTERNAL", f"{type(e).__name__}: {e}", rid)

        if result["type"] == "cast_result":
            log.info("%s %s \"%s\" (%dms)",
                     "발동" if result["success"] else "불발",
                     result["skill"]["name"] if result["success"] else result["reject_reason"],
                     result["stt"]["text"], result["timing_ms"]["total"])
        else:
            log.info("오류 %s: %s", result["code"], result["message"])

        await send_json(ws, result)


async def main():
    global service, sem
    log.info("모델 로딩 중...")
    service = await asyncio.to_thread(CastService)
    sem = asyncio.Semaphore(MAX_CONCURRENT)

    async with websockets.serve(handler, HOST, PORT, max_size=MAX_MSG_BYTES):
        log.info("서버 시작: ws://%s:%d  (종료: Ctrl+C)", HOST, PORT)
        await asyncio.Future()


if __name__ == "__main__":
    try:
        asyncio.run(main())
    except KeyboardInterrupt:
        pass