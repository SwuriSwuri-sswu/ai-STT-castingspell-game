# models.py
from sqlalchemy import Column, Integer, String, DateTime, ForeignKey, JSON, Float, Boolean
from sqlalchemy.sql import func
from sqlalchemy.orm import relationship
from database import Base
import uuid

# 파이썬 클래스가 Base 상속받는 순간, SQLAlchemy가 자동으로 기록함
# 파이썬 클래스 안에 적어둔 변수들이 자동으로 컬럼이랑 1:1 매칭됨

# game_session 테이블
class GameSession(Base):
    __tablename__ = "game_session"

    id = Column(String(36), primary_key=True, index=True, nullable=False, default=lambda: str(uuid.uuid4()))
    start_time = Column(DateTime(timezone=True), server_default=func.now(), nullable=False)
    end_time = Column(DateTime(timezone=True), nullable=True)

    settings = relationship("Settings", back_populates="session", uselist=False, cascade="all, delete-orphan")
    save_data = relationship("SaveData", back_populates="session", uselist=False, cascade="all, delete-orphan")

# settings 테이블
class Settings(Base):
    __tablename__ = "settings"

    session_id = Column(String(36), ForeignKey("game_session.id", ondelete="CASCADE"), primary_key=True, unique=True)
    master_volume = Column(Float, default=1.0)
    bgm_volume = Column(Float, default=1.0)
    sfx_volume = Column(Float, default=1.0)
    is_muted = Column(Boolean, default=False)
    is_windowed = Column(Boolean, default=False)
    resolution_op = Column(String(20), default="1920x1080")

    session = relationship("GameSession", back_populates="settings")


# save_data 테이블
class SaveData(Base):
    __tablename__ = "save_data"

    session_id = Column(String(36), ForeignKey("game_session.id", ondelete="CASCADE"), primary_key=True, unique=True)
    character_skin = Column(String(100), nullable=False)
    wand_skin = Column(String(100), nullable=False)
    cur_stage = Column(Integer, default=1)
    voice_score = Column(Integer, default=0)
    play_score = Column(Integer, default=0)

    # Text 대신 JSON 컬럼을 사용하여 파이썬 리스트/딕셔너리 형태로 유연하게 다룸
    unlocked_spells = Column(JSON, default=list)  # 예: ["fireball", "icebolt"]
    unlocked_skins = Column(JSON, default=list)  # 예: ["skin_01", "skin_02"]
    monster_kills = Column(Integer, default=0)

    session = relationship("GameSession", back_populates="save_data")


# Spell 테이블
class Spell(Base):
    __tablename__ = "spells"

    spell_id = Column(String(50), primary_key=True)  # 예: "fireball"
    spell_name = Column(String(100), nullable=False) # 예: "화염구"
    unlock_stage = Column(Integer, default=1)        # 해금 조건: 필요 스테이지
    required_score = Column(Integer, default=0)      # 해금 조건: 필요 점수


# Skin 테이블
class Skin(Base):
    __tablename__ = "skins"

    skin_id = Column(String(50), primary_key=True)