using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;

// 엔딩 씬 (마왕을 물리쳤을 때). EndingScene의 Canvas 아래 빈 오브젝트에 붙이기
//   1. Victory!  (+ 선택: 등급·결과 한 줄)
//   2. 엔딩 문구가 한 줄씩 나타남
//   3. 크레딧이 아래에서 위로 올라감 (화면 아래에 옅게 "Press ESC to skip")
//   4. 타이틀로 돌아가는 버튼
//   ESC: 지금 단계를 건너뜀 (문구 → 크레딧 → 버튼)
// ※ 게임 오버는 이제 각 씬(GameOverSequence)에서 처리. 혹시 게임 오버로 이 씬에 오면 GAME OVER 제목만 보여 줌
public class EndingSceneController : MonoBehaviour
{
    [Header("1. Victory")]
    public TMP_Text victoryText;
    public string victoryTitle = "Victory!";
    public Color victoryColor = new Color(1f, 0.84f, 0f);
    [Tooltip("Victory 아래 결과 (등급·사용 턴·약점 적중률·남은 HP·배운 스펠·플레이 시간)")]
    public TMP_Text resultText;
    [Tooltip("결과를 한 글자씩 타이핑")]
    public bool typeResult = true;
    public float typeInterval = 0.035f;
    public float typeLinePause = 0.25f;
    [Tooltip("결과까지 다 나온 뒤 기다리는 시간 (초)")]
    public float victoryHold = 4f;
    public float titleStartScale = 1.6f;

    [Header("2. 엔딩 문구")]
    public TMP_Text storyText;
    [TextArea(6, 14)]
    public string story =
        "The Demon Lord of Silence has fallen.\n\n" +
        "His spell breaks, and sound returns to the castle -\n" +
        "the wind through the towers, the crackle of the torches,\n" +
        "and the voices of the people who had been silenced.\n\n" +
        "Your words became magic,\n" +
        "and your magic gave the world its voice again.\n\n" +
        "Thank you, brave chanter.";
    [Tooltip("빈 줄로 나눈 문단마다 이 간격으로 하나씩 나타남 (초)")]
    public float paragraphInterval = 1.8f;
    [Tooltip("문구를 다 보여 준 뒤 기다리는 시간 (초)")]
    public float storyHold = 3f;

    [Header("3. 크레딧")]
    [Tooltip("크레딧 글자. Canvas 아래에 두기 (위치는 코드가 아래→위로 옮김)")]
    public TMP_Text creditsText;
    [TextArea(10, 30)]
    public string credits = DefaultCredits;
    [Tooltip("올라가는 속도 (픽셀/초, Canvas 기준)")]
    public float creditsSpeed = 70f;
    [Tooltip("화면 아래 옅은 안내 (Press ESC to skip)")]
    public TMP_Text skipHint;
    public string skipHintText = "Press ESC to skip";
    [Range(0f, 1f)] public float skipHintAlpha = 0.35f;

    [Header("4. 버튼")]
    [Tooltip("타이틀로 버튼 (OnClick에 OnClickTitle 연결). CanvasGroup 자동 추가")]
    public GameObject titleButton;
    [Tooltip("(선택) 마지막에 버튼 위에 띄울 글")]
    public TMP_Text endText;
    public string endMessage = "Thank you for playing!";

    [Header("게임 오버로 들어온 경우 (예전 흐름)")]
    public string gameOverTitle = "GAME OVER";
    public Color gameOverColor = new Color(0.85f, 0.15f, 0.15f);

    [Header("씬 이름")]
    public string titleSceneName = "TitleScene";
    public string gameSceneName = "GameScene";

    public float fadeTime = 0.8f;

    private bool skip;
    private string retryScene;

    // 크레딧 초안 (팀원 이름·역할은 바꿔 주세요)
    private const string DefaultCredits =
        "<size=130%><b>[Game Title]</b></size>\n" +
        "<size=80%>a voice-casting magic adventure</size>\n\n\n" +
        "<size=110%><b>Team</b></size>\n\n" +
        "<size=80%>Game Design & Unity Client</size>\n[Name]\n\n" +
        "<size=80%>Speech Recognition (AI)</size>\n[Name]\n\n" +
        "<size=80%>Server & Database</size>\n[Name]\n\n" +
        "<size=80%>Art & Assets</size>\n[Name]\n\n" +
        "<size=80%>Planning & QA</size>\n[Name]\n\n\n" +
        "<size=110%><b>Built With</b></size>\n\n" +
        "Unity 6\nFastAPI\nfaster-whisper\nVosk\n\n\n" +
        "<size=110%><b>Assets</b></size>\n\n" +
        "Cartoon FX Remaster - Jean Moreno (JMO)\n" +
        "Hyper Casual FX - Lana Studio\n" +
        "Stylized VFX Doodles / Stylized Water Effects - NamuFX\n" +
        "Hits Effects FREE - Matthew Guz\n" +
        "Magic Effects Pack\n" +
        "Procedural Fire\n" +
        "Character animations - Mixamo\n" +
        "Crown - Poly by Google\n\n\n" +
        "<size=110%><b>Special Thanks</b></size>\n\n" +
        "Our professor and everyone who playtested\n\n\n" +
        "2026 Convergence Capstone Design";

    private void Start()
    {
        Time.timeScale = 1f;
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        bool cleared = !GameResult.HasResult || GameResult.Cleared;   // 결과 없이 바로 열어도 승리 화면으로
        retryScene = !cleared && !string.IsNullOrEmpty(GameResult.RetryScene) ? GameResult.RetryScene : gameSceneName;

        if (victoryText != null)
        {
            victoryText.text = cleared ? victoryTitle : gameOverTitle;
            victoryText.color = cleared ? victoryColor : gameOverColor;
        }
        if (resultText != null) resultText.text = cleared ? BossResultText() : "";
        GameResult.ClearBoss();

        Hide(victoryText); Hide(resultText); Hide(storyText); Hide(creditsText); Hide(skipHint); Hide(endText);
        if (titleButton != null)
        {
            CanvasGroup g = Group(titleButton);
            g.alpha = 0f;
            g.interactable = false;
            g.blocksRaycasts = false;
        }
        StartCoroutine(cleared ? Run() : RunGameOver());
    }

    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.Escape)) skip = true;
    }

    // ---------- 승리 ----------
    private IEnumerator Run()
    {
        yield return new WaitForSeconds(0.5f);
        ShowSkipHint(true);

        // 1. Victory!
        if (victoryText != null) yield return Pop(victoryText);
        if (resultText != null && !string.IsNullOrEmpty(resultText.text))
        {
            if (typeResult) yield return TypeText(resultText);
            else yield return Fade(resultText, 0f, 1f);
        }
        yield return Wait(victoryHold);
        StartCoroutine(Fade(victoryText, 1f, 0f));
        StartCoroutine(Fade(resultText, 1f, 0f));
        yield return new WaitForSeconds(fadeTime);

        // 2. 엔딩 문구 (문단마다 하나씩)
        if (storyText != null && !string.IsNullOrEmpty(story))
        {
            string[] paragraphs = story.Replace("\r", "").Split(new[] { "\n\n" }, System.StringSplitOptions.None);
            storyText.gameObject.SetActive(true);
            storyText.alpha = 1f;
            // 전체 문구를 처음부터 자리 잡아 두고(아직 안 나온 문단은 투명) → 문단마다 제자리에서 페이드인
            //   (문단을 덧붙이면 가운데 정렬 때문에 글 전체가 위로 밀려 올라가던 문제)
            for (int i = 0; i < paragraphs.Length; i++)
            {
                if (skip) break;
                for (float t = 0f; t < fadeTime && !skip; t += Time.deltaTime)
                {
                    storyText.text = StoryWithAlpha(paragraphs, i, Mathf.Clamp01(t / fadeTime));
                    yield return null;
                }
                storyText.text = StoryWithAlpha(paragraphs, i, 1f);
                if (i < paragraphs.Length - 1) yield return Wait(paragraphInterval);
            }
            storyText.text = StoryWithAlpha(paragraphs, paragraphs.Length - 1, 1f);
            yield return Wait(storyHold);
            yield return Fade(storyText, 1f, 0f);
        }
        skip = false;

        // 3. 크레딧 (아래 → 위)
        if (creditsText != null && !string.IsNullOrEmpty(credits)) yield return ScrollCredits();
        skip = false;

        // 4. 끝 문구 + 타이틀 버튼
        ShowSkipHint(false);
        if (endText != null && !string.IsNullOrEmpty(endMessage))
        {
            endText.text = endMessage;
            yield return Fade(endText, 0f, 1f);
        }
        yield return ShowButton();
    }

    private IEnumerator ScrollCredits()
    {
        creditsText.text = credits;
        creditsText.gameObject.SetActive(true);
        creditsText.alpha = 1f;

        RectTransform rt = creditsText.rectTransform;
        RectTransform area = rt.parent as RectTransform;
        float areaH = area != null ? area.rect.height : Screen.height;
        // 지금 가로 폭 유지 (가로로 늘린 Stretch 상태에서 기준점을 가운데로 바꾸면 폭이 0이 되어 글자가 세로로 쌓이던 문제)
        float width = rt.rect.width > 10f ? rt.rect.width : (area != null ? area.rect.width * 0.8f : 1200f);
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 1f);
        rt.sizeDelta = new Vector2(width, rt.sizeDelta.y);
        creditsText.ForceMeshUpdate();
        float textH = Mathf.Max(creditsText.GetPreferredValues(credits, width, 0f).y, 10f);
        rt.sizeDelta = new Vector2(width, textH);

        // 글 맨 윗줄이 화면 아래에서 시작 → 맨 아랫줄이 화면 위로 사라질 때까지
        float startY = -areaH * 0.5f;
        float endY = areaH * 0.5f + textH;
        float y = startY;
        while (y < endY && !skip)
        {
            y += creditsSpeed * Time.deltaTime;
            rt.anchoredPosition = new Vector2(rt.anchoredPosition.x, y);
            yield return null;
        }
        creditsText.gameObject.SetActive(false);
    }

    // ---------- 게임 오버로 들어온 경우 ----------
    private IEnumerator RunGameOver()
    {
        yield return new WaitForSeconds(0.5f);
        if (victoryText != null) yield return Pop(victoryText);
        yield return ShowButton();
    }

    // ---------- 버튼 ----------
    public void OnClickTitle() => SceneManager.LoadScene(titleSceneName);
    public void OnClickRetry() => SceneManager.LoadScene(retryScene);

    // ---------- 도우미 ----------
    private IEnumerator ShowButton()
    {
        if (titleButton == null) yield break;
        CanvasGroup g = Group(titleButton);
        titleButton.SetActive(true);
        for (float t = 0f; t < fadeTime; t += Time.deltaTime)
        {
            g.alpha = t / fadeTime;
            yield return null;
        }
        g.alpha = 1f;
        g.interactable = true;
        g.blocksRaycasts = true;
    }

    private void ShowSkipHint(bool show)
    {
        if (skipHint == null) return;
        skipHint.text = skipHintText;
        skipHint.gameObject.SetActive(true);
        StartCoroutine(Fade(skipHint, show ? 0f : skipHintAlpha, show ? skipHintAlpha : 0f));
    }

    // current 문단까지 보이고(current는 alpha), 그 뒤 문단은 투명. 자리는 항상 전체 문구 기준
    private static string StoryWithAlpha(string[] paragraphs, int current, float alpha)
    {
        System.Text.StringBuilder sb = new System.Text.StringBuilder();
        for (int i = 0; i < paragraphs.Length; i++)
        {
            if (i > 0) sb.Append("\n\n");
            int a = i < current ? 255 : i == current ? Mathf.RoundToInt(alpha * 255f) : 0;
            sb.Append($"<alpha=#{a:X2}>").Append(paragraphs[i]);
        }
        return sb.ToString();
    }

    // 한 글자씩 (ESC를 누르면 바로 다 보임)
    private IEnumerator TypeText(TMP_Text text)
    {
        text.gameObject.SetActive(true);
        text.alpha = 1f;
        text.maxVisibleCharacters = 0;
        text.ForceMeshUpdate();
        int total = text.textInfo.characterCount;
        for (int i = 0; i < total && !skip; i++)
        {
            text.maxVisibleCharacters = i + 1;
            char c = text.textInfo.characterInfo[i].character;
            float wait = c == '\n' ? typeLinePause : (c == ' ' ? typeInterval * 0.5f : typeInterval);
            for (float t = 0f; t < wait && !skip; t += Time.deltaTime) yield return null;
        }
        text.maxVisibleCharacters = 99999;
        skip = false;   // 타이핑 건너뛰기에 쓴 ESC는 여기서 소진
    }

    // ESC를 누르면 바로 끝나는 대기
    private IEnumerator Wait(float seconds)
    {
        for (float t = 0f; t < seconds && !skip; t += Time.deltaTime) yield return null;
    }

    private IEnumerator Pop(TMP_Text text)
    {
        Transform t = text.transform;
        Vector3 baseScale = t.localScale;
        text.gameObject.SetActive(true);
        for (float x = 0f; x < fadeTime; x += Time.deltaTime)
        {
            float k = Mathf.Clamp01(x / fadeTime);
            float e = 1f - (1f - k) * (1f - k);
            t.localScale = baseScale * Mathf.Lerp(titleStartScale, 1f, e);
            text.alpha = k;
            yield return null;
        }
        t.localScale = baseScale;
        text.alpha = 1f;
    }

    private IEnumerator Fade(TMP_Text text, float from, float to)
    {
        if (text == null) yield break;
        text.gameObject.SetActive(true);
        for (float t = 0f; t < fadeTime; t += Time.deltaTime)
        {
            text.alpha = Mathf.Lerp(from, to, t / fadeTime);
            yield return null;
        }
        text.alpha = to;
        if (to <= 0f) text.gameObject.SetActive(false);
    }

    private static void Hide(TMP_Text text)
    {
        if (text != null) text.gameObject.SetActive(false);
    }

    private static CanvasGroup Group(GameObject go)
    {
        CanvasGroup g = go.GetComponent<CanvasGroup>();
        if (g == null) g = go.AddComponent<CanvasGroup>();
        return g;
    }

    // 보스전 결과 (Victory 아래). 결과 기록 없이 씬을 바로 열면 미리보기용 예시
    private static string BossResultText()
    {
        if (!GameResult.HasResult || !GameResult.IsBossFight)
        {
            return Application.isEditor
                ? "Grade  <b>A</b>\nTurns used  14 / 20\nWeakness hit rate  65%\nHP left  62\nSpells learned  24\nPlay time  18:42"
                : "";
        }
        string grade = string.IsNullOrEmpty(GameResult.BossGrade) ? "" : $"Grade  <b>{GameResult.BossGrade}</b>\n";
        return grade +
               $"Turns used  {GameResult.BossTurnsUsed} / {GameResult.BossMaxTurns}\n" +
               $"Weakness hit rate  {Mathf.RoundToInt(GameResult.BossWeaknessRate * 100f)}%\n" +
               $"HP left  {Mathf.CeilToInt(GameResult.PlayerHpLeft)}\n" +
               $"Spells learned  {GameResult.LearnedSpells}\n" +
               $"Play time  {GameResult.PlayTimeText}";
    }
}