using System.Collections;
using UnityEngine;

// 이펙트 생성 + 연출 시간 계산 + 자동 삭제를 한 곳에서 처리하는 헬퍼
//
// 루프(Looping) 이펙트 처리 규칙 — 에셋의 Looping 체크를 해제하지 않아도 됩니다.
// - Effect Duration = 0 (자동) : 생성된 복사본의 루프를 꺼서 '한 사이클만' 재생하고, 그 길이를 자동 계산
// - Effect Duration > 0 (직접) : 루프를 유지한 채 그 시간 동안 반복 재생 → 시간이 되면 방출을 멈추고 자연스럽게 사라짐
public static class EffectUtil
{
    // 파티클이 없을 때(VFX Graph 등) 쓰는 기본값
    public const float DefaultDuration = 1.5f;

    /// <summary>
    /// 이펙트를 생성하고, 연출 시간이 지나면 자동으로 삭제합니다.
    /// </summary>
    /// <param name="overrideDuration">0보다 크면 이 시간 동안 재생 (루프 유지)</param>
    /// <param name="runner">overrideDuration을 쓸 때 타이머를 돌릴 MonoBehaviour (보통 SkillManager)</param>
    /// <returns>연출 시간(초). prefab이 없으면 0</returns>
    /// <summary>
    /// 크기 배율을 적용해 생성 → 재생 → 자동 삭제. scale이 1이면 SpawnAndAutoDestroy와 같음
    /// </summary>
    public static float SpawnScaled(GameObject prefab, Vector3 position, Quaternion rotation, float scale,
                                    float overrideDuration = 0f, MonoBehaviour runner = null)
    {
        if (prefab == null) return 0f;
        GameObject instance = Object.Instantiate(prefab, position, rotation);
        Scale(instance, scale);
        return PlayOnce(instance, overrideDuration, runner);
    }

    /// <summary>
    /// 이펙트 크기를 배율만큼 키움. 파티클도 같이 커지도록 Scaling Mode를 Hierarchy로 바꿈 (배율 1이면 아무것도 안 함)
    /// </summary>
    public static void Scale(GameObject instance, float scale)
    {
        if (instance == null || Mathf.Approximately(scale, 1f) || scale <= 0f) return;
        foreach (ParticleSystem ps in instance.GetComponentsInChildren<ParticleSystem>(true))
        {
            ParticleSystem.MainModule main = ps.main;
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;
        }
        foreach (TrailRenderer tr in instance.GetComponentsInChildren<TrailRenderer>(true)) tr.widthMultiplier *= scale;
        foreach (Light l in instance.GetComponentsInChildren<Light>(true)) l.range *= scale;
        instance.transform.localScale *= scale;
    }

    /// <summary>
    /// 크기 배율을 적용해 생성 → 최대 maxTime초만 보여 준 뒤 방출을 멈추고, 남은 입자는 tail초 안에 정리.
    /// 에셋 길이가 너무 길어도 화면에 오래 남지 않게 할 때 사용
    /// </summary>
    public static GameObject SpawnCapped(GameObject prefab, Vector3 position, Quaternion rotation, float scale,
                                         float maxTime, float tail = 0.3f)
    {
        if (prefab == null) return null;
        GameObject instance = Object.Instantiate(prefab, position, rotation);
        Scale(instance, scale);
        ForceSingleCycle(instance);
        float length = Mathf.Min(GetDuration(instance), Mathf.Max(0.05f, maxTime));
        instance.AddComponent<EffectAutoStop>().Begin(length, tail);
        return instance;
    }

    public static float SpawnAndAutoDestroy(GameObject prefab, Vector3 position, Quaternion rotation,
                                            float overrideDuration = 0f, MonoBehaviour runner = null)
    {
        if (prefab == null) return 0f;

        GameObject instance = Object.Instantiate(prefab, position, rotation);
        return PlayOnce(instance, overrideDuration, runner);
    }

    /// <summary>
    /// 이미 생성된 이펙트를 규칙에 맞게 재생하고 자동 삭제를 예약합니다. 연출 시간을 반환합니다.
    /// </summary>
    public static float PlayOnce(GameObject instance, float overrideDuration = 0f, MonoBehaviour runner = null)
    {
        if (instance == null) return 0f;

        if (overrideDuration > 0f && runner != null)
        {
            // 지정한 시간 동안 (루프면 반복하며) 재생한 뒤 방출 정지 → 남은 입자가 사라지면 삭제
            runner.StartCoroutine(StopAfter(instance, overrideDuration));
            return overrideDuration;
        }

        // 자동: 한 사이클만 재생
        ForceSingleCycle(instance);
        float duration = overrideDuration > 0f ? overrideDuration : GetDuration(instance);
        Object.Destroy(instance, duration + 0.2f);
        return duration;
    }

    private static IEnumerator StopAfter(GameObject instance, float duration)
    {
        yield return new WaitForSeconds(duration);
        if (instance == null) yield break;

        float tail = StopEmitting(instance);
        Object.Destroy(instance, tail + 0.1f);
    }

    // 복사본의 모든 파티클 루프를 끔 (현재 사이클이 끝나면 자연스럽게 멈춤). 원본 에셋은 바뀌지 않음
    public static void ForceSingleCycle(GameObject instance)
    {
        if (instance == null) return;
        foreach (ParticleSystem ps in instance.GetComponentsInChildren<ParticleSystem>())
        {
            ParticleSystem.MainModule main = ps.main;
            main.loop = false;
        }
    }

    // 파티클 방출을 멈추고(이미 나온 입자는 자연스럽게 사라지게) 남은 입자 수명을 반환
    public static float StopEmitting(GameObject instance)
    {
        if (instance == null) return 0f;

        float maxLifetime = 0f;
        foreach (ParticleSystem ps in instance.GetComponentsInChildren<ParticleSystem>())
        {
            ps.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            float life = ps.main.startLifetime.constantMax;
            if (life > maxLifetime) maxLifetime = life;
        }
        return maxLifetime;
    }

    // 하위 ParticleSystem 들 중 가장 긴 한 사이클 재생 시간 (시작 지연 + 지속 시간 + 입자 수명)
    public static float GetDuration(GameObject instance, float fallback = DefaultDuration)
    {
        if (instance == null) return fallback;

        float max = 0f;
        foreach (ParticleSystem ps in instance.GetComponentsInChildren<ParticleSystem>())
        {
            ParticleSystem.MainModule main = ps.main;
            float length = main.startDelay.constantMax + main.duration + main.startLifetime.constantMax;
            if (length > max) max = length;
        }

        return max > 0f ? max : fallback;
    }
}

// SpawnCapped가 붙이는 타이머: 정해진 시간이 되면 방출을 멈추고 짧게 정리
public class EffectAutoStop : MonoBehaviour
{
    private float stopAt, tail;
    private bool stopped;

    public void Begin(float length, float tailTime)
    {
        stopAt = Time.time + length;
        tail = tailTime;
    }

    private void Update()
    {
        if (stopped || Time.time < stopAt) return;
        stopped = true;
        float left = EffectUtil.StopEmitting(gameObject);
        Destroy(gameObject, Mathf.Min(left, tail) + 0.05f);
    }
}