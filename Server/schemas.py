from pydantic import BaseModel, Field, ConfigDict
from datetime import datetime
from typing import Optional, List, Generic, TypeVar


# 제네릭 타입 설정 (응답 데이터 구조 공통화용)
T = TypeVar("T")

# [공통] 모든 응답의 봉투 양식
class APIResponse(BaseModel, Generic[T]):
    status: str = "success"
    code: int = 200
    message: str
    data: Optional[T] = None

# ============= [1] Game Session =============

class NewGameRequest(BaseModel):
    id: Optional[str] = Field(None, description="세션 ID (미입력 시 서버에서 UUID 생성)")

class GameSessionData(BaseModel):
    id: str
    start_time: datetime
    end_time: Optional[datetime] = None

    model_config = ConfigDict(from_attributes=True)

class NewGameResponse(APIResponse[GameSessionData]):
    pass

class EndGameSessionRequest(BaseModel):
    end_time: Optional[datetime] = None  # 클라이언트가 보내거나, 미전송 시 서버 시각(now) 기록

class NewGameData(BaseModel):
    session_id: str
    character_skin: str
    wand_skin: str
    current_stage: int
    voice_score : int = 0
    play_score : int = 0
    unlocked_spells : str
    unlocked_skins : str
    monster_kills : int


# ============= [2] Settings =============

class SettingsData(BaseModel):
    master_volume: float
    bgm_volume: float
    sfx_volume: float
    is_muted: bool
    is_windowed: bool
    resolution_op: str
    model_config = ConfigDict(from_attributes=True)

class GetSettingsResponse(APIResponse[SettingsData]):
    pass

class SettingsUpdate(BaseModel):
    master_volume: Optional[float] = None
    bgm_volume: Optional[float] = None
    sfx_volume: Optional[float] = None
    is_muted: Optional[bool] = None
    is_windowed: Optional[bool] = None
    resolution_op: Optional[str] = None


# ============= [3] Game Save =============

class SaveDataSchema(BaseModel):
    id: str
    character_skin: str
    wand_skin: str
    cur_stage: int = 1
    voice_score: int = 0
    play_score: int = 0
    unlocked_spells: List[str] = []
    unlocked_skins: List[str] = []
    monster_kills: int = 0

    model_config = ConfigDict(from_attributes=True)

class GetSaveGameResponse(APIResponse[SaveDataSchema]):
    pass

class SaveGameRequest(BaseModel):
    character_skin: Optional[str] = None
    wand_skin: Optional[str] = None
    cur_stage: Optional[int] = None
    voice_score: Optional[int] = None
    play_score: Optional[int] = None
    unlocked_spells: Optional[List[str]] = None
    unlocked_skins: Optional[List[str]] = None
    monster_kills: Optional[int] = None

class ExitGameRequest(SaveGameRequest):
    end_time: Optional[datetime] = None

class ExitGameData(BaseModel):
    id: str
    is_saved: bool = True
    end_time: datetime
    message: str = "게임 데이터 저장 및 세션 종료 완료"

    model_config = ConfigDict(from_attributes=True)

class ExitGameResponse(APIResponse[ExitGameData]):
    pass


# ============= [4] Spell =============

# 1. Spell 단일 데이터 스키마
class SpellSchema(BaseModel):
    spell_id: str
    spell_name: str
    unlock_stage: int
    required_score: int

    model_config = ConfigDict(from_attributes=True)

# 스펠 목록 조회 응답
class GetSpellsResponse(APIResponse[List[SpellSchema]]):
    pass


# 2. Skin 단일 데이터 스키마
class SkinSchema(BaseModel):
    skin_id: str
    skin_name: Optional[str] = None  # 필요 시 추가
    skin_type: Optional[str] = None  # 예: "character" 또는 "wand"

    model_config = ConfigDict(from_attributes=True)

# 스킨 목록 조회 응답
class GetSkinsResponse(APIResponse[List[SkinSchema]]):
    pass


# 3. 해금 검증 요청 및 응답 (자동 해금 처리 시)
class UnlockCheckRequest(BaseModel):
    session_id: str

class UnlockCheckResult(BaseModel):
    newly_unlocked_spells: List[str] = []
    newly_unlocked_skins: List[str] = []
    total_unlocked_spells: List[str] = []
    total_unlocked_skins: List[str] = []

class UnlockCheckResponse(APIResponse[UnlockCheckResult]):
    pass