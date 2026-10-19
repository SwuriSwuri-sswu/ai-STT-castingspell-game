# websocket.py
import asyncio
import json
import logging
import time

from fastapi import APIRouter, WebSocket, WebSocketDisconnect

from services import voice_runtime
from services import partial_vosk
from services import romanize
from services import skill_matcher as sm
from services.cast_service import CastError, PROTOCOL_VERSION, build_error
from services.stream_session import StreamError, StreamSession, to_16k

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

    # 동시 판정 수 제한. 모델을 기다리는 동안은 pending_finals 로 표시해,
    # /streaming 의 중간 받아쓰기가 새로 시작해 최종 판정을 늦추지 않게 한다 (최종 판정 우선).
    voice_runtime.pending_finals += 1
    waiting = True
    try:
        async with voice_runtime.sem:
            voice_runtime.pending_finals -= 1
            waiting = False
            try:
                return await asyncio.to_thread(voice_runtime.service.process, wav, request_id, skills)
            except CastError as e:   # 길이 초과, 잘못된 WAV 등 처리할 수 없는 입력
                return build_error(e.code, str(e), request_id)
            except Exception as e:
                log.exception("처리 중 예외")
                return build_error("INTERNAL", f"{type(e).__name__}: {e}", request_id)
    finally:
        if waiting:   # 모델을 얻기 전에 취소된 경우에도 표시를 되돌린다
            voice_runtime.pending_finals -= 1


# ---------------------------------------------------------
# 공통: 스킬 목록 수신 (/streaming, /analyzing 모두 사용)
# ---------------------------------------------------------
async def receive_skill_list(websocket: WebSocket, header: dict, current: dict | None, where: str) -> dict | None:
    """skill_list 를 검사해 새 목록을 돌려준다. 잘못된 목록이면 오류를 보내고 이전 목록을 그대로 돌려준다."""
    try:
        skills = sm.parse_skill_list(header.get("skills"))
    except sm.SkillListError as e:
        await send_json(websocket, build_error("BAD_SKILL_LIST", str(e)))
        return current

    warnings = sm.find_collisions(skills)
    log.info("[%s] 스킬 목록 수신 %d개: %s", where, len(skills),
             ", ".join(info["name"] for info in skills.values()))
    for w in warnings:
        log.warning("[%s] 비슷한 스킬 이름: %s", where, w)

    # 새 스펠 목록에 맞는 Whisper 생성 길이 제한을 미리 계산해 둔다 (판정 때는 저장된 값을 쓴다)
    if voice_runtime.service is not None:
        try:
            lim = voice_runtime.service.token_limit(skills)
            log.info("[%s] 생성 길이 제한 %d (가장 긴 이름 \"%s\" %d토큰, %s)", where, lim.limit,
                     lim.longest, lim.longest_tokens,
                     "이전 계산 재사용" if lim.cached else f"계산 {lim.elapsed_ms:.1f}ms")
        except Exception:
            log.exception("[%s] 생성 길이 제한 계산 실패 (판정 때 다시 계산)", where)

    await send_json(websocket, {
        "type": "skill_list_ack",
        "version": PROTOCOL_VERSION,
        "count": len(skills),
        "warnings": warnings,
    })
    return skills


# ---------------------------------------------------------
# [1] 실시간 자막 (말하는 동안 중간 받아쓰기)
#
# 엔진: voice_runtime.PARTIAL_ENGINE
#   "vosk"    : 조각이 올 때마다 새 소리만 Vosk 에 이어서 넣고, 들린 글자가 바뀌면 partial 을 보낸다.
#               stream_end 를 받으면 남은 소리까지 확정해 글자가 바뀌었으면 마지막 partial 을 한 번 더 보낸다.
#               Whisper(판정)와 모델을 나눠 쓰지 않으므로 아래 '최종 판정 우선' 규칙은 해당 없음.
#   "whisper" : 기존 방식. 받은 소리가 PARTIAL_INTERVAL_SEC 늘 때마다 Whisper 로 처음부터 다시 받아쓴다.
#   Vosk 모델을 불러오지 못했으면 partial 을 보내지 않는다 (판정은 정상).
#
# 프로토콜
#   (선택) 텍스트 {"type": "skill_list", ...} → skill_list_ack   (/analyzing 과 동일. 중간 받아쓰기 힌트로 사용)
#   텍스트 {"type": "stream_start", "request_id": "...", "sample_rate": 48000}   발화 시작
#   바이너리 PCM16 모노 리틀엔디언 조각 (약 0.1초씩, 헤더 없음)                   발화 중
#   텍스트 {"type": "stream_end", "request_id": "..."}                            발화 끝
#   텍스트 {"type": "stream_cancel", "request_id": "..."}                         발화 버림
#   응답: {"type": "partial", "request_id", "seq", "text", "text_roman", "audio_sec", "elapsed_ms", "engine"}
#         text_roman: 화면 자막용 로마자 (발음대로, services/romanize.py). koroman 이 없으면 null → 클라이언트는 text 를 보여 준다
#         vosk: 들린 글자가 바뀔 때마다 / whisper: 받은 소리가 PARTIAL_INTERVAL_SEC 늘어날 때마다. 최종 판정은 /analyzing 이 한다 (같은 request_id).
#
# 규칙
#   - (whisper) 최종 판정 우선: 모델이 사용 중이거나 최종 판정이 기다리고 있으면 이번 중간 받아쓰기는 건너뛴다.
#   - stream_end 이후에는 새 중간 받아쓰기를 시작하지 않는다 (진행 중이던 것의 결과는 보낸다).
#   - 연결 하나에 발화 하나. 새 stream_start 가 오면 이전 발화는 버린다.
#   - 조각이 STREAM_IDLE_TIMEOUT_SEC 동안 안 오면 발화를 버린다.
# ---------------------------------------------------------
@router.websocket("/streaming")
async def voice_streaming(websocket: WebSocket):
    await websocket.accept()
    log.info("[WS] 자막용 스트리밍 연결됨 (실시간 자막 엔진: %s)", voice_runtime.partial_engine() or "꺼짐")

    session: StreamSession | None = None   # 지금 받고 있는 발화
    skills: dict | None = None            # 이 연결에서 받은 스킬 목록 (중간 받아쓰기 힌트)
    no_stream_notified = False            # NO_STREAM 오류는 발화 사이에 한 번만 알린다
    running: set[asyncio.Task] = set()    # 진행 중인 중간 받아쓰기 (참조 유지용)

    def drop(reason: str) -> None:
        """지금 발화를 버린다. 진행 중이던 중간 받아쓰기 결과도 보내지 않는다."""
        nonlocal session
        if session is not None:
            session.cancelled = True
            log.info("[stream] %s 버림: %s (%.2fs)", session.request_id, reason, session.audio_sec)
            session = None

    async def run_partial(s: StreamSession, raw, seq: int, audio_sec: float, hint: dict | None) -> None:
        """모델 사용권(sem)을 이미 얻은 상태에서 시작한다. 끝나면 반드시 돌려준다."""
        t0 = time.perf_counter()
        try:
            text = await asyncio.to_thread(
                lambda: voice_runtime.service.partial_text(to_16k(raw, s.sample_rate), hint))
        except Exception:
            log.exception("[stream] 중간 받아쓰기 실패")
            return
        finally:
            voice_runtime.sem.release()
            s.partial_running = False

        ms = round((time.perf_counter() - t0) * 1000)
        if s.cancelled:
            log.info("[stream] %s partial #%d 버림 (발화가 취소됨)", s.request_id, seq)
            return
        log.info("[stream] %s partial #%d %.1fs \"%s\" (%dms)", s.request_id, seq, audio_sec, text, ms)
        try:
            await send_json(websocket, {
                "type": "partial",
                "version": PROTOCOL_VERSION,
                "request_id": s.request_id,
                "seq": seq,
                "text": text,
                "text_roman": romanize.to_roman(text),
                "audio_sec": round(audio_sec, 2),
                "elapsed_ms": ms,
                "engine": "whisper",
            })
        except Exception:
            pass   # 그 사이 연결이 끊긴 경우

    async def try_start_partial() -> None:
        """중간 받아쓰기 차례이고 모델이 비어 있으면 시작한다."""
        s = session
        if s is None or not s.partial_due():
            return
        if voice_runtime.service is None or voice_runtime.sem is None:
            return
        if voice_runtime.sem.locked() or voice_runtime.pending_finals > 0:
            s.skip_turn()   # 최종 판정 우선: 이번 차례는 건너뛴다
            log.info("[stream] %s %.1fs 중간 받아쓰기 건너뜀 (모델 사용 중)", s.request_id, s.audio_sec)
            return
        await voice_runtime.sem.acquire()   # 바로 위에서 비어 있음을 확인했으므로 기다리지 않고 바로 얻는다
        seq, audio_sec, raw = s.begin_partial()
        task = asyncio.create_task(run_partial(s, raw, seq, audio_sec, skills))
        running.add(task)
        task.add_done_callback(running.discard)

    def start_vosk(s: StreamSession | None) -> None:
        """[vosk] 처리 루프를 시작한다. 이미 돌고 있으면 그 루프가 새 소리와 stream_end 까지 이어서 처리한다."""
        if s is None or s.cancelled or s.vosk_running or s.vosk_finished:
            return
        s.vosk_running = True
        task = asyncio.create_task(feed_vosk(s))
        running.add(task)
        task.add_done_callback(running.discard)

    async def feed_vosk(s: StreamSession) -> None:
        """[vosk] 새로 들어온 소리를 차례로 Vosk 에 넣고, 들린 글자가 바뀌면 partial 을 보낸다.

        발화마다 루프는 하나만 돈다 (s.vosk_running). 같은 인식기를 여러 스레드가 동시에 쓰지 않게 하기 위함.
        stream_end 를 받았으면 남은 소리까지 넣고 확정한 뒤 끝낸다.
        """
        try:
            while not s.cancelled:
                final = s.ended
                audio = s.take_new_16k(final=final)
                if audio.size == 0 and (not final or s.vosk is None):
                    if final:
                        s.vosk_finished = True   # 소리가 하나도 없던 발화
                    break   # 새 소리가 없다. 다음 조각이 오면 start_vosk 가 다시 시작한다

                t0 = time.perf_counter()
                if s.vosk is None:
                    model = voice_runtime.vosk_model
                    if model is None:
                        return
                    s.vosk = await asyncio.to_thread(partial_vosk.VoskStream, model, skills)
                text = s.last_partial_text
                if audio.size:
                    text = await asyncio.to_thread(s.vosk.accept, audio)
                if final:
                    text = await asyncio.to_thread(s.vosk.finish)
                    s.vosk_finished = True
                ms = round((time.perf_counter() - t0) * 1000)

                if s.cancelled:
                    log.info("[stream] %s vosk 결과 버림 (발화가 취소됨)", s.request_id)
                    return
                if text and text != s.last_partial_text:
                    s.last_partial_text = text
                    s.seq += 1
                    log.info("[stream] %s partial #%d %.1fs \"%s\" (vosk %dms%s)",
                             s.request_id, s.seq, s.audio_sec, text, ms, ", 마지막" if final else "")
                    try:
                        await send_json(websocket, {
                            "type": "partial",
                            "version": PROTOCOL_VERSION,
                            "request_id": s.request_id,
                            "seq": s.seq,
                            "text": text,
                            "text_roman": romanize.to_roman(text),
                            "audio_sec": round(s.audio_sec, 2),
                            "elapsed_ms": ms,
                            "engine": "vosk",
                        })
                    except Exception:
                        return   # 그 사이 연결이 끊긴 경우
                if final:
                    break
        except Exception:
            log.exception("[stream] Vosk 중간 받아쓰기 실패")
        finally:
            s.vosk_running = False

    async def watchdog() -> None:
        """조각이 끊긴 발화를 버린다."""
        while True:
            await asyncio.sleep(0.5)
            if session is not None and session.idle_expired():
                drop("조각이 끊김")

    watch = asyncio.create_task(watchdog())
    try:
        while True:
            msg = await websocket.receive()
            if msg["type"] == "websocket.disconnect":
                break

            text = msg.get("text")
            data = msg.get("bytes")

            # ── 텍스트 프레임: 시작·끝·취소·스킬 목록 ──
            if text is not None:
                try:
                    header = json.loads(text)
                except json.JSONDecodeError:
                    await send_json(websocket, build_error("BAD_HEADER", "JSON 이 아닌 텍스트 프레임입니다"))
                    continue

                msg_type = header.get("type") if isinstance(header, dict) else None
                rid = header.get("request_id") if isinstance(header, dict) else None

                if msg_type == "stream_start":
                    drop("새 발화 시작")
                    try:
                        session = StreamSession(rid, header.get("sample_rate"))
                    except StreamError as e:
                        await send_json(websocket, build_error("BAD_STREAM", str(e), rid))
                        continue
                    no_stream_notified = False
                    log.info("[stream] %s 시작 (%dHz)", rid, session.sample_rate)
                    continue

                if msg_type == "stream_end":
                    if session is not None and (rid is None or rid == session.request_id):
                        session.ended = True   # 새 중간 받아쓰기는 시작하지 않는다
                        if voice_runtime.partial_engine() == "vosk":
                            start_vosk(session)   # 남은 소리까지 확정 (이미 돌고 있으면 그 루프가 처리)
                        log.info("[stream] %s 끝 (%.2fs, 중간 받아쓰기 %d회)",
                                 session.request_id, session.audio_sec, session.seq)
                        session = None
                    else:
                        log.info("[stream] stream_end 무시: 진행 중인 발화와 다름 (%s)", rid)
                    continue

                if msg_type == "stream_cancel":
                    if session is not None and (rid is None or rid == session.request_id):
                        drop("Unity 가 취소")
                    continue

                if msg_type == "skill_list":
                    skills = await receive_skill_list(websocket, header, skills, "streaming")
                    continue

                await send_json(websocket, build_error("UNKNOWN_TYPE", "알 수 없는 텍스트 메시지입니다", rid))
                continue

            if data is None:
                continue

            # ── 바이너리 프레임: PCM16 조각 ──
            if session is None:
                if not no_stream_notified:
                    no_stream_notified = True
                    await send_json(websocket, build_error("NO_STREAM", "stream_start 없이 받은 소리 조각은 버립니다"))
                continue

            was_truncated = session.truncated
            session.add_pcm16(data)
            if session.truncated and not was_truncated:
                log.info("[stream] %s 최대 길이 도달, 이후 조각은 버림", session.request_id)
            engine = voice_runtime.partial_engine()
            if engine == "vosk":
                start_vosk(session)
            elif engine == "whisper":
                await try_start_partial()

    except WebSocketDisconnect:
        pass
    finally:
        watch.cancel()
        drop("연결 종료")
        log.info("[WS] 자막용 스트리밍 연결 종료")


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
                    # 잘못된 목록이면 오류를 보내고 이전 목록을 그대로 유지한다
                    skills = await receive_skill_list(websocket, header, skills, "analyzing")
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