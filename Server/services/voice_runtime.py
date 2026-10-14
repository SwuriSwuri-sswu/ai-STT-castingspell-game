"""
음성 판정 서비스를 서버 전체에서 하나만 만들어 공유한다.

- startup(): FastAPI 시작 시 한 번 호출. Whisper 모델을 불러오고 준비 실행(warmup)까지 마친다.
- service : CastService 인스턴스. 시작 전에는 None.
- sem     : 동시에 판정할 수 있는 요청 수 제한. 모델 하나를 여러 스레드가 동시에 쓰면 CPU 경합이 심하다.
            /analyzing(최종 판정)과 /streaming(중간 자막)이 같이 쓴다.
- pending_finals : 모델을 기다리는 최종 판정 수. 0 보다 크면 중간 자막은 새로 시작하지 않는다 (최종 판정 우선).

"""

import asyncio
import logging

from .cast_service import CastService

MAX_CONCURRENT = 1

log = logging.getLogger("voice")

service: CastService | None = None
sem: asyncio.Semaphore | None = None
pending_finals: int = 0


async def startup() -> None:
    global service, sem
    log.info("음성 판정 모델 로딩 중...")
    # 모델 로딩은 수 초 걸리는 동기 작업이므로 별도 스레드에서 실행한다
    service = await asyncio.to_thread(CastService)
    sem = asyncio.Semaphore(MAX_CONCURRENT)
    log.info("음성 판정 준비 완료")