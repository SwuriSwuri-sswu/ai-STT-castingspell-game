using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// 보스전 화면 표시 (BossScene Canvas 아래 'BossHUD' 오브젝트에 붙이기)
//   위쪽 보스 체력바 · 보스 이름 · 속성/약점 · 남은 턴 · 내 턴 타이머 · 턴 배너("나의 턴!") · 하수인 경고
//   칸을 비워 두면 그 표시는 생략
public class BossHUD : MonoBehaviour
{
    [Header("전체 (등장 연출 전에는 숨김)")]
    public CanvasGroup group;

    [Header("보스 체력바")]
    [Tooltip("Image Type = Filled, Horizontal")]
    public Image hpFill;
    [Tooltip("(선택) 뒤에서 천천히 따라 줄어드는 바")]
    public Image hpDelayFill;
    public TMP_Text nameText;
    [Tooltip("(선택) 체력 숫자 '640 / 800'")]
    public TMP_Text hpText;
    [Tooltip("(선택) '물 속성 / 약점: 풀'")]
    public TMP_Text elementText;
    [Tooltip("체력바를 속성 색으로 물들이기 (바 이미지가 흰색일 때)")]
    public bool tintBarByElement = true;
    public float delaySpeed = 0.4f;

    [Header("턴")]
    [Tooltip("(선택) '남은 턴 22'")]
    public TMP_Text turnsText;
    [Tooltip("(선택) 내 턴 남은 시간 게이지 (Filled, Radial 360 추천)")]
    public Image timerFill;
    [Tooltip("(선택) 남은 초 숫자")]
    public TMP_Text timerText;
    public Color timerColor = new Color(1f, 0.85f, 0.3f);
    public Color timerWarnColor = new Color(1f, 0.25f, 0.2f);
    [Tooltip("이 초 이하가 되면 경고색 + 깜빡임")]
    public float timerWarnSeconds = 3f;

    [Header("턴 배너 ('나의 턴!' / '마왕의 턴')")]
    [Tooltip("비우면 알림 자막(BattleUIManager의 Notice Text)에 띄움")]
    public TMP_Text bannerText;
    public float bannerTime = 1.2f;

    [Header("(선택) 하수인 경고 '하수인 공격까지 8초' (Subtitle_Kill을 그대로 넣어도 됨)")]
    public TMP_Text minionText;

    private float shownDelay = 1f;
    private float targetRatio = 1f;
    private Coroutine bannerRoutine;
    private Vector3 bannerBaseScale = Vector3.one;

    private void Awake()
    {
        if (group != null) group.alpha = 0f;
        if (bannerText != null)
        {
            bannerBaseScale = bannerText.transform.localScale;
            bannerText.gameObject.SetActive(false);
        }
        SetTimer(-1f, 1f);
        SetMinionTimer(-1f);
    }

    private void Update()
    {
        if (hpDelayFill == null) return;
        shownDelay = Mathf.MoveTowards(shownDelay, targetRatio, delaySpeed * Time.unscaledDeltaTime);
        if (shownDelay < targetRatio) shownDelay = targetRatio;
        hpDelayFill.fillAmount = shownDelay;
    }

    // 등장: 서서히 나타나며 체력바가 0 → 가득 차오름
    public IEnumerator Reveal(string bossName, float duration = 1.2f)
    {
        if (nameText != null) nameText.text = bossName;
        SetHP(0f, 0f, 0f);
        float t = 0f;
        while (t < duration)
        {
            t += Time.deltaTime;
            float k = Mathf.Clamp01(t / duration);
            if (group != null) group.alpha = Mathf.Clamp01(k * 2f);
            float fill = Mathf.SmoothStep(0f, 1f, k);
            if (hpFill != null) hpFill.fillAmount = fill;
            if (hpDelayFill != null) hpDelayFill.fillAmount = fill;
            yield return null;
        }
        shownDelay = targetRatio = 1f;
    }

    public void Hide()
    {
        if (group != null) group.alpha = 0f;
    }

    public void SetHP(float ratio, float current, float max)
    {
        targetRatio = Mathf.Clamp01(ratio);
        if (hpFill != null) hpFill.fillAmount = targetRatio;
        if (hpText != null) hpText.text = $"{Mathf.CeilToInt(current)} / {Mathf.CeilToInt(max)}";
    }

    public void SetElement(MagicElement element, Color color)
    {
        if (tintBarByElement && hpFill != null) hpFill.color = color;
        if (elementText == null) return;
        string hex = ColorUtility.ToHtmlStringRGB(color);
        string text = $"<color=#{hex}>{EnemyController.ElementName(element)}</color>";
        if (EnemyController.TryGetWeakness(element, out MagicElement weak))
        {
            string weakHex = ColorUtility.ToHtmlStringRGB(EnemyController.ElementColor(weak));
            text += $"   Weakness: <color=#{weakHex}>{EnemyController.ElementName(weak)}</color>";
        }
        elementText.text = text;
    }

    public void SetTurnsLeft(int turns)
    {
        if (turnsText != null) turnsText.text = $"Turns left {Mathf.Max(0, turns)}";
    }

    // remaining < 0 이면 숨김
    public void SetTimer(float remaining, float total)
    {
        bool show = remaining >= 0f;
        if (timerFill != null)
        {
            timerFill.gameObject.SetActive(show);
            if (show)
            {
                timerFill.fillAmount = Mathf.Clamp01(remaining / Mathf.Max(0.01f, total));
                bool warn = remaining <= timerWarnSeconds;
                Color c = warn ? Color.Lerp(timerWarnColor, Color.white, 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 18f)) : timerColor;
                timerFill.color = c;
            }
        }
        if (timerText != null)
        {
            timerText.gameObject.SetActive(show);
            if (show)
            {
                timerText.text = Mathf.CeilToInt(remaining).ToString();
                timerText.color = remaining <= timerWarnSeconds ? timerWarnColor : Color.white;
            }
        }
    }

    // seconds < 0 이면 숨김
    public void SetMinionTimer(float seconds)
    {
        if (minionText == null) return;
        bool show = seconds >= 0f;
        minionText.gameObject.SetActive(show);
        if (show) minionText.text = $"Minion attacks in {Mathf.CeilToInt(seconds)}s";
    }

    // 배너 칸을 비워 두면 알림 자막(Subtitle_Notice)에 색을 입혀 띄움 / 글이 비어 있으면 생략
    public void ShowBanner(string text, Color color)
    {
        if (string.IsNullOrEmpty(text)) return;
        if (bannerText == null)
        {
            if (BattleUIManager.Instance != null)
                BattleUIManager.Instance.ShowNotice($"<color=#{ColorUtility.ToHtmlStringRGB(color)}>{text}</color>");
            return;
        }
        if (bannerRoutine != null) StopCoroutine(bannerRoutine);
        bannerRoutine = StartCoroutine(Banner(text, color));
    }

    private IEnumerator Banner(string text, Color color)
    {
        bannerText.gameObject.SetActive(true);
        bannerText.text = text;
        bannerText.color = color;
        float t = 0f;
        while (t < bannerTime)
        {
            t += Time.unscaledDeltaTime;
            float k = t / bannerTime;
            // 크게 튀어나왔다가 → 자리 잡고 → 사라짐
            float scale = k < 0.2f ? Mathf.Lerp(1.6f, 1f, k / 0.2f) : 1f;
            bannerText.transform.localScale = bannerBaseScale * scale;
            bannerText.alpha = k < 0.75f ? 1f : 1f - (k - 0.75f) / 0.25f;
            yield return null;
        }
        bannerText.gameObject.SetActive(false);
        bannerText.transform.localScale = bannerBaseScale;
        bannerRoutine = null;
    }
}