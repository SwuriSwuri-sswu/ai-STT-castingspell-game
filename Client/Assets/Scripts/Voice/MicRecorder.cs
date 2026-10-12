using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

/// <summary>
/// MicStream(소리) → VoiceActivityDetector(말 구간 감지) → WAV 전송 → 결과 대기 를 조정한다.
///
/// 입력 모드
///   - PushToTalk : 녹음 키를 누르는 동안 모아서 떼면 전송 (기존 방식)
///   - AutoDetect : VAD 가 말 시작·끝을 감지해 자동 전송 (B-1)
///
/// B-1 단계: 말이 끝나면 발화를 WAV 한 덩어리로 보낸다 (기존 cast_header + WAV 규칙).
///          B-2 에서는 SubmitUtterance 부분만 조각 전송으로 바뀐다.
///
/// [막기 방식]
///   주문 하나를 보내면 서버 판정이 돌아올 때까지 새 주문을 받지 않는다.
///   대기 중에 말한 주문은 보관하지 않고 버린다.
///   판정 결과(또는 오류)가 오거나, 정해진 시간이 지나면 대기를 푼다.
/// </summary>
public class MicRecorder : MonoBehaviour
{
    public enum InputMode { PushToTalk, AutoDetect }

    [Header("연결")]
    [SerializeField] MicStream micStream;
    [SerializeField] VoiceWsTest wsClient;

    [Header("입력 모드")]
    [SerializeField] InputMode inputMode = InputMode.AutoDetect;
    [SerializeField] KeyCode recordKey = KeyCode.Space;   // PushToTalk 에서만 사용

    [Header("자동 감지 설정")]
    [SerializeField] VadSettings vadSettings = new VadSettings();

    [Header("결과 대기 (막기 방식)")]
    [Tooltip("서버 응답이 이 시간(초) 안에 오지 않으면 대기를 강제로 푼다. 게임이 멈추지 않게 하는 안전장치")]
    [SerializeField] float resultTimeoutSec = 3f;

    [Header("발화 다듬기")]
    [Tooltip("말 끝 무음 중 남겨 둘 길이(초). 끝 자음이 잘리지 않게 약간 남긴다")]
    [SerializeField] float keepTrailingSilenceSec = 0.1f;

    [Header("디버그")]
    [SerializeField] bool saveDebugWav = true;   // 보낸 WAV 를 파일로 저장 (빌드 시 끌 것)
    [SerializeField] bool showDebugGui = true;   // 화면 왼쪽 위 상태 표시 (빌드 시 끌 것)

    const float MinRecordingSec = 0.15f;   // 서버 최소 0.1초 + 여유
    const float MaxRecordingSec = 4.9f;    // 서버 최대 5초보다 약간 짧게 (프레임 단위 오차 대비)
    const float SilentPeak = 0.01f;        // 이보다 작으면 소리가 거의 없는 것으로 판단

    /// <summary>현재 VAD (디버그 표시용). 장치 변경 등으로 다시 만들어지기 전에는 null 일 수 있다.</summary>
    public VoiceActivityDetector Vad => vad;

    // ── 게임(자막 연출 등)에서 구독하는 이벤트 ──
    public event Action RecordingStarted;            // 발화 수집 시작
    public event Action RecordingSubmitted;          // 발화 전송 시작
    public event Action<string> RecordingRejected;   // 전송하지 못함 (사유)

    public bool IsRecording => collecting;
    public bool IsWaitingForResult => waitingForResult;

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

    void OnEnable()
    {
        if (micStream != null) micStream.DeviceChanged += OnDeviceChanged;
        if (micStream != null) micStream.SamplesAvailable += OnSamples;
        if (wsClient != null)
        {
            wsClient.CastResultReceived += OnCastResult;
            wsClient.ErrorReceived += OnServerError;
        }
    }

    void OnDisable()
    {
        if (micStream != null) micStream.DeviceChanged -= OnDeviceChanged;
        if (micStream != null) micStream.SamplesAvailable -= OnSamples;
        if (wsClient != null)
        {
            wsClient.CastResultReceived -= OnCastResult;
            wsClient.ErrorReceived -= OnServerError;
        }
        collecting = false;
    }

    /// <summary>
    /// 마이크가 바뀌면 VAD 를 버리고, 다음 샘플이 들어올 때 새 샘플레이트로 다시 만든다 (소음 측정도 다시 함).
    /// 모으던 발화는 버린다. 결과 대기(막기) 상태는 이미 보낸 서버 요청과 관련되므로 그대로 둔다.
    /// </summary>
    void OnDeviceChanged()
    {
        vad = null;
        collecting = false;
        ignoringCurrentSpeech = false;
        utterance.Clear();
        Debug.Log("[MIC] 마이크가 바뀌어 감지를 다시 시작합니다.");
    }

    void Start()
    {
        if (micStream == null) Debug.LogError("[MIC] micStream 이 비어 있습니다. Inspector 에서 MicStream 을 연결하세요.");
        if (wsClient == null) Debug.LogError("[MIC] wsClient 가 비어 있습니다. Inspector 에서 VoiceWsTest 를 연결하세요.");
    }

    void Update()
    {
        // [막기] 안전장치: 서버가 응답하지 않아도 정해진 시간이 지나면 대기를 푼다
        if (waitingForResult && Time.unscaledTime >= waitDeadline)
            ReleaseWait("응답 시간 초과");

        if (inputMode != InputMode.PushToTalk) return;

        if (Input.GetKeyDown(recordKey))
        {
            // [막기] 판정 대기 중에는 키를 눌러도 녹음을 시작하지 않는다
            if (waitingForResult)
            {
                Debug.Log("[MIC] 판정 대기 중이라 녹음을 시작하지 않습니다 (막기)");
                return;
            }
            BeginUtterance(null);
        }
        if (Input.GetKeyUp(recordKey) && collecting) FinishUtterance("키 뗌");
    }

    // ─────────────────────────────────────────────
    // MicStream → VAD
    // ─────────────────────────────────────────────

    void OnSamples(float[] samples, int count)
    {
        if (vad == null) CreateVad();

        // 두 모드 모두 계산은 계속한다 (음량 표시, 소음 기준 유지).
        // 대기 중에도 멈추지 않아야 대기가 풀린 직후 바로 정확하게 감지할 수 있다.
        vad.Process(samples, count);

        // 누르고 말하기: 키를 누르고 있는 동안 들어온 소리를 그대로 모은다
        if (inputMode == InputMode.PushToTalk && collecting)
        {
            for (int i = 0; i < count; i++) utterance.Add(samples[i]);
            if (utterance.Count >= MaxRecordingSec * micStream.SampleRate) FinishUtterance("최대 길이");
        }
    }

    void CreateVad()
    {
        vad = new VoiceActivityDetector(vadSettings, micStream.SampleRate);
        vad.SpeechStarted += OnSpeechStarted;
        vad.SpeechFrame += OnSpeechFrame;
        vad.SpeechEnded += OnSpeechEnded;
    }

    void OnSpeechStarted(float[] preroll)
    {
        if (inputMode != InputMode.AutoDetect) return;

        // [막기] 이전 주문의 판정이 아직 오지 않았으면 이번 발화는 통째로 무시한다.
        //        말하는 도중에 대기가 풀려도 이 발화는 끝까지 무시해서,
        //        앞부분이 잘린 반쪽짜리 음성이 서버로 가지 않게 한다.
        if (waitingForResult)
        {
            ignoringCurrentSpeech = true;
            Debug.Log("[MIC] 판정 대기 중이라 이번 발화는 무시합니다 (막기)");
            return;
        }
        BeginUtterance(preroll);
    }

    void OnSpeechFrame(float[] frame, int count)
    {
        if (inputMode != InputMode.AutoDetect || !collecting || ignoringCurrentSpeech) return;
        for (int i = 0; i < count; i++) utterance.Add(frame[i]);   // 배열은 재사용되므로 바로 복사
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
        int remove = trailingSilenceSamples - Mathf.RoundToInt(keepTrailingSilenceSec * micStream.SampleRate);
        if (remove > 0 && remove < utterance.Count)
            utterance.RemoveRange(utterance.Count - remove, remove);

        FinishUtterance(reason == SpeechEndReason.Silence ? "무음" : "최대 길이");
    }

    // ─────────────────────────────────────────────
    // 발화 수집 → 전송
    // ─────────────────────────────────────────────

    void BeginUtterance(float[] preroll)
    {
        utterance.Clear();
        if (preroll != null) utterance.AddRange(preroll);
        collecting = true;
        Debug.Log($"[MIC] 발화 시작 ({inputMode})");
        RecordingStarted?.Invoke();
    }

    void FinishUtterance(string why)
    {
        collecting = false;
        int sr = micStream.SampleRate;

        int maxCount = Mathf.FloorToInt(MaxRecordingSec * sr);
        if (utterance.Count > maxCount) utterance.RemoveRange(maxCount, utterance.Count - maxCount);

        float sec = (float)utterance.Count / sr;
        lastInfo = $"{sec:F2}s ({why})";

        if (sec < MinRecordingSec)
        {
            Debug.LogWarning($"[MIC] 발화가 너무 짧아 보내지 않습니다 ({sec:F2}초).");
            RecordingRejected?.Invoke("주문이 들리지 않았습니다.");
            return;
        }

        float[] data = utterance.ToArray();
        float peak = 0f;
        for (int i = 0; i < data.Length; i++) peak = Mathf.Max(peak, Mathf.Abs(data[i]));

        // 모노 1채널, 실제 샘플레이트를 WAV 헤더에 기록 (서버가 이 값을 보고 16kHz 로 맞춘다)
        byte[] wav = WavEncoder.FromSamples(data, 1, sr);
        Debug.Log($"[MIC] 발화 끝: {sec:F2}초 ({why}), {sr}Hz, peak {peak:F3}, {wav.Length} bytes");

        if (peak < SilentPeak)
            Debug.LogWarning("[MIC] 소리가 거의 들어오지 않았습니다. 마이크 음소거, 장치 선택, 입력 볼륨을 확인하세요.");

        if (saveDebugWav) SaveDebugWav(wav);

        if (wsClient == null)
        {
            RecordingRejected?.Invoke("서버 연결 설정이 없습니다.");
            return;
        }
        SubmitUtterance(wav);
    }

    /// <summary>
    /// B-1: WAV 한 덩어리 전송. B-2 에서는 이 메서드가 stream_start / 조각 / stream_end 전송으로 바뀐다.
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
            string id = await wsClient.SendWavBytes(wav, "mic");
            if (id == null)
            {
                ReleaseWait("서버 연결 없음");
                RecordingRejected?.Invoke("서버에 연결되어 있지 않습니다.");
                return;
            }
            // 결과가 이미 도착해 대기가 풀렸다면 번호를 기록할 필요가 없다
            if (waitingForResult) pendingRequestId = id;
        }
        catch (Exception e)
        {
            Debug.LogError("[MIC] 전송 실패: " + e);
            ReleaseWait("전송 실패");
            RecordingRejected?.Invoke("주문 전송에 실패했습니다.");
        }
    }

    // ─────────────────────────────────────────────
    // 결과 수신 → 대기 해제 (막기 방식)
    // ─────────────────────────────────────────────

    void OnCastResult(CastResult r)
    {
        if (r != null && IsMyResult(r.RequestId))
            ReleaseWait(r.Success ? "발동" : $"불발 {r.RejectReason}");
    }

    void OnServerError(ErrorResponse err)
    {
        if (err != null && IsMyResult(err.RequestId))
            ReleaseWait($"오류 {err.Code}");
    }

    /// <summary>
    /// [막기] 지금 기다리는 요청의 결과일 때만 대기를 푼다.
    ///  - 시간 초과로 이미 포기한 예전 요청의 결과가 늦게 와도, 새 요청의 대기를 잘못 풀지 않는다.
    ///  - 테스트 버튼(Send hello, Send WAV)의 응답으로 대기가 풀리지 않는다.
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
    // 디버그
    // ─────────────────────────────────────────────

    void SaveDebugWav(byte[] wav)
    {
        string dir = Path.Combine(Application.persistentDataPath, "mic_debug");
        Directory.CreateDirectory(dir);
        string path = Path.Combine(dir, $"mic_{DateTime.Now:yyyyMMdd_HHmmss_fff}.wav");
        File.WriteAllBytes(path, wav);
        Debug.Log($"[MIC] 디버그 WAV 저장: {path}");
    }

    void OnGUI()
    {
        if (!showDebugGui) return;
        GUILayout.BeginArea(new Rect(10, 140, 420, 110));
        GUILayout.Label($"Mode: {inputMode}   Source: {(micStream != null ? micStream.Source.ToString() : "-")}");
        if (vad != null)
            GUILayout.Label($"VAD: {vad.State}   {vad.CurrentDb:F1} dB   (noise {vad.NoiseFloorDb:F1} / threshold {vad.ThresholdDb:F1})");
        GUILayout.Label(collecting ? "● REC" : waitingForResult ? "... waiting for result (blocked)" : "Ready");
        GUILayout.Label($"Last: {lastInfo}");
        GUILayout.EndArea();
    }
}