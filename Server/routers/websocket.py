# websocket.py
from fastapi import APIRouter, WebSocket, WebSocketDisconnect

# APIRouter 인스턴스 생성
router = APIRouter(prefix="/ws/voice", tags=["Voice WebSocket"])

# ---------------------------------------------------------
# [1] 실시간 오디오 스트리밍 (화면 자막)
# ---------------------------------------------------------
@router.websocket("/streaming")
async def voice_streaming(websocket: WebSocket):
    await websocket.accept()
    print("[WS] 자막용 스트리밍 연결됨")
    try:
        while True:
            # 클라이언트가 보낸 오디오 데이터를 서버가 받음
            data = await websocket.receive_bytes()
            # STT로 인식 중인 중간 결과를 텍스트로만 쏴줌

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
# ---------------------------------------------------------
@router.websocket("/analyzing")
async def voice_analyzing(websocket: WebSocket):
    await websocket.accept()
    print("[WS] 스킬 분석용 연결됨")
    try:
        while True:
            # 클라이언트가 보낸 오디오 데이터를 서버가 받음
            data = await websocket.receive_bytes()
            # 최종 분석된 스펠 ID를 함께 쏴줌

            # TODO: 오디오 파일 분석 및 마법 스펠 매칭 로직
            await websocket.send_json({
                "status": "success",
                "text": "파이어 볼!",
                "spell_id": "fire_1"
            })
    except WebSocketDisconnect:
        print("[WS] 스킬 분석 정상 종료")
    except Exception as e:
        print(f"[WS Error] 스킬 분석 오류: {e}")
        await websocket.close()