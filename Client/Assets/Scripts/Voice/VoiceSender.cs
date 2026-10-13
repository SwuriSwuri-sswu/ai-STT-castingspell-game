using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

/// <summary>
/// VoiceInput(소리) → VoiceActivityDetector(말 구간 감지) → WAV 변환·전송 → 결과 대기 를 조정한다.
///
/// 입력: VAD 가 말 시작·끝을 감지해 자동으로 전송한다 (자동 감지만 사용).
///
/// B-1 단계: 말이 끝나면 발화를 WAV 한 덩어리로 보낸다 (기존 cast_header + WAV 규칙).
///          B-2 에서는 SubmitUtterance 부분만 조각 전송으로 바뀐다.
///
/// [막기 방식]
///   주문 하나를 보내면 서버 판정이 돌아올 때까지 새 주문을 받지 않는다.
///   대기 중에 말한 주문은 보관하지 않고 버린다.
///   판정 결과(또는 오류)가 오거나, 정해진 시간이 지나면 대기를 푼다.
/// </summary>
public class VoiceSender : MonoBehaviour
{
    [Header("연결")]
    [SerializeField] VoiceInput voiceInput;
    [SerializeField] VoiceSocket voiceSocket;

    [Header("자동 감지 설정")]
    [SerializeField] VadSettings vadSettings = new VadSettings();

    [Header("결과 대기 (막기 방식)")]
    [Tooltip("서버 응답이 이 시간(초) 안에 오지 않으면 대기를 강제로 푼다. 게임이 멈추지 않게 하는 안전장치")]
    [SerializeField] float resultTimeoutSec = 3f;

    [Header("발화 다듬기")]
    [Tooltip("말 끝 무음 중 남겨 둘 길이(초). 끝 자음이 잘리지 않게 약간 남긴다")]
    [SerializeField] float keepTrailingSilenceSec = 0.1f;

    const float MinRecordingSec = 0.15f;   // 서버 최소 0.1초 + 여유
    const float MaxRecordingSec = 4.9f;    // 서버 최대 5초보다 약간 짧게 (프레임 단위 오차 대비)
    const float SilentPeak = 0.01f;        // 이보다 작으면 소리가 거의 없는 것으로 판단

    /// <summary>현재 VAD (디버그 표시용). 장치 변경 등으로 다시 만들어지기 전에는 null 일 수 있다.</summary>
    public VoiceActivityDetector Vad => vad;

    // ── 게임(자막 연출 등)에서 구독하는 이벤트 ──
    public event Action RecordingStarted;            // 발화 수집 시작
    public event Action RecordingSubmitted;          // 발화 전송 시작
    public event Action<string> RecordingRejected;   // 전송하지 못함 (사유)
    public event Action<byte[]> UtteranceSent;       // 서버로 보낸 WAV (VoiceDebug 의 디버그 저장용)

    public bool IsRecording => collecting;
    public bool IsWaitingForResult => waitingForResult;
    public string LastInfo => lastInfo;              // 마지막 발화 정보 (디버그 표시용)

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
        if (voiceInput == null) Debug.LogError("[MIC] voiceInput 이 비어 있습니다. Inspector 에서 VoiceInput 을 연결하세요.");
        if (voiceSocket == null) Debug.LogError("[MIC] voiceSocket 가 비어 있습니다. Inspector 에서 VoiceSocket 를 연결하세요.");
    }

    void Update()
    {
        // [막기] 안전장치: 서버가 응답하지 않아도 정해진 시간이 지나면 대기를 푼다
        if (waitingForResult && Time.unscaledTime >= waitDeadline)
        {
            ReleaseWait("응답 시간 초과");
            RecordingRejected?.Invoke("주문 해석 시간이 초과되었습니다.");   // 자막이 '해석 중'에 멈추지 않게 알린다
        }
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
        if (!collecting || ignoringCurrentSpeech) return;
        for (int i = 0; i < count; i++) utterance.Add(frame[i]);   // 배열은 재사용되므로 바로 복사
    }

    void OnSpeechEnded(SpeechEndReason reason, int trailingSilenceSamples)
    {
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
        Debug.Log("[MIC] 발화 시작");
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
            Debug.LogWarning($"[MIC] 발화가 너무 짧아 보내지 않습니다 ({sec:F2}초).");
            RecordingRejected?.Invoke("주문이 들리지 않았습니다.");
            return;
        }

        float[] data = utterance.ToArray();
        float peak = 0f;
        for (int i = 0; i < data.Length; i++) peak = Mathf.Max(peak, Mathf.Abs(data[i]));

        // 모노 1채널, 실제 샘플레이트를 WAV 헤더에 기록 (서버가 이 값을 보고 16kHz 로 맞춘다)
        byte[] wav = EncodeWav(data, sr);
        Debug.Log($"[MIC] 발화 끝: {sec:F2}초 ({why}), {sr}Hz, peak {peak:F3}, {wav.Length} bytes");

        if (peak < SilentPeak)
            Debug.LogWarning("[MIC] 소리가 거의 들어오지 않았습니다. 마이크 음소거, 장치 선택, 입력 볼륨을 확인하세요.");

        UtteranceSent?.Invoke(wav);   // VoiceDebug 가 켜져 있으면 디버그 WAV 로 저장한다

        if (voiceSocket == null)
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
            string id = await voiceSocket.SendWavBytes(wav, "mic");
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