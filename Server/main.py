# main.py
import os
import secrets
from datetime import datetime
from typing import Any, Optional

from fastapi import FastAPI, Depends, Header, WebSocket, WebSocketDisconnect, status
from fastapi.middleware.cors import CORSMiddleware
from fastapi.exceptions import RequestValidationError
from sqlalchemy.orm import Session
from routers.websocket import router as voice_router
import uvicorn

import database
import schemas_fin as schemas  # 최적화한 스키마 파일 import
from database import Base, engine
from models import GameSession, Settings, SaveData, Spell, Skin
from exceptions import NotFoundError, UnauthorizedError, BadRequestError, AppException
from handlers import app_exception_handler, validation_exception_handler, global_exception_handler

# 1. DB 테이블 자동 생성
Base.metadata.create_all(bind=engine)

# 2. FastAPI 앱 실행 및 핸들러 등록
app = FastAPI(title="Magic Spell Game Backend")

app.include_router(voice_router)

app.add_exception_handler(AppException, app_exception_handler)
app.add_exception_handler(RequestValidationError, validation_exception_handler)
app.add_exception_handler(Exception, global_exception_handler)

# 3. CORS 설정
app.add_middleware(
    CORSMiddleware,
    allow_origins=["*"],
    allow_methods=["*"],
    allow_headers=["*"],
)

# 4. 토큰 체크 (보안) - 유니티 "access-token" 헤더 검증
VALID_TOKEN = os.environ.get("GAME_TOKEN", "valid_token")


def tok_check(access_token: Optional[str] = Header(None, alias="access-token")):
    if not access_token or not secrets.compare_digest(access_token, VALID_TOKEN):
        raise UnauthorizedError("유효하지 않거나 누락된 접근 토큰입니다.")
    return access_token


# 공통 성공 반환 헬퍼 함수
def success_response(data: Any, message: str = "요청이 성공했습니다."):
    return {
        "status": "success",
        "code": 200,
        "message": message,
        "data": data
    }


# ============= [1] Game Session & Save Data =============

# 신규 게임 생성 및 세션 시작
@app.post("/v1/game/new", response_model=schemas.NewGameResponse)
def create_new_game(
        request_data: schemas.NewGameRequest,
        db: Session = Depends(database.get_db),
        token: str = Depends(tok_check)
):
    # 1. 새 세션 생성
    new_session = GameSession()
    if request_data.id:
        new_session.id = request_data.id

    db.add(new_session)
    db.commit()
    db.refresh(new_session)

    # 2. 기본 저장 데이터(SaveData) 생성
    initial_save = SaveData(
        session_id=new_session.id,
        character_skin="default_character",
        wand_skin="default_wand",
        cur_stage=1,
        voice_score=0,
        play_score=0,
        unlocked_spells=[],
        unlocked_skins=[],
        monster_kills=0
    )
    db.add(initial_save)

    # 3. 기본 설정(Settings) 생성
    initial_settings = Settings(session_id=new_session.id)
    db.add(initial_settings)

    db.commit()

    return success_response(data=new_session, message="새 게임 세션이 시작되었습니다.")

# 게임 저장
@app.patch("/v1/game/save", response_model=schemas.GetSaveGameResponse)
def save_game(
        request_data: schemas.SaveGameRequest,
        session_id: str = Header(..., alias="session-id"),
        db: Session = Depends(database.get_db),
        token: str = Depends(tok_check)
):
    save_data = db.query(SaveData).filter(SaveData.session_id == session_id).first()
    if not save_data:
        raise NotFoundError("저장 데이터를 찾을 수 없습니다.")

    # 요청 들어온 필드만 선택적으로 업데이트
    update_dict = request_data.model_dump(exclude_unset=True)
    for key, value in update_dict.items():
        if hasattr(save_data, key):
            setattr(save_data, key, value)

    # 스펠 자동 해금 조건 검수
    check_and_unlock_spells(save_data, db)

    db.commit()
    db.refresh(save_data)

    # SaveDataSchema 형식으로 맞춤
    save_dict = {
        "id": save_data.session_id,
        "character_skin": save_data.character_skin,
        "wand_skin": save_data.wand_skin,
        "cur_stage": save_data.cur_stage,
        "voice_score": save_data.voice_score,
        "play_score": save_data.play_score,
        "unlocked_spells": save_data.unlocked_spells or [],
        "unlocked_skins": save_data.unlocked_skins or [],
        "monster_kills": save_data.monster_kills
    }
    return success_response(data=save_dict, message="게임이 성공적으로 저장되었습니다.")


# 게임 종료 및 저장
@app.post("/v1/game/exit", response_model=schemas.ExitGameResponse)
def exit_game(
        request_data: schemas.ExitGameRequest,
        session_id: str = Header(..., alias="session-id"),
        db: Session = Depends(database.get_db),
        token: str = Depends(tok_check)
):
    db_session = db.query(GameSession).filter(GameSession.id == session_id).first()
    if not db_session:
        raise NotFoundError("세션을 찾을 수 없습니다.")

    # 1. 종료 시간 기록
    db_session.end_time = request_data.end_time or datetime.now()

    # 2. 저장 데이터 업데이트
    save_data = db.query(SaveData).filter(SaveData.session_id == session_id).first()
    if save_data:
        update_dict = request_data.model_dump(exclude_unset=True, exclude={"end_time"})
        for key, value in update_dict.items():
            if hasattr(save_data, key):
                setattr(save_data, key, value)
        check_and_unlock_spells(save_data, db)

    db.commit()

    exit_data = schemas.ExitGameData(
        id=session_id,
        is_saved=True,
        end_time=db_session.end_time
    )
    return success_response(data=exit_data, message="게임 데이터 저장 및 세션이 종료되었습니다.")


# ============= [2] Settings =============

@app.get("/v1/settings", response_model=schemas.GetSettingsResponse)
def get_settings(
        session_id: str = Header(..., alias="session-id"),
        db: Session = Depends(database.get_db),
        token: str = Depends(tok_check)
):
    settings = db.query(Settings).filter(Settings.session_id == session_id).first()

    # 세션 또는 설정 데이터가 없다면 동적 생성
    if not settings:
        db_session = db.query(GameSession).filter(GameSession.id == session_id).first()
        if not db_session:
            db_session = GameSession(id=session_id)
            db.add(db_session)
            db.commit()

        settings = Settings(session_id=session_id)
        db.add(settings)
        db.commit()
        db.refresh(settings)

    return success_response(data=settings, message="설정 조회 성공")


@app.patch("/v1/settings", response_model=schemas.GetSettingsResponse)
def update_settings(
        request_data: schemas.SettingsUpdate,
        session_id: str = Header(..., alias="session-id"),
        db: Session = Depends(database.get_db),
        token: str = Depends(tok_check)
):
    settings = db.query(Settings).filter(Settings.session_id == session_id).first()
    if not settings:
        raise NotFoundError("설정 정보를 찾을 수 없습니다.")

    update_data = request_data.model_dump(exclude_unset=True)
    for key, value in update_data.items():
        if hasattr(settings, key):
            setattr(settings, key, value)

    db.commit()
    db.refresh(settings)
    return success_response(data=settings, message="설정이 변경되었습니다.")


# ============= [3] Spells & Automatic Unlock =============

# 전체 스펠 마스터 데이터 목록 조회
@app.get("/v1/spells", response_model=schemas.GetSpellsResponse)
def get_all_spells(
        db: Session = Depends(database.get_db),
        token: str = Depends(tok_check)
):
    spells = db.query(Spell).all()
    return success_response(data=spells, message="스펠 목록 조회 성공")

# 조건 달성 시 자동 해금하는 내부 헬퍼 함수
def check_and_unlock_spells(save_data: SaveData, db: Session):
    available_spells = db.query(Spell).filter(
        Spell.unlock_stage <= save_data.cur_stage,
        Spell.required_score <= save_data.play_score
    ).all()

    current_unlocked = set(save_data.unlocked_spells or [])

    for spell in available_spells:
        if spell.spell_id not in current_unlocked:
            current_unlocked.add(spell.spell_id)
            print(f"스펠 자동 해금 조건 달성: {spell.spell_id}")

    save_data.unlocked_spells = list(current_unlocked)


# ==========================================
# Uvicorn 서버 자동 실행 코드
# ==========================================
if __name__ == "__main__":
    uvicorn.run("main:app", host="0.0.0.0", port=8000, reload=True)