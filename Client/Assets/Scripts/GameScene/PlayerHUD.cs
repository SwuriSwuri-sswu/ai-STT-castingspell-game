using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// 플레이어 체력바 + 피격 연출 (Canvas 아래 'PlayerHUD' 오브젝트에 붙이기)
//   - 체력바: 빨간 바는 즉시 줄고, 뒤의 흰 바가 천천히 따라 줄어듦 (얼마나 맞았는지 보이게)
//   - 피격: 화면 가장자리가 붉게 번쩍 + 카메라 흔들림 + 체력바 흔들림 + "-8" 숫자
//   - 체력이 적으면: 화면이 은은하게 붉게 맥박
//   - (선택) 몹 공격 게이지: 다음 공격까지 남은 시간
public class PlayerHUD : MonoBehaviour
{
    [Header("체력바")]
    [Tooltip("Image Type = Filled, Fill Method = Horizontal 인 이미지")]
    public Image hpFill;
    [Tooltip("(선택) hpFill 뒤에 깔린 흰색/노란색 Filled 이미지. 천천히 줄어들며 깎인 양을 보여 줌")]
    public Image hpDelayFill;
    [Tooltip("(선택) 'HP 72 / 100' 표시")]
    public TMP_Text hpText;
    [Tooltip("체력 숫자 문구. {hp} = 현재 체력, {max} = 최대 체력")]
    public string hpTextFormat = "HP {hp} / {max}";
    [Tooltip("체력 비율에 따라 바 색이 초록 → 노랑 → 빨강으로 바뀜.\n" +
             "※ 바 이미지가 흰색(또는 회색조)이어야 색이 제대로 보입니다. 빨간 이미지면 셋 다 흰색으로 두세요")]
    public bool colorByHealth = true;
    public Color hpHighColor = new Color(0.35f, 0.85f, 0.35f);   // 초록
    public Color hpMidColor = new Color(1f, 0.85f, 0.2f);        // 노랑
    public Color hpLowColor = new Color(0.9f, 0.2f, 0.15f);      // 빨강
    [Tooltip("이 비율에서 정확히 노란색")]
    [Range(0.1f, 0.9f)] public float hpMidPoint = 0.5f;
    public float delayBarWait = 0.4f;
    public float delayBarSpeed = 0.6f;

    [Header("피격 화면 연출")]
    [Tooltip("화면 전체를 덮는 빨간 이미지 (가장자리만 붉은 비네트 이미지면 더 좋음). Raycast Target은 끄기")]
    public Image hitFlash;
    [Range(0f, 1f)] public float flashAlpha = 0.35f;
    public float flashFadeTime = 0.45f;
    [Tooltip("체력이 이 비율 이하면 화면이 은은하게 붉게 맥박")]
    [Range(0f, 1f)] public float lowHpThreshold = 0.3f;
    [Range(0f, 1f)] public float lowHpPulseAlpha = 0.15f;
    public float lowHpPulseSpeed = 3f;

    [Header("흔들림")]
    public float cameraShake = 0.06f;
    public float cameraShakeTime = 0.25f;
    [Tooltip("체력바가 흔들리는 크기 (픽셀)")]
    public float barShake = 8f;

    [Header("(선택) 깎인 숫자 / 회복 숫자")]
    [Tooltip("체력바 근처에 '-8'(피해) / '+5'(회복)을 띄울 글자. 비우면 표시 안 함")]
    public TMP_Text damagePopup;
    public float popupTime = 0.8f;
    [Tooltip("회복 숫자 색")]
    public Color healPopupColor = new Color(0.45f, 1f, 0.45f);

    [Header("회복 연출")]
    [Tooltip("회복할 때 체력바가 잠깐 커지는 정도 (0 = 끄기)")]
    public float healPulseScale = 0.08f;
    public float healPulseTime = 0.35f;

    [Header("(선택) 몹 공격 게이지")]
    [Tooltip("다음 공격까지 차오르는 Filled 이미지. 비우면 표시 안 함")]
    public Image enemyAttackFill;
    [Tooltip("게이지가 이 비율을 넘으면 경고색")]
    [Range(0f, 1f)] public float attackWarnRatio = 0.75f;
    public Color attackNormalColor = new Color(1f, 0.75f, 0.2f);
    public Color attackWarnColor = new Color(1f, 0.2f, 0.15f);

    private float shownDelay = 1f;
    private float delayWaitTimer;
    private float flashTimer = 999f;
    private Coroutine shakeRoutine, barShakeRoutine, popupRoutine, healPulseRoutine;
    private Color damagePopupColor = Color.white;
    private Vector3 barBaseScale = Vector3.one;
    private RectTransform barRect;
    private Vector2 barBasePos;

    private void Start()
    {
        if (hitFlash != null)
        {
            hitFlash.raycastTarget = false;
            SetAlpha(hitFlash, 0f);
        }
        if (damagePopup != null)
        {
            damagePopupColor = damagePopup.color;
            damagePopup.gameObject.SetActive(false);
        }

        if (hpFill != null)
        {
            barRect = hpFill.transform.parent as RectTransform;
            if (barRect == null) barRect = hpFill.rectTransform;
            barBasePos = barRect.anchoredPosition;
            barBaseScale = barRect.localScale;
        }

        PlayerHealth hp = PlayerHealth.Instance;
        if (hp == null)
        {
            Debug.LogWarning("[PlayerHUD] 씬에 PlayerHealth가 없습니다.");
            return;
        }
        hp.OnHealthChanged += HandleHealthChanged;
        hp.OnDamaged += HandleDamaged;
        hp.OnHealed += HandleHealed;
        HandleHealthChanged(hp.CurrentHP, hp.maxHP);
        shownDelay = hp.Ratio;
    }

    private void OnDestroy()
    {
        PlayerHealth hp = PlayerHealth.Instance;
        if (hp != null)
        {
            hp.OnHealthChanged -= HandleHealthChanged;
            hp.OnDamaged -= HandleDamaged;
            hp.OnHealed -= HandleHealed;
        }
    }

    private void Update()
    {
        PlayerHealth hp = PlayerHealth.Instance;
        float ratio = hp != null ? hp.Ratio : 1f;
        float dt = Time.unscaledDeltaTime;

        // 흰 바가 잠시 뒤 천천히 따라 줄어듦
        if (hpDelayFill != null)
        {
            if (delayWaitTimer > 0f) delayWaitTimer -= dt;
            else shownDelay = Mathf.MoveTowards(shownDelay, ratio, delayBarSpeed * dt);
            if (shownDelay < ratio) shownDelay = ratio; // 회복 시 바로 맞춤
            hpDelayFill.fillAmount = shownDelay;
        }

        // 피격 번쩍임 + 저체력 맥박
        if (hitFlash != null)
        {
            flashTimer += dt;
            float flash = flashTimer < flashFadeTime ? flashAlpha * (1f - flashTimer / flashFadeTime) : 0f;
            float pulse = 0f;
            if (hp != null && !hp.IsDead && ratio <= lowHpThreshold && ratio > 0f)
            {
                pulse = lowHpPulseAlpha * (0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * lowHpPulseSpeed));
            }
            if (hp != null && hp.IsDead) pulse = flashAlpha; // 쓰러지면 붉은 화면 유지
            SetAlpha(hitFlash, Mathf.Max(flash, pulse));
        }

        // 몹 공격 게이지
        if (enemyAttackFill != null)
        {
            EnemyController enemy = EnemyManager.Instance != null ? EnemyManager.Instance.CurrentEnemy : null;
            float p = enemy != null && !enemy.IsDying ? enemy.AttackProgress : 0f;
            enemyAttackFill.fillAmount = p;
            enemyAttackFill.color = p >= attackWarnRatio
                ? Color.Lerp(attackWarnColor, Color.white, 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 20f))
                : attackNormalColor;
        }
    }

    private void HandleHealthChanged(float current, float max)
    {
        float ratio = max > 0f ? current / max : 0f;
        if (hpFill != null)
        {
            hpFill.fillAmount = ratio;
            if (colorByHealth) hpFill.color = HealthColor(ratio);
        }
        if (hpText != null) hpText.text = hpTextFormat
            .Replace("{hp}", Mathf.CeilToInt(current).ToString())
            .Replace("{max}", Mathf.CeilToInt(max).ToString());
    }

    private void HandleDamaged(float amount, string reason)
    {
        flashTimer = 0f;
        delayWaitTimer = delayBarWait;

        if (Camera.main != null)
        {
            if (shakeRoutine != null) StopCoroutine(shakeRoutine);
            shakeRoutine = StartCoroutine(ShakeCamera(Camera.main.transform));
        }
        if (barRect != null)
        {
            if (barShakeRoutine != null) StopCoroutine(barShakeRoutine);
            barShakeRoutine = StartCoroutine(ShakeBar());
        }
        if (damagePopup != null)
        {
            if (popupRoutine != null) StopCoroutine(popupRoutine);
            popupRoutine = StartCoroutine(Popup($"-{Mathf.CeilToInt(amount)}", damagePopupColor));
        }
    }

    private Vector3 cameraBasePos;
    private bool cameraShaking;

    private IEnumerator ShakeCamera(Transform cam)
    {
        if (!cameraShaking) cameraBasePos = cam.localPosition;
        cameraShaking = true;
        float t = 0f;
        while (t < cameraShakeTime)
        {
            t += Time.unscaledDeltaTime;
            float k = 1f - t / cameraShakeTime;
            cam.localPosition = cameraBasePos + (Vector3)(Random.insideUnitCircle * cameraShake * k);
            yield return null;
        }
        cam.localPosition = cameraBasePos;
        cameraShaking = false;
        shakeRoutine = null;
    }

    private IEnumerator ShakeBar()
    {
        float t = 0f;
        const float time = 0.3f;
        while (t < time)
        {
            t += Time.unscaledDeltaTime;
            float k = 1f - t / time;
            barRect.anchoredPosition = barBasePos + Random.insideUnitCircle * barShake * k;
            yield return null;
        }
        barRect.anchoredPosition = barBasePos;
        barShakeRoutine = null;
    }

    private void HandleHealed(float amount, string reason)
    {
        if (damagePopup != null)
        {
            if (popupRoutine != null) StopCoroutine(popupRoutine);
            popupRoutine = StartCoroutine(Popup($"+{Mathf.CeilToInt(amount)}", healPopupColor));
        }
        if (barRect != null && healPulseScale > 0f)
        {
            if (healPulseRoutine != null) StopCoroutine(healPulseRoutine);
            healPulseRoutine = StartCoroutine(HealPulse());
        }
    }

    // 회복 시 체력바가 살짝 커졌다 돌아옴
    private IEnumerator HealPulse()
    {
        float t = 0f;
        while (t < healPulseTime)
        {
            t += Time.unscaledDeltaTime;
            float k = Mathf.Sin(Mathf.Clamp01(t / healPulseTime) * Mathf.PI);
            barRect.localScale = barBaseScale * (1f + healPulseScale * k);
            yield return null;
        }
        barRect.localScale = barBaseScale;
        healPulseRoutine = null;
    }

    private IEnumerator Popup(string text, Color color)
    {
        damagePopup.gameObject.SetActive(true);
        damagePopup.text = text;
        damagePopup.color = color;
        RectTransform rt = damagePopup.rectTransform;
        Vector2 basePos = rt.anchoredPosition;
        float t = 0f;
        while (t < popupTime)
        {
            t += Time.unscaledDeltaTime;
            float k = t / popupTime;
            rt.anchoredPosition = basePos + new Vector2(0f, 30f * k);
            damagePopup.alpha = 1f - k * k;
            yield return null;
        }
        rt.anchoredPosition = basePos;
        damagePopup.gameObject.SetActive(false);
        popupRoutine = null;
    }

    // 빨강(0) → 노랑(중간) → 초록(1)
    private Color HealthColor(float ratio)
    {
        if (ratio >= hpMidPoint)
            return Color.Lerp(hpMidColor, hpHighColor, Mathf.InverseLerp(hpMidPoint, 1f, ratio));
        return Color.Lerp(hpLowColor, hpMidColor, Mathf.InverseLerp(0f, hpMidPoint, ratio));
    }

    private static void SetAlpha(Graphic g, float a)
    {
        Color c = g.color;
        c.a = a;
        g.color = c;
    }
}