using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using NativeWebSocket;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;
using UnityEngine;

/// <summary>
/// 서버 /ws/voice/streaming 의 "partial" 응답 (말하는 도중의 중간 받아쓰기). 서버 JSON 과 1:1 (snake_case 자동 변환)
/// </summary>
public class StreamPartial
{
    public string Type;        // "partial"
    public int Version;
    public string RequestId;   // VoiceSender.CurrentRequestId 와 같은 번호
    public int Seq;            // 발화 안에서 1부터 늘어나는 순번
    public string Text;        // 지금까지 들린 글자 전체 (확정 아님, 한글. 로그용)
    public string TextRoman;   // 화면 자막용 로마자 (발음대로). 서버에 koroman 이 없거나 예전 서버면 null
    public float AudioSec;     // 서버가 받은 소리 길이 (초)
    public int ElapsedMs;      // 서버 처리 시간 (ms)
    public string Engine;      // "vosk" / "whisper"
}

/// <summary>
/// 실시간 자막용 웹소켓 (/ws/voice/streaming). 최종 판정은 VoiceSocket(/ws/voice/analyzing)이 따로 한다.
///
/// 흐름 (VoiceSender 가 호출)
///   StartStream(id, sampleRate) → SendSamples(...) 반복 (PCM16 조각) → EndStream(id) 또는 CancelStream(id)
///   서버가 보내는 partial 은 PartialReceived 이벤트로 알린다 (VoiceRecorder 가 자막으로 표시).
///
/// [보내는 순서]
///   NativeWebSocket 은 텍스트와 바이너리의 전송 대기열이 따로라서, 보내는 중에 다른 종류를 보내면
///   순서가 섞이거나 다음 전송 때까지 남을 수 있다. 그래서 모든 메시지를 이 클래스의 대기열 하나에 넣고,
///   앞 메시지 전송이 끝난 뒤에 다음 것을 보낸다.
///
/// 연결이 안 되어 있으면 모든 함수가 조용히 아무것도 하지 않는다 (최종 판정에는 영향 없음).
/// </summary>
public class VoiceStreamSocket : MonoBehaviour
{
    /// <summary>중간 받아쓰기를 받았을 때 (메인 스레드에서 호출됨)</summary>
    public event Action<StreamPartial> PartialReceived;

    [SerializeField] string serverUrl = "ws://127.0.0.1:8000/ws/voice/streaming";
    [Tooltip("켜면 해금된 스펠만 서버로 보낸다 (VoiceSocket 과 같게 맞출 것). 서버가 받아쓰기 힌트로 쓴다")]
    [SerializeField] bool unlockedOnly = true;
    [Tooltip("받은 중간 자막을 Console 에 출력")]
    [SerializeField] bool logPartials = true;

    /// <summary>서버와 연결되어 있는지</summary>
    public bool IsConnected => ws != null && ws.State == WebSocketState.Open;

    /// <summary>지금 서버로 보내고 있는 발화 번호. 없으면 null</summary>
    public string ActiveRequestId { get; private set; }

    WebSocket ws;
    SkillManager subscribedSkill;   // 스펠 목록 변경 구독 해제용

    // 송신 대기열: Text 가 있으면 텍스트, 없으면 Bytes(바이너리)
    struct Outgoing { public string Text; public byte[] Bytes; }
    readonly Queue<Outgoing> sendQueue = new Queue<Outgoing>();
    bool pumping;

    static readonly JsonSerializer Serializer = JsonSerializer.Create(new JsonSerializerSettings
    {
        ContractResolver = new DefaultContractResolver { NamingStrategy = new SnakeCaseNamingStrategy() },
        MissingMemberHandling = MissingMemberHandling.Ignore,  // 서버에 필드가 늘어나도 깨지지 않게
    });

    async void Start()
    {
        Application.runInBackground = true;

        ws = new WebSocket(serverUrl);
        ws.OnOpen += () => { Debug.Log("[STREAM] 연결됨"); SendSkillList(); };   // 연결될 때마다 스펠 목록 전송
        subscribedSkill = SkillManager.Instance;
        if (subscribedSkill != null) subscribedSkill.SpellsChanged += SendSkillList;   // 해금·초기화 때 재전송
        ws.OnError += e => Debug.LogWarning("[STREAM] 오류: " + e);
        ws.OnClose += code => Debug.Log("[STREAM] 연결 종료: " + code);
        ws.OnMessage += bytes => HandleMessage(Encoding.UTF8.GetString(bytes));

        // 주의: Connect() 는 연결이 유지되는 동안 반환되지 않을 수 있다(내부 수신 루프).
        // 이 줄 뒤에 코드를 두지 말고, 연결 후 작업은 OnOpen 에서 할 것.
        await ws.Connect();
    }

    // ─────────────────────────────────────────────
    // 발화 (VoiceSender 가 호출)
    // ─────────────────────────────────────────────

    /// <summary>발화 시작. 연결이 없으면 아무것도 하지 않는다 (이후 SendSamples 도 무시됨)</summary>
    public void StartStream(string requestId, int sampleRate)
    {
        if (!IsConnected || string.IsNullOrEmpty(requestId))
        {
            ActiveRequestId = null;
            return;
        }
        ActiveRequestId = requestId;
        EnqueueText(JsonConvert.SerializeObject(new { type = "stream_start", request_id = requestId, sample_rate = sampleRate }));
    }

    /// <summary>모노 float(-1~1) 샘플 [start, start+count) 를 PCM16 리틀엔디언으로 바꿔 보낸다. StartStream 이후에만 보낸다</summary>
    public void SendSamples(IReadOnlyList<float> samples, int start, int count)
    {
        if (ActiveRequestId == null || !IsConnected || samples == null || count <= 0) return;

        var bytes = new byte[count * 2];
        for (int i = 0; i < count; i++)
        {
            short v = (short)(Mathf.Clamp(samples[start + i], -1f, 1f) * 32767f);
            bytes[2 * i] = (byte)(v & 0xFF);
            bytes[2 * i + 1] = (byte)((v >> 8) & 0xFF);
        }
        EnqueueBytes(bytes);
    }

    /// <summary>발화 끝. 서버는 남은 소리까지 확정해 마지막 partial 을 보낼 수 있다</summary>
    public void EndStream(string requestId)
    {
        if (requestId == null || requestId != ActiveRequestId) return;
        ActiveRequestId = null;
        if (IsConnected) EnqueueText(JsonConvert.SerializeObject(new { type = "stream_end", request_id = requestId }));
    }

    /// <summary>발화 버림 (녹음 취소 등). 서버는 이 발화의 결과를 더 보내지 않는다</summary>
    public void CancelStream(string requestId)
    {
        if (requestId == null || requestId != ActiveRequestId) return;
        ActiveRequestId = null;
        if (IsConnected) EnqueueText(JsonConvert.SerializeObject(new { type = "stream_cancel", request_id = requestId }));
    }

    // ── 스펠 목록 전송: 연결 직후, 해금·초기화 때 (서버가 받아쓰기 힌트로 사용) ──
    public void SendSkillList()
    {
        SkillManager sm = SkillManager.Instance;
        if (sm == null || !IsConnected) return;
        var spells = sm.GetVoiceSpells(unlockedOnly);
        if (spells.Count == 0) return;

        EnqueueText(JsonConvert.SerializeObject(new
        {
            type = "skill_list",
            skills = spells.Select(s => new { id = s.name, name = s.spellName }),   // id = SpellData 에셋 이름
        }));
    }

    // ─────────────────────────────────────────────
    // 송신 대기열 (메인 스레드에서만 사용)
    // ─────────────────────────────────────────────

    void EnqueueText(string text)
    {
        sendQueue.Enqueue(new Outgoing { Text = text });
        Pump();
    }

    void EnqueueBytes(byte[] bytes)
    {
        sendQueue.Enqueue(new Outgoing { Bytes = bytes });
        Pump();
    }

    // 대기열을 하나씩 꺼내 보낸다. 앞 메시지 전송이 끝나야 다음 것을 보낸다 (이미 돌고 있으면 그 루프가 처리)
    async void Pump()
    {
        if (pumping) return;
        pumping = true;
        try
        {
            while (sendQueue.Count > 0)
            {
                if (!IsConnected)
                {
                    sendQueue.Clear();
                    break;
                }
                Outgoing m = sendQueue.Dequeue();
                if (m.Text != null) await ws.SendText(m.Text);
                else await ws.Send(m.Bytes);
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning("[STREAM] 전송 실패: " + e.Message);
            sendQueue.Clear();
        }
        finally
        {
            pumping = false;
        }
    }

    // ─────────────────────────────────────────────
    // 수신
    // ─────────────────────────────────────────────

    void HandleMessage(string json)
    {
        JObject obj;
        try
        {
            obj = JObject.Parse(json);
        }
        catch (JsonException e)
        {
            Debug.LogWarning("[STREAM] JSON 파싱 실패: " + e.Message + "\n" + json);
            return;
        }

        string type = (string)obj["type"];
        if (type == "partial")
        {
            StreamPartial p = obj.ToObject<StreamPartial>(Serializer);
            if (logPartials)
                Debug.Log($"[STREAM] partial #{p.Seq} \"{p.Text}\" ({p.Engine}, {p.AudioSec:F1}s, {p.ElapsedMs}ms) request_id={p.RequestId}");
            PartialReceived?.Invoke(p);
        }
        else if (type == "skill_list_ack")
        {
            Debug.Log($"[STREAM] 스펠 목록 적용: {obj["count"]}개");
        }
        else if (type == "error")
        {
            Debug.LogWarning($"[STREAM] 오류 응답 {obj["code"]}: {obj["message"]} (request_id={obj["request_id"]})");
        }
        else
        {
            Debug.LogWarning("[STREAM] 알 수 없는 응답: " + json);
        }
    }

    void OnDestroy()
    {
        if (subscribedSkill != null) subscribedSkill.SpellsChanged -= SendSkillList;
    }

    async void OnApplicationQuit()
    {
        if (ws != null && ws.State == WebSocketState.Open) await ws.Close();
    }

    void Update()
    {
#if !UNITY_WEBGL || UNITY_EDITOR
        ws?.DispatchMessageQueue();
#endif
    }
}
