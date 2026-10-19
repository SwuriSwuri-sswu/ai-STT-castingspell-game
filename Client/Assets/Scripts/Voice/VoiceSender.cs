using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

/// <summary>
/// VoiceInput(소리) → VoiceActivityDetector(말 구간 감지) → WAV 변환·전송 → 결과 대기 를 조정한다.
///
/// 입력 방식 (Inspector 의 inputMode 에서 선택)
///   - PushToTalk : 키(기본 스페이스바)를 누르는 동안 녹음하고, 떼면 전송한다. (기본값)
///   - AutoDetect : VAD 가 말 시작·끝을 감지해 자동으로 전송한다 (기존 방식).
///   두 방식 모두 WAV 변환·전송·결과 대기(막기)는 같은 코드를 쓴다.
///
/// B-1 단계: 말이 끝나면 발화를 WAV 한 덩어리로 보낸다 (기존 cast_header + WAV 규칙).
///          실시간 자막: 녹음하는 동안 같은 소리를 조각(기본 0.1초)으로 VoiceStreamSocket(/streaming)에도 보낸다.
///          최종 판정은 지금처럼 WAV 한 덩어리로 /analyzing 에 보낸다 (두 요청이 같은 request_id 를 쓴다).
///
/// [막기 방식]
///   주문 하나를 보내면 서버 판정이 돌아올 때까지 새 주문을 받지 않는다.
///   대기 중에 말한 주문은 보관하지 않고 버린다.
///   판정 결과(또는 오류)가 오거나, 정해진 시간이 지나면 대기를 푼다.
///
/// [받기 조건]
///   CanAccept 가 false 일 때(예: 공격할 적이 없음) 시작된 발화는 보내지 않고 끝까지 무시한다.
/// </summary>
public class VoiceSender : MonoBehaviour
{
    /// <summary>녹음을 시작하고 끝내는 방식</summary>
    public enum InputMode
    {
        PushToTalk = 0,   // 키를 누르는 동안 녹음
        AutoDetect = 1,   // VAD 자동 감지 (기존 방식)
    }

    [Header("연결")]
    [SerializeField] VoiceInput voiceInput;
    [SerializeField] VoiceSocket voiceSocket;

    [Header("실시간 자막 (중간 받아쓰기)")]
    [Tooltip("/ws/voice/streaming 연결. 비워 두면 실시간 자막 없이 최종 판정만 한다")]
    [SerializeField] VoiceStreamSocket streamSocket;
    [Tooltip("녹음하는 동안 소리 조각을 실시간 자막 서버로 보낼지")]
    [SerializeField] bool streamPartials = true;
    [Tooltip("조각 하나의 길이(초). 서버는 조각이 올 때마다 받아쓴다")]
    [SerializeField, Range(0.05f, 0.5f)] float streamChunkSec = 0.1f;

    [Header("입력 방식")]
    [Tooltip("PushToTalk: 키를 누르는 동안 녹음 / AutoDetect: 말소리를 자동 감지 (기존 방식). 플레이 중에 바꿔도 된다")]
    [SerializeField] InputMode inputMode = InputMode.PushToTalk;

    [Header("키 입력 설정 (PushToTalk)")]
    [Tooltip("누르는 동안 녹음할 키")]
    [SerializeField] KeyCode pushToTalkKey = KeyCode.Space;
    [Tooltip("키를 누르기 직전 구간을 함께 보낼 길이(초). 키와 동시에 말을 시작해도 첫 음절이 잘리지 않게 한다")]
    [SerializeField, Min(0f)] float pttPrerollSec = 0.2f;
    [Tooltip("키를 뗀 뒤 더 녹음할 시간(초). 마지막 음절을 말하면서 키를 떼도 잘리지 않게 한다")]
    [SerializeField, Min(0f)] float pttReleaseTailSec = 0.2f;

    [Header("자동 감지 설정 (AutoDetect)")]
    [SerializeField] VadSettings vadSettings = new VadSettings();

    [Header("결과 대기 (막기 방식)")]
    [Tooltip("서버 응답이 이 시간(초) 안에 오지 않으면 대기를 강제로 푼다. 게임이 멈추지 않게 하는 안전장치")]
    [SerializeField] float resultTimeoutSec = 3f;

    [Header("발화 다듬기")]
    [Tooltip("말 끝 무음 중 남겨 둘 길이(초). 끝 자음이 잘리지 않게 약간 남긴다")]
    [SerializeField] float keepTrailingSilenceSec = 0.1f;

    [Header("무음 차단")]
    [Tooltip("녹음 전체의 가장 큰 소리(peak, 0~1)가 이보다 작으면 무음으로 보고 판정을 보내지 않는다. 작게 말한 주문이 막히면 낮춘다")]
    [SerializeField, Range(0f, 0.1f)] float silentPeak = 0.01f;

    const float MinRecordingSec = 0.15f;   // 서버 최소 0.1초 + 여유
    const float MaxRecordingSec = 4.9f;    // 서버 최대 5초보다 약간 짧게 (프레임 단위 오차 대비)

    /// <summary>현재 VAD (디버그 표시용). 장치 변경 등으로 다시 만들어지기 전에는 null 일 수 있다.</summary>
    public VoiceActivityDetector Vad => vad;

    // ── 게임(자막 연출 등)에서 구독하는 이벤트 ──
    public event Action RecordingStarted;            // 발화 수집 시작
    public event Action RecordingSubmitted;          // 발화 전송 시작
    public event Action<string> RecordingRejected;   // 전송하지 못함 (사유)
    public event Action RecordingDiscarded;          // 무음이라 조용히 버림 (화면 문구 없이 안내로 복귀)
    public event Action<byte[]> UtteranceSent;       // 서버로 보낸 WAV (VoiceDebug 의 디버그 저장용)

    /// <summary>
    /// 지금 주문을 받을 수 있는지 (예: 공격할 적이 있는지). VoiceRecorder 가 설정한다.
    /// false 이면 그때 시작된 발화는 보내지 않고 끝까지 무시한다. null 이면 항상 받는다.
    /// VoiceSender 가 게임 코드(VoiceSkillBridge, SkillManager)를 직접 알지 않도록 함수로 받는다.
    /// </summary>
    public Func<bool> CanAccept { get; set; }

    public bool IsRecording => collecting;
    public bool IsWaitingForResult => waitingForResult;
    public string LastInfo => lastInfo;              // 마지막 발화 정보 (디버그 표시용)

    /// <summary>지금 입력 방식이 키 입력(PushToTalk)인지. VoiceRecorder 가 안내 문구를 고를 때 쓴다</summary>
    public bool IsPushToTalk => inputMode == InputMode.PushToTalk;
    public KeyCode PushToTalkKey => pushToTalkKey;

    /// <summary>지금(또는 마지막) 발화의 요청 번호. 실시간 자막(partial)과 최종 판정(cast_result)이 같은 번호를 쓴다</summary>
    public string CurrentRequestId { get; private set; }

    VoiceActivityDetector vad;
    readonly List<float> utterance = new List<float>(16000 * 5);
    bool collecting;
    string lastInfo = "-";

    // ─────────────────────────────────────────────
    // 결과 대기 상태 (막기 방식)
    //   waitingForResult : true 인 동안 새 주문을 받지 않는다
    //   waitDeadline     : 이 시각이 지나면 응답이 없어도 대기를 푼다
    //   pendingRequestId : 지금 기다리는 요청 번호. 다른 요청의 결과로 대기가 풀리지 않게 한다
    //   ignoringCurrentSpeech : 대기 중 시작된 발화를 끝까지 무시하기 위한 표시
    // ─────────────────────────────────────────────
    bool waitingForResult;
    float waitDeadline;
    string pendingRequestId;
    bool ignoringCurrentSpeech;

    // 시간 초과로 기다리기를 포기한 요청 (요청 번호 → 보낸 시각). 늦게 도착한 결과를 알아보고 버리는 데 쓴다.
    // 최근 MaxExpiredRequests 개만 기억한다 (결과가 와도 지우지 않는다: 다른 스크립트도 같은 결과를 확인하기 때문)
    const int MaxExpiredRequests = 16;
    readonly Dictionary<string, float> expiredRequests = new Dictionary<string, float>();
    readonly Queue<string> expiredOrder = new Queue<string>();

    // ─────────────────────────────────────────────
    // 키 입력(PushToTalk) 상태
    //   pttHeld     : 키를 눌러 녹음을 시작했고 아직 떼지 않음
    //   pttStopAt   : 키를 뗀 뒤 꼬리 녹음이 끝나는 시각. -1 이면 꼬리 녹음 중이 아님
    //   pttRing     : 키를 누르기 전 최근 소리를 보관하는 원형 버퍼 (프리롤)
    //   appliedMode : 마지막으로 적용한 입력 방식. 플레이 중 Inspector 에서 바꾸면 감지해 정리한다
    // ─────────────────────────────────────────────
    bool pttHeld;
    float pttStopAt = -1f;
    float[] pttRing;
    int pttRingHead, pttRingCount, pttRingRate;
    InputMode appliedMode;

    // ── 실시간 자막 전송 상태 ──
    int requestCounter;                                     // 요청 번호 (unity-mic-1, 2, ...)
    bool streaming;                                         // 이번 발화를 실시간 자막 서버로 보내는 중인지
    readonly List<float> streamBuf = new List<float>(4800); // 아직 보내지 않은 조각

    void OnEnable()
    {
        appliedMode = inputMode;
        if (voiceInput != null) voiceInput.DeviceChanged += OnDeviceChanged;
        if (voiceInput != null) voiceInput.SamplesAvailable += OnSamples;
        if (voiceSocket != null)
        {
            voiceSocket.CastResultReceived += OnCastResult;
            voiceSocket.ErrorReceived += OnServerError;
        }
    }

    void OnDisable()
    {
        if (voiceInput != null) voiceInput.DeviceChanged -= OnDeviceChanged;
        if (voiceInput != null) voiceInput.SamplesAvailable -= OnSamples;
        if (voiceSocket != null)
        {
            voiceSocket.CastResultReceived -= OnCastResult;
            voiceSocket.ErrorReceived -= OnServerError;
        }
        StopStream(cancel: true);
        collecting = false;
        ResetPushToTalk();
    }

    /// <summary>
    /// 마이크가 바뀌면 VAD 를 버리고, 다음 샘플이 들어올 때 새 샘플레이트로 다시 만든다 (소음 측정도 다시 함).
    /// 모으던 발화는 버린다. 결과 대기(막기) 상태는 이미 보낸 서버 요청과 관련되므로 그대로 둔다.
    /// </summary>
    void OnDeviceChanged()
    {
        bool wasCollecting = collecting;
        StopStream(cancel: true);
        vad = null;
        collecting = false;
        ignoringCurrentSpeech = false;
        utterance.Clear();
        ResetPushToTalk();   // 샘플레이트가 바뀔 수 있으므로 프리롤 버퍼도 다시 만든다
        if (wasCollecting) RecordingRejected?.Invoke("The microphone changed, so recording was canceled.");
        Debug.Log("[MIC] 마이크가 바뀌어 감지를 다시 시작합니다.");
    }

    void Start()
    {
        if (voiceInput == null) Debug.LogError("[MIC] voiceInput 이 비어 있습니다. Inspector 에서 VoiceInput 을 연결하세요.");
        if (voiceSocket == null) Debug.LogError("[MIC] voiceSocket 가 비어 있습니다. Inspector 에서 VoiceSocket 를 연결하세요.");
    }

    void Update()
    {
        // [막기] 안전장치: 서버가 응답하지 않아도 정해진 시간이 지나면 대기를 푼다
        if (waitingForResult && Time.unscaledTime >= waitDeadline)
        {
            MarkExpired(pendingRequestId ?? CurrentRequestId);   // 이 요청의 결과가 나중에 와도 쓰지 않는다
            ReleaseWait("response timeout");
            RecordingRejected?.Invoke("Reading the spell took too long.");   // 자막이 '해석 중'에 멈추지 않게 알린다
        }

        // 플레이 중 Inspector 에서 입력 방식을 바꾸면 모으던 발화를 버리고 새 방식으로 시작한다
        if (inputMode != appliedMode)
        {
            Debug.Log($"[MIC] 입력 방식 변경: {appliedMode} → {inputMode}");
            appliedMode = inputMode;
            CancelRecording("The input mode changed, so recording was canceled.");
            ignoringCurrentSpeech = false;
        }

        if (inputMode == InputMode.PushToTalk) UpdatePushToTalk();
    }

    // ─────────────────────────────────────────────
    // VoiceInput → VAD
    // ─────────────────────────────────────────────

    void OnSamples(float[] samples, int count)
    {
        if (vad == null) CreateVad();

        // 판정 대기 중에도 계산은 계속한다 (음량 표시, 소음 기준 유지).
        // 멈추지 않아야 대기가 풀린 직후 바로 정확하게 감지할 수 있다.
        vad.Process(samples, count);

        // 키 입력 모드: VAD 이벤트 대신 여기서 직접 소리를 모은다 (VAD 는 음량 표시용으로만 계속 돈다)
        if (inputMode == InputMode.PushToTalk) CollectPushToTalk(samples, count);
    }

    void CreateVad()
    {
        vad = new VoiceActivityDetector(vadSettings, voiceInput.SampleRate);
        vad.SpeechStarted += OnSpeechStarted;
        vad.SpeechFrame += OnSpeechFrame;
        vad.SpeechEnded += OnSpeechEnded;
    }

    void OnSpeechStarted(float[] preroll)
    {
        if (inputMode != InputMode.AutoDetect) return;   // 키 입력 모드에서는 VAD 의 말 시작·끝을 쓰지 않는다

        // [막기] 이전 주문의 판정이 아직 오지 않았으면 이번 발화는 통째로 무시한다.
        //        말하는 도중에 대기가 풀려도 이 발화는 끝까지 무시해서,
        //        앞부분이 잘린 반쪽짜리 음성이 서버로 가지 않게 한다.
        if (waitingForResult)
        {
            ignoringCurrentSpeech = true;
            Debug.Log("[MIC] 판정 대기 중이라 이번 발화는 무시합니다 (막기)");
            return;
        }

        // [받기 조건] 공격할 적이 없는 등 지금 주문을 받을 수 없으면 이번 발화는 끝까지 무시한다
        if (CanAccept != null && !CanAccept())
        {
            ignoringCurrentSpeech = true;
            Debug.Log("[MIC] 지금은 주문을 받을 수 없어 이번 발화는 무시합니다 (공격할 적 없음)");
            RecordingRejected?.Invoke("The enemy hasn't arrived yet.");
            return;
        }
        BeginUtterance(preroll);
    }

    void OnSpeechFrame(float[] frame, int count)
    {
        if (inputMode != InputMode.AutoDetect || !collecting || ignoringCurrentSpeech) return;
        for (int i = 0; i < count; i++) utterance.Add(frame[i]);   // 배열은 재사용되므로 바로 복사
        StreamSamples(frame, count);
    }

    void OnSpeechEnded(SpeechEndReason reason, int trailingSilenceSamples)
    {
        if (inputMode != InputMode.AutoDetect) return;

        // [막기] 무시하던 발화가 끝났다. 다음 발화부터 다시 받을 수 있다
        if (ignoringCurrentSpeech)
        {
            ignoringCurrentSpeech = false;
            return;
        }
        if (!collecting) return;

        // 끝부분 무음을 잘라낸다 (전송량과 STT 시간 감소). 끝 자음 보호용으로 조금은 남긴다
        int remove = trailingSilenceSamples - Mathf.RoundToInt(keepTrailingSilenceSec * voiceInput.SampleRate);
        if (remove > 0 && remove < utterance.Count)
            utterance.RemoveRange(utterance.Count - remove, remove);

        FinishUtterance(reason == SpeechEndReason.Silence ? "silence" : "max length");
    }

    // ─────────────────────────────────────────────
    // 키 입력 (PushToTalk)
    //   키 누름  → 받을 수 있는지 확인 → 프리롤 + 녹음 시작
    //   누르는 중 → 마이크 소리를 그대로 모음 (최대 길이에 닿으면 자동 전송)
    //   키 뗌    → pttReleaseTailSec 만큼 더 녹음 → 전송 (FinishUtterance 이후는 자동 감지와 같다)
    // ─────────────────────────────────────────────

    void UpdatePushToTalk()
    {
        if (Input.GetKeyDown(pushToTalkKey)) TryBeginPushToTalk();

        // GetKeyUp 대신 '지금 눌려 있는지'로 확인한다. 키를 뗀 프레임을 놓쳐도 녹음이 끝나지 않는 일이 없다
        if (pttHeld && !Input.GetKey(pushToTalkKey)) ReleasePushToTalk();

        // 꼬리 녹음 시간이 지나면 전송
        if (pttStopAt >= 0f && Time.unscaledTime >= pttStopAt)
        {
            pttStopAt = -1f;
            if (collecting) FinishUtterance("key released");
        }
    }

    void TryBeginPushToTalk()
    {
        // 이미 녹음 중이거나 꼬리 녹음 중이면 무시한다 (다시 눌러도 이어서 녹음하지 않음)
        if (collecting || pttStopAt >= 0f) return;

        if (voiceInput == null || !voiceInput.IsRunning)
        {
            Debug.LogWarning("[MIC] 마이크가 준비되지 않아 녹음을 시작할 수 없습니다.");
            RecordingRejected?.Invoke("The microphone isn't ready.");
            return;
        }

        // 일시정지 중(Time.timeScale 0)에는 받지 않는다
        if (Time.timeScale <= 0f)
        {
            Debug.Log("[MIC] 일시정지 중이라 녹음하지 않습니다.");
            return;
        }

        // [막기] 이전 주문의 판정이 아직 오지 않았으면 녹음하지 않는다 (자동 감지 모드처럼 로그만 남김)
        if (waitingForResult)
        {
            Debug.Log("[MIC] 판정 대기 중이라 녹음하지 않습니다 (막기)");
            return;
        }

        // [받기 조건] 공격할 적이 없는 등 지금 주문을 받을 수 없으면 녹음하지 않는다
        if (CanAccept != null && !CanAccept())
        {
            Debug.Log("[MIC] 지금은 주문을 받을 수 없어 녹음하지 않습니다 (공격할 적 없음)");
            RecordingRejected?.Invoke("The enemy hasn't arrived yet.");
            return;
        }

        pttHeld = true;
        BeginUtterance(TakePushToTalkPreroll());
    }

    void ReleasePushToTalk()
    {
        pttHeld = false;
        if (!collecting) return;

        if (pttReleaseTailSec > 0f) pttStopAt = Time.unscaledTime + pttReleaseTailSec;
        else FinishUtterance("key released");
    }

    /// <summary>VoiceInput 의 소리를 받는다. 녹음 중이면 발화에 모으고, 아니면 프리롤 버퍼에 보관한다.</summary>
    void CollectPushToTalk(float[] samples, int count)
    {
        if (!collecting)
        {
            PushPushToTalkPreroll(samples, count);
            return;
        }

        for (int i = 0; i < count; i++) utterance.Add(samples[i]);   // 배열은 재사용되므로 바로 복사
        StreamSamples(samples, count);

        // 최대 길이에 닿으면 키를 떼지 않았어도 바로 전송한다. 이후 키를 떼도 다시 보내지 않는다
        if (utterance.Count >= Mathf.FloorToInt(MaxRecordingSec * voiceInput.SampleRate))
        {
            pttHeld = false;
            pttStopAt = -1f;
            FinishUtterance("max length");
        }
    }

    void PushPushToTalkPreroll(float[] samples, int count)
    {
        int rate = voiceInput.SampleRate;
        int size = Mathf.RoundToInt(pttPrerollSec * rate);
        if (size <= 0) return;

        // 처음이거나 샘플레이트·길이 설정이 바뀌면 버퍼를 새로 만든다
        if (pttRing == null || pttRing.Length != size || pttRingRate != rate)
        {
            pttRing = new float[size];
            pttRingRate = rate;
            pttRingHead = 0;
            pttRingCount = 0;
        }

        for (int i = 0; i < count; i++)
        {
            pttRing[pttRingHead] = samples[i];
            pttRingHead = (pttRingHead + 1) % size;
            if (pttRingCount < size) pttRingCount++;
        }
    }

    /// <summary>프리롤 버퍼의 소리를 오래된 것부터 꺼내고 버퍼를 비운다. 비어 있으면 null.</summary>
    float[] TakePushToTalkPreroll()
    {
        if (pttRing == null || pttRingCount == 0) return null;

        var pre = new float[pttRingCount];
        int start = (pttRingHead - pttRingCount + pttRing.Length) % pttRing.Length;
        for (int i = 0; i < pttRingCount; i++) pre[i] = pttRing[(start + i) % pttRing.Length];

        pttRingHead = 0;
        pttRingCount = 0;   // 다음 녹음에 이번 소리가 섞이지 않게 비운다
        return pre;
    }

    void OnApplicationFocus(bool hasFocus)
    {
        // 키를 누른 채 다른 창으로 가면 키를 뗀 신호를 못 받을 수 있으므로 녹음을 버린다
        if (!hasFocus && inputMode == InputMode.PushToTalk && collecting)
            CancelRecording("The window changed, so recording was canceled.");
    }

    /// <summary>모으던 발화를 보내지 않고 버린다 (창 전환, 입력 방식 변경 등). 결과 대기 상태는 건드리지 않는다.</summary>
    void CancelRecording(string reason)
    {
        bool wasCollecting = collecting;
        StopStream(cancel: true);
        collecting = false;
        utterance.Clear();
        pttHeld = false;
        pttStopAt = -1f;

        if (wasCollecting)
        {
            Debug.Log($"[MIC] 녹음 취소: {reason}");
            RecordingRejected?.Invoke(reason);
        }
    }

    void ResetPushToTalk()
    {
        pttHeld = false;
        pttStopAt = -1f;
        pttRing = null;
        pttRingHead = 0;
        pttRingCount = 0;
    }

    // ─────────────────────────────────────────────
    // 발화 수집 → 전송
    // ─────────────────────────────────────────────

    void BeginUtterance(float[] preroll)
    {
        utterance.Clear();
        if (preroll != null) utterance.AddRange(preroll);
        collecting = true;
        CurrentRequestId = $"unity-mic-{++requestCounter}";   // RecordingStarted 전에 정해야 자막 쪽이 새 번호를 본다
        BeginStream(preroll);
        Debug.Log($"[MIC] 발화 시작 ({CurrentRequestId})");
        RecordingStarted?.Invoke();
    }

    void FinishUtterance(string why)
    {
        collecting = false;
        int sr = voiceInput.SampleRate;

        int maxCount = Mathf.FloorToInt(MaxRecordingSec * sr);
        if (utterance.Count > maxCount) utterance.RemoveRange(maxCount, utterance.Count - maxCount);

        float sec = (float)utterance.Count / sr;
        lastInfo = $"{sec:F2}s ({why})";

        if (sec < MinRecordingSec)
        {
            StopStream(cancel: true);
            Debug.LogWarning($"[MIC] 발화가 너무 짧아 보내지 않습니다 ({sec:F2}초).");
            RecordingRejected?.Invoke("No spell was heard.");
            return;
        }

        // 무음 판단에 쓸 peak 를 실시간 자막 마무리보다 먼저 구한다 (무음이면 stream_end 대신 stream_cancel 을 보내야 하므로)
        float[] data = utterance.ToArray();
        float peak = 0f;
        for (int i = 0; i < data.Length; i++) peak = Mathf.Max(peak, Mathf.Abs(data[i]));

        // 모노 1채널, 실제 샘플레이트를 WAV 헤더에 기록 (서버가 이 값을 보고 16kHz 로 맞춘다)
        byte[] wav = EncodeWav(data, sr);
        Debug.Log($"[MIC] 발화 끝: {sec:F2}초 ({why}), {sr}Hz, peak {peak:F3}, {wav.Length} bytes");

        UtteranceSent?.Invoke(wav);   // VoiceDebug 가 켜져 있으면 디버그 WAV 로 저장한다 (무음으로 막힌 녹음도 저장)

        // [무음 차단] 판정(/analyzing)은 보내지 않고, 실시간 자막(/streaming)은 취소한다.
        //   화면에는 실패 문구를 띄우지 않고 로그만 남긴다. 결과를 기다리지 않으므로 바로 다시 말할 수 있다.
        if (peak < silentPeak)
        {
            StopStream(cancel: true);
            Debug.LogWarning($"[MIC] 무음이라 판정을 보내지 않습니다 ({CurrentRequestId}, peak {peak:F3} < {silentPeak:F3}). " +
                             "계속되면 마이크 음소거, 장치 선택, 입력 볼륨을 확인하세요.");
            RecordingDiscarded?.Invoke();
            return;
        }

        StopStream(cancel: false);   // 남은 조각과 stream_end (서버가 마지막 확정 자막을 보낼 수 있다)

        if (voiceSocket == null)
        {
            RecordingRejected?.Invoke("No server connection is set up.");
            return;
        }
        SubmitUtterance(wav);
    }

    // ─────────────────────────────────────────────
    // 실시간 자막 전송 (VoiceStreamSocket → /ws/voice/streaming)
    //   발화 시작: stream_start + 프리롤 → 녹음 중: streamChunkSec 마다 PCM16 조각
    //   → 발화 끝: 남은 조각 + stream_end / 취소: stream_cancel
    //   연결이 없거나 꺼 두면 아무것도 하지 않는다 (최종 판정에는 영향 없음)
    // ─────────────────────────────────────────────

    void BeginStream(float[] preroll)
    {
        streamBuf.Clear();
        streaming = false;
        if (!streamPartials || streamSocket == null || !streamSocket.IsConnected) return;

        streamSocket.StartStream(CurrentRequestId, voiceInput.SampleRate);
        streaming = true;
        if (preroll != null) StreamSamples(preroll, preroll.Length);
    }

    /// <summary>녹음 중인 소리를 조각 버퍼에 모으고, streamChunkSec 만큼 찰 때마다 보낸다</summary>
    void StreamSamples(float[] samples, int count)
    {
        if (!streaming) return;
        for (int i = 0; i < count; i++) streamBuf.Add(samples[i]);   // 배열은 재사용되므로 바로 복사

        int chunk = Mathf.Max(1, Mathf.RoundToInt(streamChunkSec * voiceInput.SampleRate));
        int sent = 0;
        while (streamBuf.Count - sent >= chunk)
        {
            streamSocket.SendSamples(streamBuf, sent, chunk);
            sent += chunk;
        }
        if (sent > 0) streamBuf.RemoveRange(0, sent);
    }

    /// <summary>cancel = false: 남은 조각을 보내고 stream_end / cancel = true: stream_cancel</summary>
    void StopStream(bool cancel)
    {
        if (!streaming) return;
        streaming = false;
        if (streamSocket != null)
        {
            if (cancel)
            {
                streamSocket.CancelStream(CurrentRequestId);
            }
            else
            {
                if (streamBuf.Count > 0) streamSocket.SendSamples(streamBuf, 0, streamBuf.Count);
                streamSocket.EndStream(CurrentRequestId);
            }
        }
        streamBuf.Clear();
    }

    /// <summary>
    /// WAV 한 덩어리를 /analyzing 으로 보낸다 (최종 판정). 실시간 자막 조각은 StreamSamples 가 따로 보낸다.
    /// </summary>
    async void SubmitUtterance(byte[] wav)
    {
        // [막기] 전송하는 순간부터 판정이 올 때까지 새 주문을 받지 않는다.
        //        await 보다 먼저 켜야, 응답이 아주 빨리 와도 순서가 꼬이지 않는다.
        waitingForResult = true;
        waitDeadline = Time.unscaledTime + resultTimeoutSec;
        pendingRequestId = null;
        RecordingSubmitted?.Invoke();

        try
        {
            string id = await voiceSocket.SendWavWithId(wav, CurrentRequestId);   // 실시간 자막과 같은 번호
            if (id == null)
            {
                ReleaseWait("no server connection");
                RecordingRejected?.Invoke("Not connected to the server.");
                return;
            }
            // 결과가 이미 도착해 대기가 풀렸다면 번호를 기록할 필요가 없다
            if (waitingForResult) pendingRequestId = id;
        }
        catch (Exception e)
        {
            Debug.LogError("[MIC] 전송 실패: " + e);
            ReleaseWait("send failed");
            RecordingRejected?.Invoke("Failed to send the spell.");
        }
    }

    // ─────────────────────────────────────────────
    // 결과 수신 → 대기 해제 (막기 방식)
    // ─────────────────────────────────────────────

    void OnCastResult(CastResult r)
    {
        if (r == null) return;
        if (IsStaleResult(r.RequestId))
        {
            LogStale(r.RequestId, r.Success ? $"발동 {r.Skill?.Name}" : r.RejectReason);
            return;
        }
        if (IsMyResult(r.RequestId))
            ReleaseWait(r.Success ? "cast" : $"rejected {r.RejectReason}");
    }

    void OnServerError(ErrorResponse err)
    {
        if (err == null) return;
        if (IsStaleResult(err.RequestId))
        {
            LogStale(err.RequestId, $"error {err.Code}");
            return;
        }
        if (IsMyResult(err.RequestId))
            ReleaseWait($"error {err.Code}");
    }

    /// <summary>
    /// 이미 쓸모없어진(늦게 도착한) 결과인지. VoiceRecorder(자막·시전)와 PlayerHealth(벌칙)가 결과를 쓰기 전에 확인한다.
    ///  - 시간 초과로 기다리기를 포기한 요청의 결과
    ///  - 그 뒤에 새 주문을 말해서 가장 최근 요청이 아닌 결과
    /// 미리 기억해 둔 정보만 보므로, 어느 스크립트가 결과를 먼저 받아도 같은 답이 나온다.
    /// 요청 번호가 없는 결과는 판단할 수 없으므로 버리지 않는다 (기존 동작 유지).
    /// </summary>
    public bool IsStaleResult(string requestId)
    {
        if (string.IsNullOrEmpty(requestId)) return false;
        if (expiredRequests.ContainsKey(requestId)) return true;
        return CurrentRequestId != null && requestId != CurrentRequestId;
    }

    /// <summary>시간 초과로 포기한 요청을 기억한다 (보낸 시각은 로그의 '도착까지 n초' 계산용)</summary>
    void MarkExpired(string requestId)
    {
        if (string.IsNullOrEmpty(requestId) || expiredRequests.ContainsKey(requestId)) return;
        expiredRequests[requestId] = waitDeadline - resultTimeoutSec;
        expiredOrder.Enqueue(requestId);
        while (expiredOrder.Count > MaxExpiredRequests)
            expiredRequests.Remove(expiredOrder.Dequeue());
    }

    void LogStale(string requestId, string what)
    {
        if (expiredRequests.TryGetValue(requestId, out float sentAt))
            Debug.Log($"[MIC] 시간 초과된 결과를 버립니다 ({requestId}: {what}, 도착까지 {Time.unscaledTime - sentAt:F1}초)");
        else
            Debug.Log($"[MIC] 이전 요청의 결과를 버립니다 ({requestId}: {what}, 지금 요청 {CurrentRequestId})");
    }

    /// <summary>
    /// [막기] 지금 기다리는 요청의 결과일 때만 대기를 푼다.
    ///  - 시간 초과로 이미 포기한 예전 요청의 결과가 늦게 와도, 새 요청의 대기를 잘못 풀지 않는다.
    ///  - request_id 가 다른 응답으로 대기가 풀리지 않는다.
    ///  - 요청 번호를 아직 모르는 아주 짧은 순간(pendingRequestId == null)에는 도착한 결과를 내 것으로 본다.
    /// </summary>
    bool IsMyResult(string requestId)
    {
        return waitingForResult && (pendingRequestId == null || requestId == pendingRequestId);
    }

    /// <summary>[막기] 대기를 풀고 다음 주문을 받을 수 있게 한다.</summary>
    void ReleaseWait(string why)
    {
        if (!waitingForResult) return;
        waitingForResult = false;
        pendingRequestId = null;
        Debug.Log($"[MIC] 결과 대기 해제: {why}");
    }

    // ─────────────────────────────────────────────
    // WAV 변환 (기존 WavEncoder 를 옮겨 옴)
    // ─────────────────────────────────────────────

    /// <summary>
    /// 모노 float(-1~1) 샘플을 PCM16 WAV 바이트(44바이트 헤더 포함)로 변환한다.
    /// sampleRate 에는 실제 데이터의 값(VoiceInput.SampleRate)을 넣어야 서버가 16kHz 로 올바르게 맞춘다.
    /// </summary>
    static byte[] EncodeWav(float[] samples, int sampleRate)
    {
        const int channels = 1;
        int dataBytes = samples.Length * 2;   // 16비트 = 2바이트

        using (var ms = new MemoryStream(44 + dataBytes))
        using (var w = new BinaryWriter(ms))  // 리틀엔디언으로 기록 (WAV 규격과 동일)
        {
            // RIFF 헤더
            w.Write(Encoding.ASCII.GetBytes("RIFF"));
            w.Write(36 + dataBytes);
            w.Write(Encoding.ASCII.GetBytes("WAVE"));

            // fmt 청크
            w.Write(Encoding.ASCII.GetBytes("fmt "));
            w.Write(16);                              // fmt 청크 크기
            w.Write((short)1);                        // 1 = PCM
            w.Write((short)channels);
            w.Write(sampleRate);
            w.Write(sampleRate * channels * 2);       // 초당 바이트 수
            w.Write((short)(channels * 2));           // 샘플 1개(전 채널)의 바이트 수
            w.Write((short)16);                       // 비트 수

            // data 청크
            w.Write(Encoding.ASCII.GetBytes("data"));
            w.Write(dataBytes);
            for (int i = 0; i < samples.Length; i++)
            {
                float s = Mathf.Clamp(samples[i], -1f, 1f);
                w.Write((short)(s * 32767f));
            }

            w.Flush();
            return ms.ToArray();
        }
    }
}