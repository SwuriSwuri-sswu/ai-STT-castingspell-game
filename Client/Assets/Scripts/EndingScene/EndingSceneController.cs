using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;

// 엔딩 씬 (게임 오버 / 클리어 결과 화면). EndingScene의 Canvas 아래 오브젝트에 붙이기
//   제목 → 결과 → 버튼 순서로 천천히 나타남
//   버튼 OnClick에 OnClickRetry / OnClickTitle 연결
public class EndingSceneController : MonoBehaviour
{
    [Header("글자")]
    public TMP_Text titleText;
    [Tooltip("처치 수·점수·플레이 시간 등")]
    public TMP_Text resultText;
    public string gameOverTitle = "GAME OVER";
    public string clearTitle = "STAGE CLEAR";
    [Tooltip("보스를 물리쳤을 때 제목")]
    public string bossClearTitle = "VICTORY";
    public Color gameOverColor = new Color(0.85f, 0.15f, 0.15f);
    public Color clearColor = new Color(1f, 0.84f, 0f);

    [Header("나타나는 순서 (각각 CanvasGroup, 비워도 됨)")]
    public CanvasGroup titleGroup;
    public CanvasGroup resultGroup;
    public CanvasGroup buttonGroup;
    public float startDelay = 0.5f;
    public float fadeTime = 0.8f;
    public float gap = 0.4f;
    [Tooltip("제목이 크게 시작해서 원래 크기로 줄어드는 연출")]
    public float titleStartScale = 1.6f;

    [Header("씬 이름")]
    public string gameSceneName = "GameScene";
    public string titleSceneName = "TitleScene";

    private void Start()
    {
        Time.timeScale = 1f;

        bool cleared = GameResult.HasResult && GameResult.Cleared;
        if (titleText != null)
        {
            titleText.text = cleared ? (GameResult.IsBossFight ? bossClearTitle : clearTitle) : gameOverTitle;
            titleText.color = cleared ? clearColor : gameOverColor;
        }
        if (resultText != null)
        {
            if (!GameResult.HasResult) resultText.text = "";
            else if (GameResult.IsBossFight) resultText.text = BossResultText(cleared);
            else resultText.text =
                  $"처치한 몬스터  {GameResult.Kills}\n" +
                  $"배운 스펠  {GameResult.LearnedSpells}\n" +
                  $"버틴 시간  {GameResult.PlayTimeText}\n" +
                  $"점수  {GameResult.Score}";
        }
        // 다시하기: 보스전에서 졌으면 보스전부터, 그 외에는 처음부터
        retryScene = (!cleared && GameResult.HasResult && !string.IsNullOrEmpty(GameResult.RetryScene))
            ? GameResult.RetryScene : gameSceneName;
        GameResult.ClearBoss();

        SetAlpha(titleGroup, 0f);
        SetAlpha(resultGroup, 0f);
        SetAlpha(buttonGroup, 0f);
        if (buttonGroup != null) buttonGroup.interactable = false;
        StartCoroutine(Reveal());
    }

    private IEnumerator Reveal()
    {
        yield return new WaitForSeconds(startDelay);

        Transform titleT = titleGroup != null ? titleGroup.transform : (titleText != null ? titleText.transform : null);
        Vector3 baseScale = titleT != null ? titleT.localScale : Vector3.one;
        yield return Fade(titleGroup, t =>
        {
            if (titleT != null)
            {
                float e = 1f - (1f - t) * (1f - t);
                titleT.localScale = baseScale * Mathf.Lerp(titleStartScale, 1f, e);
            }
        });

        yield return new WaitForSeconds(gap);
        yield return Fade(resultGroup, null);
        yield return new WaitForSeconds(gap);
        yield return Fade(buttonGroup, null);
        if (buttonGroup != null) buttonGroup.interactable = true;
    }

    private IEnumerator Fade(CanvasGroup g, System.Action<float> onStep)
    {
        float t = 0f;
        while (t < fadeTime)
        {
            t += Time.deltaTime;
            float k = Mathf.Clamp01(t / fadeTime);
            SetAlpha(g, k);
            onStep?.Invoke(k);
            yield return null;
        }
        SetAlpha(g, 1f);
        onStep?.Invoke(1f);
    }

    private static void SetAlpha(CanvasGroup g, float a)
    {
        if (g != null) g.alpha = a;
    }

    private string retryScene;

    private static string BossResultText(bool cleared)
    {
        string text = cleared ? $"등급  <b>{GameResult.BossGrade}</b>\n" : $"마왕의 남은 체력  {Mathf.CeilToInt(GameResult.BossHpLeftRatio * 100f)}%\n";
        text += $"사용한 턴  {GameResult.BossTurnsUsed} / {GameResult.BossMaxTurns}\n" +
                $"상성 적중률  {Mathf.RoundToInt(GameResult.BossWeaknessRate * 100f)}%\n" +
                $"배운 스펠  {GameResult.LearnedSpells}";
        if (cleared) text += $"\n남은 체력  {Mathf.CeilToInt(GameResult.PlayerHpLeft)}";
        return text;
    }

    public void OnClickRetry() => SceneManager.LoadScene(string.IsNullOrEmpty(retryScene) ? gameSceneName : retryScene);
    public void OnClickTitle() => SceneManager.LoadScene(titleSceneName);
}