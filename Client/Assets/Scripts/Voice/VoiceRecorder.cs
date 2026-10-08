using System;
using System.IO;
using UnityEngine;

/// <summary>
/// 스페이스바를 누르는 동안 마이크로 녹음하고, 떼면 PCM16 WAV로 변환해 서버로 전송한다.
/// (VAD 적용 전 단계: 누르고 말하기 방식)
/// </summary>
public class VoiceRecorder : MonoBehaviour
{
    [SerializeField] VoiceWsTest wsClient;      // Inspector에서 같은 씬의 VoiceWsTest 연결
    [SerializeField] bool saveDebugWav = true;  // 보낸 WAV를 파일로도 저장 (문제 확인용)

    const int RequestedSampleRate = 16000;      // 요청값. 실제 값은 clip.frequency 를 사용
    const int MaxRecordingSec = 5;              // 서버 최대 길이(5초)와 맞춤
    const float MinRecordingSec = 0.15f;        // 서버 최소 길이(0.1초) + 여유
    const float SilentPeak = 0.01f;             // 이보다 작으면 소리가 거의 없는 것으로 판단

    string device;
    AudioClip clip;
    bool recording;

    void Start()
    {
        if (Microphone.devices.Length == 0)
        {
            Debug.LogError("[MIC] 사용할 수 있는 마이크가 없습니다.");
            return;
        }

        for (int i = 0; i < Microphone.devices.Length; i++)
            Debug.Log($"[MIC] 장치 {i}: {Microphone.devices[i]}");

        device = Microphone.devices[0];
        Microphone.GetDeviceCaps(device, out int minFreq, out int maxFreq);
        Debug.Log($"[MIC] 사용 장치: {device} (지원 범위 {minFreq}~{maxFreq} Hz, 0~0이면 제한 없음)");

        if (wsClient == null)
            Debug.LogError("[MIC] wsClient 가 비어 있습니다. Inspector에서 VoiceWsTest 를 연결하세요.");
    }

    void Update()
    {
        if (string.IsNullOrEmpty(device)) return;

        if (Input.GetKeyDown(KeyCode.Space)) StartRecording();
        if (Input.GetKeyUp(KeyCode.Space)) StopRecording(false);

        // 최대 시간에 도달하면 Unity가 녹음을 자동으로 멈춘다. 이 경우도 전송한다.
        if (recording && !Microphone.IsRecording(device)) StopRecording(true);
    }

    void StartRecording()
    {
        if (recording) return;
        if (Microphone.IsRecording(device)) Microphone.End(device);

        clip = Microphone.Start(device, false, MaxRecordingSec, RequestedSampleRate);
        if (clip == null)
        {
            Debug.LogError("[MIC] 녹음을 시작하지 못했습니다.");
            return;
        }

        recording = true;
        Debug.Log($"[MIC] 녹음 시작 (요청 {RequestedSampleRate} Hz → 실제 {clip.frequency} Hz, {clip.channels}ch). " +
                  "키를 누르고 잠깐 뒤에 말하세요.");
    }

    void StopRecording(bool autoStopped)
    {
        if (!recording) return;
        recording = false;

        // 자동으로 멈췄다면 클립 전체, 아니면 현재 녹음 위치까지
        int samples = autoStopped ? clip.samples : Microphone.GetPosition(device);
        Microphone.End(device);

        float sec = (float)samples / clip.frequency;
        if (samples <= 0 || sec < MinRecordingSec)
        {
            Debug.LogWarning($"[MIC] 녹음이 너무 짧아 보내지 않습니다 ({sec:F2}초).");
            return;
        }

        // 실제로 녹음된 구간만 꺼낸다
        float[] data = new float[samples * clip.channels];
        clip.GetData(data, 0);

        float peak = 0f;
        for (int i = 0; i < data.Length; i++)
            peak = Mathf.Max(peak, Mathf.Abs(data[i]));

        // WAV 헤더에는 반드시 실제 샘플레이트와 채널 수를 기록
        byte[] wav = WavEncoder.FromSamples(data, clip.channels, clip.frequency);
        Debug.Log($"[MIC] 녹음 완료: {sec:F2}초, {clip.frequency} Hz, {clip.channels}ch, " +
                  $"peak {peak:F3}, {wav.Length} bytes" + (autoStopped ? " (최대 시간 도달)" : ""));

        if (peak < SilentPeak)
            Debug.LogWarning("[MIC] 소리가 거의 들어오지 않았습니다. 마이크 음소거, 장치 선택, 입력 볼륨을 확인하세요.");

        if (saveDebugWav) SaveDebugWav(wav);

        if (wsClient == null)
        {
            Debug.LogError("[MIC] wsClient 가 없어 전송하지 못했습니다.");
            return;
        }
        _ = wsClient.SendWavBytes(wav, "mic");
    }

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
        GUILayout.BeginArea(new Rect(10, 140, 280, 40));
        GUILayout.Label(recording ? "● REC (release SPACE to send)" : "Hold SPACE to record");
        GUILayout.EndArea();
    }

    void OnDisable()
    {
        if (recording && !string.IsNullOrEmpty(device)) Microphone.End(device);
        recording = false;
    }
}