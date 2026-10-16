"""
음성 서비스(최종 판정 + 실시간 자막)를 서버 전체에서 하나씩만 만들어 공유한다.

- startup(): FastAPI 시작 시 한 번 호출. 모델을 차례로 불러오고, 모델별 로딩 시간을 로그로 남긴다.
    [1/2] Whisper 판정 모델 (CastService: 모델 불러오기 + 준비 실행)
    [2/2] Vosk 실시간 자막 모델 (PARTIAL_ENGINE 이 "vosk" 일 때만. 캐시에 없으면 자동으로 내려받음)
- service : CastService 인스턴스 (Whisper 최종 판정). 시작 전에는 None.
- sem     : Whisper 를 동시에 쓸 수 있는 요청 수 제한. 모델 하나를 여러 스레드가 동시에 쓰면 CPU 경합이 심하다.
            PARTIAL_ENGINE 이 "whisper" 이면 /streaming(중간 자막)도 같이 쓴다.
- pending_finals : 모델을 기다리는 최종 판정 수. 0 보다 크면 Whisper 중간 자막은 새로 시작하지 않는다 (최종 판정 우선).
- vosk_model : Vosk 모델. 쓰지 않거나 불러오지 못했으면 None (이때 Vosk 실시간 자막은 꺼지고 판정은 정상 동작).
"""

import asyncio
import logging
import time

from . import partial_vosk
from .cast_service import CastService

MAX_CONCURRENT = 1

# 실시간 자막(/ws/voice/streaming) 엔진
#   "vosk"    : Vosk 로 조각마다 이어서 받아쓴다. Whisper(판정)와 모델을 나눠 쓰지 않는다.
#   "whisper" : 기존 방식. 판정용 Whisper 모델로 PARTIAL_INTERVAL_SEC 마다 처음부터 다시 받아쓴다.
#   "off"     : 실시간 자막을 보내지 않는다.
PARTIAL_ENGINE = "vosk"

log = logging.getLogger("voice")

service: CastService | None = None
sem: asyncio.Semaphore | None = None
pending_finals: int = 0
vosk_model = None


def partial_engine() -> str | None:
    """지금 실제로 쓰는 실시간 자막 엔진 ("vosk" / "whisper"). 쓸 수 없으면 None."""
    if PARTIAL_ENGINE == "vosk":
        return "vosk" if vosk_model is not None else None
    if PARTIAL_ENGINE == "whisper":
        return "whisper" if service is not None else None
    return None


async def startup() -> None:
    global service, sem, vosk_model
    t_all = time.perf_counter()

    # [1/2] Whisper 판정 모델. 수 초 걸리는 동기 작업이므로 별도 스레드에서 실행한다
    log.info("[1/2] Whisper 판정 모델 로딩 중... (처음 실행이면 자동으로 내려받습니다)")
    t0 = time.perf_counter()
    service = await asyncio.to_thread(CastService)
    sem = asyncio.Semaphore(MAX_CONCURRENT)
    log.info("[1/2] Whisper 판정 모델 준비 완료: %.2f초", time.perf_counter() - t0)

    # [2/2] Vosk 실시간 자막 모델 (Whisper 가 끝난 뒤 차례로 불러온다)
    if PARTIAL_ENGINE == "vosk":
        cached = partial_vosk.find_cached_model()
        log.info("[2/2] Vosk 실시간 자막 모델 로딩 중... (%s)",
                 f"캐시 사용: {cached}" if cached else "캐시에 없음 → 자동으로 내려받습니다. 처음 한 번, 인터넷 필요")
        t0 = time.perf_counter()
        vosk_model = await asyncio.to_thread(partial_vosk.load_model)
        t_load = time.perf_counter() - t0

        if vosk_model is not None:
            t1 = time.perf_counter()
            try:
                await asyncio.to_thread(partial_vosk.warmup, vosk_model)
            except Exception:
                log.exception("[2/2] Vosk 준비 실행 실패")
                vosk_model = None
            t_warm = time.perf_counter() - t1

        if vosk_model is None:
            log.warning("[2/2] Vosk 모델을 쓸 수 없어 실시간 자막을 끕니다 (%.2f초). 판정(Whisper)은 정상 동작합니다.",
                        time.perf_counter() - t0)
        else:
            log.info("[2/2] Vosk 실시간 자막 모델 준비 완료: %.2f초 (불러오기%s %.2f초 + 준비 실행 %.2f초)",
                     time.perf_counter() - t0, "" if cached else "·내려받기", t_load, t_warm)
    else:
        log.info("[2/2] 실시간 자막 엔진이 \"%s\" 이므로 Vosk 는 불러오지 않습니다", PARTIAL_ENGINE)

    log.info("음성 서버 준비 완료: 총 %.2f초 (실시간 자막: %s)",
             time.perf_counter() - t_all, partial_engine() or "꺼짐")
