using System.Collections;
using UnityEngine;

// 지팡이 끝 Point Light 연출 (WandTip의 Point Light에 붙이기)
//   평소   : 지금 Inspector에 맞춰 둔 밝기·범위·색 그대로
//   영창 중 : (Space를 누르고 말하는 동안) 아주 살짝 커졌다 작아졌다
//   시전   : 스펠 속성 색으로 한 번 크게 번쩍 → 원래대로
[RequireComponent(typeof(Light))]
public class WandTipLight : MonoBehaviour
{
    [Header("영창 중 (숨쉬듯 커졌다 작아짐)")]
    [Tooltip("영창 중 밝기·범위가 커지는 정도 (0.15 = 최대 15%)")]
    [Range(0f, 1f)] public float chantPulse = 0.15f;
    [Tooltip("1초에 커졌다 작아지는 횟수")]
    public float chantSpeed = 2.5f;
    [Tooltip("영창이 시작·끝날 때 부드럽게 바뀌는 시간 (초)")]
    public float chantFade = 0.2f;
    [Tooltip("말을 끝내고 서버 판정을 기다리는 동안에도 계속 움직이기")]
    public bool pulseWhileWaiting = true;

    [Header("시전 번쩍임 (속성 색)")]
    [Tooltip("번쩍일 때 밝기 배율 (평소 밝기 × 이 값)")]
    public float flashIntensity = 4f;
    [Tooltip("번쩍일 때 범위 배율 (평소 범위 × 이 값)")]
    public float flashRange = 2f;
    [Tooltip("번쩍임이 커지는 시간 (초)")]
    public float flashRise = 0.06f;
    [Tooltip("번쩍임이 사라지는 시간 (초)")]
    public float flashFall = 0.45f;
    [Tooltip("번쩍임 뒤 색이 원래 색으로 돌아오는 시간 (초)")]
    public float colorReturn = 0.3f;

    [Header("연결 (비우면 자동으로 찾음)")]
    public VoiceSender voiceSender;

    private Light tipLight;
    private float baseIntensity;
    private float baseRange;
    private Color baseColor;
    private float chantBlend;     // 0 = 평소, 1 = 영창 중
    private float flashBlend;     // 0 = 평소, 1 = 번쩍임 최대
    private Color flashColor;
    private float colorBlend;     // 0 = 원래 색, 1 = 속성 색
    private Coroutine flashRoutine;
    private float onWeight = 1f, onTarget = 1f, onSpeed = 2f;   // 켜짐 정도 (보스전 입장 동안 꺼 둠)
    private SkillManager subscribed;

    private void Awake()
    {
        tipLight = GetComponent<Light>();
        baseIntensity = tipLight.intensity;
        baseRange = tipLight.range;
        baseColor = tipLight.color;
        flashColor = baseColor;
    }

    private void Start()
    {
        if (voiceSender == null) voiceSender = FindFirstObjectByType<VoiceSender>();
        Subscribe();
    }

    private void OnEnable() => Subscribe();

    private void OnDisable()
    {
        if (subscribed != null) subscribed.OnSpellCast -= HandleCast;
        subscribed = null;
    }

    private void Subscribe()
    {
        SkillManager sm = SkillManager.Instance;
        if (sm == null || sm == subscribed) return;
        if (subscribed != null) subscribed.OnSpellCast -= HandleCast;
        sm.OnSpellCast += HandleCast;
        subscribed = sm;
    }

    private void Update()
    {
        if (subscribed == null) Subscribe();   // SkillManager가 늦게 생긴 경우

        bool chanting = voiceSender != null &&
                        (voiceSender.IsRecording || (pulseWhileWaiting && voiceSender.IsWaitingForResult));
        float target = chanting ? 1f : 0f;
        chantBlend = Mathf.MoveTowards(chantBlend, target, Time.deltaTime / Mathf.Max(0.01f, chantFade));

        // 영창: 0 ~ chantPulse 사이로 부드럽게 오르내림
        float wave = (Mathf.Sin(Time.time * chantSpeed * Mathf.PI * 2f) + 1f) * 0.5f;
        float chantMul = 1f + chantPulse * wave * chantBlend;

        float intensityMul = Mathf.Lerp(chantMul, flashIntensity, flashBlend);
        float rangeMul = Mathf.Lerp(chantMul, flashRange, flashBlend);
        onWeight = Mathf.MoveTowards(onWeight, onTarget, Time.deltaTime * onSpeed);
        tipLight.enabled = onWeight > 0.001f;
        tipLight.intensity = baseIntensity * intensityMul * onWeight;
        tipLight.range = baseRange * rangeMul;
        tipLight.color = Color.Lerp(baseColor, flashColor, colorBlend);
    }

    private void HandleCast(SpellData spell, EnemyController target, float power)
    {
        if (spell == null || onTarget <= 0f) return;
        Flash(EnemyController.ElementColor(spell.elementType));
    }

    /// <summary>빛 켜기/끄기. fadeTime 0 = 즉시 (보스전: 입장 동안 끔 → 전투 시작 때 켬)</summary>
    public void SetOn(bool on, float fadeTime = 0.5f)
    {
        onTarget = on ? 1f : 0f;
        if (fadeTime <= 0f) { onWeight = onTarget; onSpeed = 1000f; }
        else onSpeed = 1f / fadeTime;
        if (tipLight != null) tipLight.enabled = onWeight > 0.001f || on;
    }

    // 다른 스크립트에서 직접 번쩍이게 할 때도 사용 가능
    public void Flash(Color color)
    {
        flashColor = color;
        if (flashRoutine != null) StopCoroutine(flashRoutine);
        flashRoutine = StartCoroutine(FlashRoutine());
    }

    private IEnumerator FlashRoutine()
    {
        colorBlend = 1f;
        float start = flashBlend;
        for (float t = 0f; t < flashRise; t += Time.deltaTime)
        {
            flashBlend = Mathf.Lerp(start, 1f, t / flashRise);
            yield return null;
        }
        flashBlend = 1f;

        for (float t = 0f; t < flashFall; t += Time.deltaTime)
        {
            flashBlend = 1f - Mathf.SmoothStep(0f, 1f, t / flashFall);
            yield return null;
        }
        flashBlend = 0f;

        for (float t = 0f; t < colorReturn; t += Time.deltaTime)
        {
            colorBlend = 1f - t / colorReturn;
            yield return null;
        }
        colorBlend = 0f;
        flashRoutine = null;
    }
}
