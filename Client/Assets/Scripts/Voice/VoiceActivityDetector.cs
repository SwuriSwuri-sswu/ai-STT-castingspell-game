using System;
using UnityEngine;

/// <summary>VAD 설정. VoiceSender 의 Inspector 에서 조정한다. 값은 모두 시작값이며 테스트하며 바꾼다.</summary>
[Serializable]
public class VadSettings
{
    [Tooltip("음량을 계산하는 단위 (ms)")]
    public float frameMs = 30f;
    [Tooltip("시작 직후 주변 소음을 재는 시간 (초). 이 동안은 말해도 감지하지 않는다")]
    public float calibrationSec = 1f;
    [Tooltip("소음 기준보다 이만큼(dB) 크면 말소리로 본다")]
    public float thresholdAboveNoiseDb = 10f;
    [Tooltip("소음 기준의 최솟값 (dB). 완전 무음일 때 기준이 너무 낮아져 작은 소리에도 반응하는 것을 막는다")]
    public float minNoiseFloorDb = -60f;
    [Tooltip("말소리 프레임이 이만큼 연속되면 말 시작")]
    public int startFrames = 3;
    [Tooltip("조용한 구간이 이만큼(초) 이어지면 말 끝")]
    public float endSilenceSec = 0.4f;
    [Tooltip("말 시작 직전 구간을 함께 보낼 길이 (초). 첫 음절 잘림 방지")]
    public float prerollSec = 0.25f;
    [Tooltip("이 길이(초)에 도달하면 강제로 말 끝. 서버 최대 5초보다 짧게")]
    public float maxUtteranceSec = 4.8f;
    [Tooltip("대기 중 소음 기준을 천천히 따라가게 할지 (방 소음 변화 대응)")]
    public bool adaptNoiseFloor = true;
    [Tooltip("소음 기준을 따라가는 속도 (프레임당, 0~1)")]
    public float noiseAdaptRate = 0.02f;
}

public enum VadState { Calibrating, Idle, Speaking }
public enum SpeechEndReason { Silence, MaxLength }

/// <summary>
/// 음량 기반 말 구간 감지기. Unity 컴포넌트가 아닌 일반 클래스라서 파일 입력으로 반복 시험하기 쉽다.
/// 흐름: 샘플 → 30ms 프레임 → 음량(dB) → 상태 기계 → 이벤트
/// </summary>
public class VoiceActivityDetector
{
    /// <summary>말 시작. 프리롤(시작 직전 구간)을 새 배열로 넘긴다.</summary>
    public event Action<float[]> SpeechStarted;
    /// <summary>말하는 중 프레임. 배열은 재사용되므로 받는 쪽이 즉시 복사해야 한다.</summary>
    public event Action<float[], int> SpeechFrame;
    /// <summary>말 끝. (사유, 끝부분 무음 샘플 수) — 받는 쪽이 끝 무음을 잘라낼 때 쓴다.</summary>
    public event Action<SpeechEndReason, int> SpeechEnded;

    public VadState State { get; private set; } = VadState.Calibrating;
    public float CurrentDb { get; private set; } = -100f;
    public float NoiseFloorDb { get; private set; }
    public float ThresholdDb => NoiseFloorDb + s.thresholdAboveNoiseDb;
    public int SampleRate { get; }

    readonly VadSettings s;
    readonly int frameSize, calibrationFrames, endSilenceSamples, maxSamples;
    readonly float[] frame;
    int frameFill;

    // 프리롤: 최근 프레임을 보관하는 원형 버퍼 (오래된 것부터 덮어씀)
    readonly float[] preroll;
    readonly int prerollFrames;
    int prerollHead, prerollCount;

    int calibCount;
    double calibSum;
    int voicedRun;          // 연속 말소리 프레임 수
    int silentSamples;      // 말하는 중 이어진 무음 샘플 수
    int utteranceSamples;   // 현재 발화 길이 (프리롤 포함)

    public VoiceActivityDetector(VadSettings settings, int sampleRate)
    {
        s = settings;
        SampleRate = sampleRate;

        frameSize = Mathf.Max(1, Mathf.RoundToInt(sampleRate * s.frameMs / 1000f));
        calibrationFrames = Mathf.Max(1, Mathf.RoundToInt(s.calibrationSec * sampleRate / frameSize));
        endSilenceSamples = Mathf.RoundToInt(s.endSilenceSec * sampleRate);
        maxSamples = Mathf.RoundToInt(s.maxUtteranceSec * sampleRate);

        // 프리롤은 말 시작 판정에 쓰인 프레임 수보다 길어야 첫 음절이 들어간다
        prerollFrames = Mathf.Max(s.startFrames, Mathf.CeilToInt(s.prerollSec * sampleRate / frameSize));
        preroll = new float[prerollFrames * frameSize];
        frame = new float[frameSize];

        NoiseFloorDb = s.minNoiseFloorDb;
    }

    /// <summary>샘플을 넣는다. 30ms 프레임이 찰 때마다 판정한다.</summary>
    public void Process(float[] samples, int count)
    {
        for (int i = 0; i < count; i++)
        {
            frame[frameFill++] = samples[i];
            if (frameFill == frameSize)
            {
                ProcessFrame();
                frameFill = 0;
            }
        }
    }

    void ProcessFrame()
    {
        // ① 음량 (RMS → dB)
        double sum = 0;
        for (int i = 0; i < frameSize; i++) sum += frame[i] * frame[i];
        float rms = (float)Math.Sqrt(sum / frameSize);
        CurrentDb = 20f * Mathf.Log10(rms + 1e-9f);
        bool voiced = CurrentDb >= ThresholdDb;

        // ② 상태 기계
        switch (State)
        {
            case VadState.Calibrating:
                PushPreroll();
                calibSum += CurrentDb;
                calibCount++;
                if (calibCount >= calibrationFrames)
                {
                    NoiseFloorDb = Mathf.Max((float)(calibSum / calibCount), s.minNoiseFloorDb);
                    State = VadState.Idle;
                    Debug.Log($"[VAD] 소음 기준 {NoiseFloorDb:F1}dB, 말소리 기준 {ThresholdDb:F1}dB");
                }
                break;

            case VadState.Idle:
                PushPreroll();   // 판정 전에 먼저 보관해야 시작 판정에 쓰인 프레임도 프리롤에 들어간다
                if (voiced)
                {
                    voicedRun++;
                }
                else
                {
                    voicedRun = 0;
                    if (s.adaptNoiseFloor)   // 조용할 때만 소음 기준을 천천히 따라간다
                        NoiseFloorDb = Mathf.Max(Mathf.Lerp(NoiseFloorDb, CurrentDb, s.noiseAdaptRate), s.minNoiseFloorDb);
                }
                if (voicedRun >= s.startFrames) BeginSpeech();
                break;

            case VadState.Speaking:
                utteranceSamples += frameSize;
                SpeechFrame?.Invoke(frame, frameSize);
                silentSamples = voiced ? 0 : silentSamples + frameSize;

                if (silentSamples >= endSilenceSamples) EndSpeech(SpeechEndReason.Silence);
                else if (utteranceSamples >= maxSamples) EndSpeech(SpeechEndReason.MaxLength);
                break;
        }
    }

    void PushPreroll()
    {
        Array.Copy(frame, 0, preroll, prerollHead * frameSize, frameSize);
        prerollHead = (prerollHead + 1) % prerollFrames;
        if (prerollCount < prerollFrames) prerollCount++;
    }

    void BeginSpeech()
    {
        // 원형 버퍼에서 오래된 프레임부터 순서대로 꺼낸다
        var pre = new float[prerollCount * frameSize];
        int start = (prerollHead - prerollCount + prerollFrames) % prerollFrames;
        for (int f = 0; f < prerollCount; f++)
            Array.Copy(preroll, ((start + f) % prerollFrames) * frameSize, pre, f * frameSize, frameSize);

        State = VadState.Speaking;
        voicedRun = 0;
        silentSamples = 0;
        utteranceSamples = pre.Length;
        prerollHead = 0;
        prerollCount = 0;   // 다음 발화에 이번 소리가 섞이지 않게 비운다

        SpeechStarted?.Invoke(pre);
    }

    void EndSpeech(SpeechEndReason reason)
    {
        int trailing = silentSamples;
        State = VadState.Idle;
        silentSamples = 0;
        voicedRun = 0;
        SpeechEnded?.Invoke(reason, trailing);
    }
}