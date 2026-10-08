using System.Threading.Tasks;
using System.Collections.Generic;
using System.IO;
using System.Text;
using NativeWebSocket;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;
using UnityEngine;

public class VoiceWsTest : MonoBehaviour
{
    [SerializeField] string serverUrl = "ws://localhost:8765";
    [SerializeField] string wavFileName = "test.wav";   // Assets/StreamingAssets 안의 파일

    WebSocket ws;
    int requestCounter;
    readonly Dictionary<string, float> sentAt = new Dictionary<string, float>();

    static readonly JsonSerializer Serializer = JsonSerializer.Create(new JsonSerializerSettings
    {
        ContractResolver = new DefaultContractResolver { NamingStrategy = new SnakeCaseNamingStrategy() },
        MissingMemberHandling = MissingMemberHandling.Ignore,  // 서버에 필드가 늘어나도 깨지지 않게
    });

    async void Start()
    {
        Application.runInBackground = true;

        ws = new WebSocket(serverUrl);
        ws.OnOpen += () => Debug.Log("[WS] 연결됨");
        ws.OnError += e => Debug.LogError("[WS] 오류: " + e);
        ws.OnClose += code => Debug.Log("[WS] 연결 종료: " + code);
        ws.OnMessage += bytes => HandleMessage(Encoding.UTF8.GetString(bytes));

        // 주의: Connect() 는 연결이 유지되는 동안 반환되지 않을 수 있다(내부 수신 루프).
        // 이 줄 뒤에 코드를 두지 말고, 연결 후 작업은 OnOpen 이나 버튼에서 할 것.
        await ws.Connect();
    }

    bool IsOpen()
    {
        if (ws != null && ws.State == WebSocketState.Open) return true;
        Debug.LogWarning("[WS] 연결되어 있지 않습니다. 서버 실행 여부를 확인하세요.");
        return false;
    }

    // ── 1) 연결 확인: 기대 응답은 error / BAD_HEADER ──
    public async void SendHello()
    {
        if (!IsOpen()) return;
        await ws.SendText("hello");
        Debug.Log("[WS] hello 전송");
    }

    // ── 2) 파일 WAV 전송 (기존 버튼용) ──
    public async void SendWav()
    {
        string path = Path.Combine(Application.streamingAssetsPath, wavFileName);
        if (!File.Exists(path))
        {
            Debug.LogError("[WS] WAV 파일이 없습니다: " + path);
            return;
        }
        await SendWavBytes(File.ReadAllBytes(path), "file");
    }

    // ── WAV 바이트 전송 (파일, 마이크 공용): 헤더(텍스트) -> WAV(바이너리) ──
    public async Task<string> SendWavBytes(byte[] wav, string tag = "mic")
    {
        if (!IsOpen()) return null;

        string id = $"unity-{tag}-{++requestCounter}";
        string header = JsonConvert.SerializeObject(new { type = "cast_header", request_id = id });

        sentAt[id] = Time.realtimeSinceStartup;
        await ws.SendText(header);
        await ws.Send(wav);
        Debug.Log($"[WS] 전송 {id}: {wav.Length} bytes");
        return id;
    }

    // ── 수신 처리: type 으로 먼저 분기한 뒤 클래스로 변환 ──
    void HandleMessage(string json)
    {
        JObject obj;
        try
        {
            obj = JObject.Parse(json);
        }
        catch (JsonException e)
        {
            Debug.LogError("[WS] JSON 파싱 실패: " + e.Message + "\n" + json);
            return;
        }

        string type = (string)obj["type"];
        if (type == "cast_result")
        {
            OnCastResult(obj.ToObject<CastResult>(Serializer));
        }
        else if (type == "error")
        {
            var err = obj.ToObject<ErrorResponse>(Serializer);
            Debug.LogWarning($"[WS] 오류 응답 {err.Code}: {err.Message} (request_id={err.RequestId})");
        }
        else
        {
            Debug.LogWarning("[WS] 알 수 없는 응답: " + json);
        }
    }

    void OnCastResult(CastResult r)
    {
        string roundTrip = "-";
        if (r.RequestId != null && sentAt.TryGetValue(r.RequestId, out float t0))
        {
            roundTrip = $"{(Time.realtimeSinceStartup - t0) * 1000f:F0}ms";
            sentAt.Remove(r.RequestId);
        }

        if (r.Success)
            Debug.Log($"[발동] {r.Skill.Name} ({r.Skill.Id})  text=\"{r.Stt.Text}\"  match={r.Stt.Match:F2}  request_id={r.RequestId}");
        else
            Debug.Log($"[불발] {r.RejectReason}  text=\"{r.Stt.Text}\"  match={r.Stt.Match:F2}  no_speech={r.Stt.NoSpeech:F2}  request_id={r.RequestId}");

        if (r.Audio != null)
        {
            string pitch = r.Audio.PitchHz.HasValue ? $"{r.Audio.PitchHz.Value:F0}Hz" : "검출 실패";
            Debug.Log($"   성량 {r.Audio.VolumeDb:F1}dB, 피치 {pitch}, 발화 {r.Audio.SpeechSec:F2}s / 전체 {r.Audio.DurationSec:F2}s ({r.Audio.SampleRate}Hz)");
        }
        Debug.Log($"   서버 처리 {r.TimingMs.Total}ms (STT {r.TimingMs.Stt}ms), 왕복 {roundTrip}, damage={(r.Damage.HasValue ? r.Damage.Value.ToString() : "null")}");
    }

    // ── 테스트용 버튼 (Game 뷰 왼쪽 위) ──
    void OnGUI()
    {
        GUILayout.BeginArea(new Rect(10, 10, 280, 120));
        GUILayout.Label("WS state: " + (ws == null ? "none" : ws.State.ToString()));
        if (GUILayout.Button("1) Send hello  (expect BAD_HEADER)")) SendHello();
        if (GUILayout.Button("2) Send WAV")) SendWav();
        GUILayout.EndArea();
    }

    async void OnApplicationQuit()
    {
        if (ws != null && ws.State == WebSocketState.Open) await ws.Close();
    }
}