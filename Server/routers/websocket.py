# websocket.py
import asyncio
import json
import logging

from fastapi import APIRouter, WebSocket, WebSocketDisconnect

from services import voice_runtime
from services import skill_matcher as sm
from services.cast_service import CastError, PROTOCOL_VERSION, build_error

log = logging.getLogger("voice.ws")

# APIRouter 인스턴스 생성
router = APIRouter(prefix="/ws/voice", tags=["Voice WebSocket"])


async def send_json(ws: WebSocket, obj: dict) -> None:
    # 한글이 \uXXXX 로 바뀌지 않도록 ensure_ascii=False
    await ws.send_text(json.dumps(obj, ensure_ascii=False))


async def judge(wav: bytes, request_id, skills: dict | None) -> dict:
    """WAV 하나를 판정해 cast_result 또는 error dict 를 반환한다. skills 가 None 이면 기본 스킬."""
    if voice_runtime.service is None or voice_runtime.sem is None:
        return build_error("NOT_READY", "음성 판정 모델이 아직 준비되지 않았습니다", request_id)

    # 동시 판정 수 제한
    async with voice_runtime.sem:
        try:
            return await asyncio.to_thread(voice_runtime.service.process, wav, request_id, skills)
        except CastError as e:   # 길이 초과, 잘못된 WAV 등 처리할 수 없는 입력
            return build_error(e.code, str(e), request_id)
        except Exception as e:
            log.exception("처리 중 예외")
            return build_error("INTERNAL", f"{type(e).__name__}: {e}", request_id)


# ---------------------------------------------------------
# [1] 실시간 오디오 스트리밍 (화면 자막)
# ---------------------------------------------------------
@router.websocket("/streaming")
async def voice_streaming(websocket: WebSocket):
    await websocket.accept()
    print("[WS] 자막용 스트리밍 연결됨")
    try:
        while True:
            data = await websocket.receive_bytes()

            # TODO: 실제 STT 실시간 파이프라인 연동 위치
            await websocket.send_json({
                "status": "success",
                "text": "파이... 파이어..."
            })
    except WebSocketDisconnect:
        print("[WS] 자막용 스트리밍 정상 종료")
    except Exception as e:
        print(f"[WS Error] 자막 스트리밍 오류: {e}")
        await websocket.close()


# ---------------------------------------------------------
# [2] 마법 스펠 분석 요청 (스킬 발사용)
#
# 프로토콜
#   (선택) 텍스트 {"type": "skill_list", "skills": [{"id", "name", "aliases"}, ...]}
#          → 응답 {"type": "skill_list_ack", "count", "warnings"} 또는 error(BAD_SKILL_LIST)
#          이 연결에서 판정할 스킬 목록. 다시 보내면 교체된다. 보내지 않으면 기본 스킬.
#   요청: (선택) 텍스트 {"type": "cast_header", "request_id": "..."} → 바이너리 WAV 1개 = 발화 1개
#   응답: 텍스트 JSON 1개 ("cast_result" 또는 "error")
# ---------------------------------------------------------
@router.websocket("/analyzing")
async def voice_analyzing(websocket: WebSocket):
    await websocket.accept()
    log.info("[WS] 스킬 분석용 연결됨")
    pending_id = None   # cast_header 로 받은 request_id. 다음 WAV 에 한 번만 쓴다
    skills = None       # 이 연결에서 받은 스킬 목록. None 이면 기본 스킬

    try:
        while True:
            # receive() 는 텍스트와 바이너리를 모두 받는다 (receive_bytes 는 텍스트가 오면 오류)
            msg = await websocket.receive()
            if msg["type"] == "websocket.disconnect":
                break

            text = msg.get("text")
            data = msg.get("bytes")

            # ── 텍스트 프레임: 헤더 또는 스킬 목록 ──
            if text is not None:
                try:
                    header = json.loads(text)
                except json.JSONDecodeError:
                    await send_json(websocket, build_error("BAD_HEADER", "JSON 이 아닌 텍스트 프레임입니다"))
                    continue

                msg_type = header.get("type") if isinstance(header, dict) else None

                if msg_type == "cast_header":
                    pending_id = header.get("request_id")
                    continue

                if msg_type == "skill_list":
                    try:
                        new_skills = sm.parse_skill_list(header.get("skills"))
                    except sm.SkillListError as e:
                        # 잘못된 목록이면 이전 목록을 그대로 유지한다
                        await send_json(websocket, build_error("BAD_SKILL_LIST", str(e)))
                        continue

                    skills = new_skills
                    warnings = sm.find_collisions(skills)
                    log.info("스킬 목록 수신 %d개: %s", len(skills),
                             ", ".join(info["name"] for info in skills.values()))
                    for w in warnings:
                        log.warning("비슷한 스킬 이름: %s", w)

                    await send_json(websocket, {
                        "type": "skill_list_ack",
                        "version": PROTOCOL_VERSION,
                        "count": len(skills),
                        "warnings": warnings,
                    })
                    continue

                await send_json(websocket, build_error("UNKNOWN_TYPE", "알 수 없는 텍스트 메시지입니다"))
                continue

            if data is None:
                continue

            # ── 바이너리 프레임: WAV ──
            rid, pending_id = pending_id, None
            result = await judge(data, rid, skills)

            if result["type"] == "cast_result":
                log.info("%s %s \"%s\" (%dms)",
                         "발동" if result["success"] else "불발",
                         result["skill"]["name"] if result["success"] else result["reject_reason"],
                         result["stt"]["text"], result["timing_ms"]["total"])
            else:
                log.info("오류 %s: %s", result["code"], result["message"])

            await send_json(websocket, result)

    except WebSocketDisconnect:
        pass
    finally:
        log.info("[WS] 스킬 분석 연결 종료")