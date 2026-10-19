using System.Collections.Generic;
using UnityEngine;

public enum ChantTier { Normal, Strong, Perfect }

public struct ChantResult
{
    public ChantTier tier;
    public float power;                 // 시전 함수에 넘길 값 (1.0 / 1.25 / 1.5)
    public float volumeDiffDb;          // 기준 없으면 NaN
    public float? pitchDiffSemitones;   // 피치 없거나 기준 없으면 null

    public string LogText => tier switch
    {
        ChantTier.Strong => "Strong chant!",
        ChantTier.Perfect => "Perfect chant!",
        _ => null
    };
}

/// <summary>
/// 성량·피치를 '평소 목소리'와 비교해 영창 보너스(power)를 계산한다.
/// 사람 목소리 발화(발동 성공 + NO_MATCH/AMBIGUOUS 불발)마다 Evaluate를 호출할 것.
/// </summary>
public class VoiceBonusCalculator : MonoBehaviour
{
    [Header("판정 기준")]
    [SerializeField] float strongVolumeDb = 6f;          // 강한 영창: 평소보다 +6dB 이상
    [SerializeField] float perfectPitchSemitones = 2f;   // 완벽한 영창: 추가로 +2반음 이상

    [Header("보너스")]
    [SerializeField] float strongBonus = 1.25f;
    [SerializeField] float perfectBonus = 1.5f;

    [Header("평소 목소리 기준")]
    [Tooltip("기준 발화가 이 개수만큼 쌓인 다음 발화부터 보너스 판정 (2 = 3번째 발화부터)")]
    [SerializeField] int minSamples = 2;
    [Tooltip("중앙값을 낼 최근 발화 개수")]
    [SerializeField] int windowSize = 10;
    [SerializeField] float baselineVolumeMin = -40f;
    [SerializeField] float baselineVolumeMax = -15f;
    [Tooltip("켜면 처음 windowSize개로 기준 고정 / 끄면 최근 windowSize개로 계속 갱신")]
    [SerializeField] bool freezeAfterWindow = false;

    readonly List<float> volumeSamples = new();
    readonly List<float> pitchSamples = new();

    /// <param name="castSucceeded">발동 성공 여부. 불발이면 기준만 갱신하고 power는 1.0</param>
    /// <param name="volumeDb">cast_result.audio.volume_db</param>
    /// <param name="pitchHz">cast_result.audio.pitch_hz (못 찾았으면 null)</param>
    public ChantResult Evaluate(bool castSucceeded, float volumeDb, float? pitchHz)
    {
        if (pitchHz.HasValue && pitchHz.Value <= 0f) pitchHz = null;

        var r = new ChantResult { tier = ChantTier.Normal, power = 1f, volumeDiffDb = float.NaN };

        // 1. 기준 발화가 부족하면 보통
        if (volumeSamples.Count >= minSamples)
        {
            float baseVol = Mathf.Clamp(Median(volumeSamples), baselineVolumeMin, baselineVolumeMax);
            r.volumeDiffDb = volumeDb - baseVol;

            if (pitchHz.HasValue && pitchSamples.Count >= minSamples)
            {
                float basePitch = Median(pitchSamples);
                r.pitchDiffSemitones = 12f * Mathf.Log(pitchHz.Value / basePitch, 2f);
            }

            // 2. 성량 +6dB 미만 → 보통 / 3. 피치 null 또는 +2반음 미만 → 강한 / 4. 그 외 → 완벽
            if (r.volumeDiffDb >= strongVolumeDb)
            {
                bool pitchUp = r.pitchDiffSemitones.HasValue && r.pitchDiffSemitones.Value >= perfectPitchSemitones;
                r.tier = pitchUp ? ChantTier.Perfect : ChantTier.Strong;
            }
        }

        // 5. 기준 갱신: 강한·완벽 영창은 제외 (불발 발화도 같은 기준으로 판정해서 제외)
        if (r.tier == ChantTier.Normal)
        {
            AddSample(volumeSamples, volumeDb);
            if (pitchHz.HasValue) AddSample(pitchSamples, pitchHz.Value);
        }

        if (castSucceeded)
        {
            r.power = r.tier switch
            {
                ChantTier.Strong => strongBonus,
                ChantTier.Perfect => perfectBonus,
                _ => 1f
            };
        }
        return r;
    }

    public void ResetBaseline()
    {
        volumeSamples.Clear();
        pitchSamples.Clear();
    }

    void AddSample(List<float> list, float value)
    {
        if (freezeAfterWindow && list.Count >= windowSize) return;
        list.Add(value);
        if (list.Count > windowSize) list.RemoveAt(0);
    }

    static float Median(List<float> src)
    {
        var s = new List<float>(src);
        s.Sort();
        int n = s.Count;
        return n % 2 == 1 ? s[n / 2] : (s[n / 2 - 1] + s[n / 2]) * 0.5f;
    }
}
