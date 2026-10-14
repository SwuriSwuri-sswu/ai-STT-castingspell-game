using System;

// NetworkManager가 서버와 주고받는 데이터 형식(DTO) 모음
// 필드 이름은 서버 JSON 키와 정확히 같아야 합니다 (JsonUtility는 이름으로 매칭).
// ※ 백엔드 팀원에게 실제 API 응답 형식과 일치하는지 꼭 확인받으세요.

// 모든 응답을 감싸는 공통 형식: { "data": { ... } }
[Serializable]
public class ApiResponse<T>
{
    public T data;
}

// POST /v1/game/new 응답 data
[Serializable]
public class GameSessionData
{
    public string id;
}

// PATCH /v1/game/save 요청
[Serializable]
public class SaveDataRequest
{
    public string character_skin;
    public string wand_skin;
    public int cur_stage;
    public int voice_score;
    public int play_score;
    public int monster_kills;
}

// POST /v1/game/exit 요청
[Serializable]
public class ExitGameRequest
{
    public string end_time;
    public string character_skin;
    public string wand_skin;
    public int cur_stage;
    public int voice_score;
    public int play_score;
    public int monster_kills;
}

// GET /v1/settings 응답 data
[Serializable]
public class SettingsData
{
    public float master_volume;
    public float bgm_volume;
    public float sfx_volume;
    public bool is_muted;
    public bool is_windowed;
    public string resolution_op;
}

// PATCH /v1/settings 요청
[Serializable]
public class SettingsUpdateRequest
{
    public float master_volume;
    public float bgm_volume;
    public float sfx_volume;
    public bool is_muted;
    public bool is_windowed;
    public string resolution_op;
}
