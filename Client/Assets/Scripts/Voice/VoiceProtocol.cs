// 서버 응답 JSON 과 1:1 대응 (cast_result 에는 version, damage 가 없다. 데미지는 Unity 가 계산)
// 필드명은 PascalCase, JSON 은 snake_case -> SnakeCaseNamingStrategy 로 자동 매핑

public class CastResult
{
    public string Type;          // "cast_result"
    public string RequestId;     // null 가능
    public bool Success;
    public string RejectReason;  // 발동 시 null
    public SkillInfo Skill;      // 불발 시 null
    public SttInfo Stt;
    public AudioInfo Audio;      // 설정에 따라 불발 시 null
    public TimingInfo TimingMs;
}

public class SkillInfo
{
    public string Id;
    public string Name;
}

public class SttInfo
{
    public string Text;
    public float Match;
    public float SecondMatch;
    public float Conf;
    public float NoSpeech;
    public int Tokens;           // Whisper 가 실제로 생성한 토큰 수
    public int? TokenLimit;      // 적용한 생성 길이 제한 (null 이면 제한 없음). Tokens 가 이 값에 닿으면 지어낸 글자일 가능성이 높다
}

public class AudioInfo
{
    public float VolumeDb;
    public float? PitchHz;       // 검출 실패 시 null
    public float DurationSec;
    public float SpeechSec;
    public int SampleRate;
}

public class TimingInfo
{
    public int Decode;
    public int Stt;
    public int Analysis;
    public int Total;
}

public class ErrorResponse
{
    public string Type;          // "error"
    public int Version;
    public string RequestId;
    public string Code;          // 분기는 이 값으로
    public string Message;
}