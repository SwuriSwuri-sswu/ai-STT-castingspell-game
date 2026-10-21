using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

// GameScene 시작 연출 (Intro 두루마리 패널에 붙이기)
//
// 흐름
//   1. UI를 전부 숨기고, 몹 스폰을 잠시 멈춤
//   2. 두루마리가 펼쳐지듯 나타남
//   3. 스토리 페이지들을 타자 치듯 한 글자씩 (페이지마다 다 읽을 시간만큼 기다렸다가 다음 장)
//   4. UI 안내 페이지가 투명 → 서서히 나타남
//   5. 두루마리가 사라지고, 숨겼던 UI가 서서히 나타난 뒤 몹 스폰 시작
//
// 조작: 클릭 / Enter = 타자 중이면 바로 다 보여 주기, 다 보였으면 다음 장
//       Esc = 인트로 전체 건너뛰기
//       (Space는 음성 녹음 키라서 넘기기에 쓰지 않음)
public class IntroSequence : MonoBehaviour
{
    [Header("연결 (비우면 자동)")]
    [Tooltip("두루마리 패널. 비우면 이 오브젝트 (CanvasGroup 자동 추가)")]
    public CanvasGroup introPanel;
    [Tooltip("스토리를 타자로 칠 텍스트 (Intro_Text). 비우면 자식 중 첫 TextMeshPro")]
    public TMP_Text storyText;
    [Tooltip("(선택) UI 안내 페이지. 아이콘 이미지 + 설명 글을 직접 배치한 오브젝트.\n" +
             "비우면 아래 Guide Content 글을 Story Text 자리에 그대로 띄움")]
    public GameObject guidePage;
    [Tooltip("비우면 씬에서 자동으로 찾음")]
    public EnemyManager enemyManager;

    [Header("인트로 동안 숨길 UI")]
    [Tooltip("HPBar, PortraitFrame, Book, Wand, Settings, MusicToggle, Subtitle_Main 등.\n" +
             "꺼지지 않고 투명해지기만 해서 스크립트는 그대로 동작함")]
    public List<GameObject> hideDuringIntro = new List<GameObject>();

    [Header("1. 스토리 (타자 연출) — 한 칸 = 한 장")]
    [TextArea(5, 12)]
    public List<string> storyPages = new List<string>
    {
        "A lazy king who has never learned a single spell.\n" +
        "Once again, he won't budge from his throne.\n\n" +
        "But then... the <color=#A0281E><b>Demon Lord of Silence</b></color> awoke\n" +
        "and scattered the spells of the grimoire to the winds.",

        "The scattered spells hid inside the monsters,\n" +
        "and the monsters began swarming toward the castle.\n\n" +
        "For a king too lazy even to walk, only one option remains:\n" +
        "<b>chant the spells out loud.</b>",

        "The more monsters you defeat, the more spells you recover.\n\n" +
        "To defeat the <color=#A0281E><b>Demon Lord of Silence</b></color>,\n" +
        "gather as many spells as you can.",
    };
    [Tooltip("1초에 치는 글자 수")]
    public float charsPerSecond = 28f;
    [Tooltip("문장 부호(. ! ? ,)와 줄바꿈 뒤에 잠깐 쉬는 시간")]
    public float punctuationPause = 0.12f;
    [Tooltip("(선택) 타자 소리")]
    public AudioClip typeSound;
    [Tooltip("타자 소리를 몇 글자마다 낼지")]
    [Min(1)] public int typeSoundEvery = 2;
    [Range(0f, 1f)] public float typeSoundVolume = 0.5f;

    [System.Serializable]
    public class GuideIcon
    {
        [Tooltip("안내 글에서 바꿀 표시. 예: 도감 → 글 안의 {도감}이 아이콘으로 바뀜")]
        public string key;
        [Tooltip("버튼에 쓰는 아이콘 이미지 (비우면 글자로 굵게 표시)")]
        public Sprite sprite;
    }

    [Header("2. UI 안내 (서서히 나타남)")]
    [Tooltip("{도감}처럼 중괄호로 쓴 부분이 아래 아이콘 이미지로 바뀜")]
    [TextArea(5, 12)]
    public string guideContent =
        "Hold <b>[Space]</b> and chant a spell to cast magic.\n" +
        "<b>Elements</b>: Water > Fire > Grass > Water.\n" +
        "When your <b>HP</b> hits 0, it's game over!\n" +
        "Open the book to check each monster's attack power.\n\n" +
        "{Book}: Your collection of spells and monsters.\n" +
        "{Wand}: Check the wand you are using.\n" +
        "{Settings}: Open the settings.\n" +
        "{Music}: Turn the background music on or off.";
    public List<GuideIcon> guideIcons = new List<GuideIcon>
    {
        new GuideIcon { key = "Book" },
        new GuideIcon { key = "Wand" },
        new GuideIcon { key = "Settings" },
        new GuideIcon { key = "Music" },
    };
    [Tooltip("아이콘 크기 (줄 높이 기준 배수). 1 = 글자 줄 높이와 같음")]
    public float iconScale = 1.2f;
    [Tooltip("아이콘 자리 가로 폭 (글자 한 칸 기준 배수). 아이콘이 옆 글자와 겹치면 키우기")]
    public float iconSpace = 1.6f;
    public float guideFadeDuration = 1.2f;

    [Header("읽는 시간 (다 쓴 뒤 다음으로 넘어가기까지)")]
    [Tooltip("기본 대기 시간 (초)")]
    public float readBaseTime = 1.5f;
    [Tooltip("글자 하나당 더 기다리는 시간 (초). 한국어 기준 0.05~0.07 추천")]
    public float readTimePerChar = 0.06f;

    [Header("연출 시간")]
    [Tooltip("씬 시작 후 두루마리를 펼치기 전 대기 (초).\n" +
             "SceneFadeIn이 있으면 화면이 밝아질 때까지 자동으로 기다리므로 0이어도 됨")]
    public float openDelay = 0f;
    public float panelOpenDuration = 1.2f;
    public float pageFadeDuration = 0.4f;
    public float panelCloseDuration = 0.6f;
    public float uiFadeInDuration = 0.6f;
    [Tooltip("UI가 다 나타난 뒤 몹 스폰까지 대기 (EnemyManager의 첫 등장 대기와 별도)")]
    public float delayBeforeSpawn = 0.3f;
    [Tooltip("(선택) 게임 시작 때 알림 자막. 비우면 안 띄움")]
    public string startNotice = "";

    [Header("넘기기")]
    public bool allowSkip = true;
    public KeyCode nextKey = KeyCode.Return;
    public bool clickToNext = true;
    public KeyCode skipAllKey = KeyCode.Escape;
    [Tooltip("켜면 처음 한 번만 인트로를 보여 줌 (재도전할 때는 바로 시작)")]
    public bool playOnlyFirstTime = false;

    private const string SeenKey = "IntroSeen";

    private class HiddenUI
    {
        public CanvasGroup group;
        public bool addedGroup;
        public float alpha;
        public bool interactable;
        public bool blocksRaycasts;
    }

    private readonly List<HiddenUI> hidden = new List<HiddenUI>();
    private bool startSpawnAfter;
    private bool skipAll;
    private AudioSource audioSource;
    private Vector3 panelBaseScale = Vector3.one;

    public static bool IsPlaying { get; private set; }

    // =========================================================
    // 준비: 다른 스크립트의 Start보다 먼저 (Awake) UI 숨김·스폰 보류
    // =========================================================
    private void Awake()
    {
        if (introPanel == null)
        {
            introPanel = GetComponent<CanvasGroup>();
            if (introPanel == null) introPanel = gameObject.AddComponent<CanvasGroup>();
        }
        if (storyText == null) storyText = GetComponentInChildren<TMP_Text>(true);
        if (enemyManager == null) enemyManager = FindFirstObjectByType<EnemyManager>();

        // EnemyManager.Start에서 바로 스폰하지 않도록
        if (enemyManager != null)
        {
            startSpawnAfter = enemyManager.spawnOnStart;
            enemyManager.spawnOnStart = false;
        }

        HideUI();
        panelBaseScale = introPanel.transform.localScale;
        introPanel.alpha = 0f;
        if (guidePage != null) guidePage.SetActive(false);
        if (storyText != null) storyText.text = "";
        IsPlaying = true;
    }

    private IEnumerator Start()
    {
        if (playOnlyFirstTime && PlayerPrefs.GetInt(SeenKey, 0) == 1)
        {
            yield return FinishRoutine(instant: true);
            yield break;
        }

        if (typeSound != null)
        {
            audioSource = gameObject.AddComponent<AudioSource>();
            audioSource.playOnAwake = false;
            audioSource.spatialBlend = 0f;
        }

        yield return null; // 씬 페이드 인 등과 한 프레임 겹치지 않게

        // 검은 화면이 걷히기 전에 펼쳐지면 안 보이므로, SceneFadeIn이 끝날 때까지 대기
        float wait = openDelay;
        SceneFadeIn fade = FindFirstObjectByType<SceneFadeIn>();
        if (fade != null && fade.fadeScreen != null && fade.fadeScreen.gameObject.activeInHierarchy)
        {
            wait = Mathf.Max(wait, fade.fadeDuration * 0.8f);
        }
        if (wait > 0f) yield return new WaitForSecondsRealtime(wait);

        // 1. 두루마리 펼치기 (세로로 늘어나며 나타남)
        yield return Tween(panelOpenDuration, t =>
        {
            float e = EaseOutBack(t);
            introPanel.alpha = Mathf.Clamp01(t * 1.5f);
            introPanel.transform.localScale = new Vector3(panelBaseScale.x, panelBaseScale.y * Mathf.Lerp(0.3f, 1f, e), panelBaseScale.z);
        });
        introPanel.transform.localScale = panelBaseScale;

        // 2. 스토리 (타자)
        if (storyText != null)
        {
            for (int i = 0; i < storyPages.Count && !skipAll; i++)
            {
                if (string.IsNullOrWhiteSpace(storyPages[i])) continue;
                storyText.alpha = 1f;
                yield return TypeText(storyText, storyPages[i]);
                if (skipAll) break;
                yield return WaitOrNext(ReadTime(storyText.GetParsedText().Length));
                if (skipAll) break;
                yield return FadeText(storyText, 1f, 0f, pageFadeDuration);
            }
        }

        // 3. UI 안내 (서서히)
        if (!skipAll) yield return ShowGuide();

        yield return FinishRoutine(instant: false);
    }

    private void Update()
    {
        if (IsPlaying && allowSkip && Input.GetKeyDown(skipAllKey)) skipAll = true;
    }

    // =========================================================
    // 타자 연출
    // =========================================================
    private IEnumerator TypeText(TMP_Text text, string content)
    {
        text.text = content;
        text.maxVisibleCharacters = 0;
        text.ForceMeshUpdate();
        int total = text.textInfo.characterCount;
        float interval = 1f / Mathf.Max(1f, charsPerSecond);
        float timer = 0f;
        int shown = 0;

        while (shown < total)
        {
            if (skipAll || NextPressed()) break;

            timer -= Time.unscaledDeltaTime;
            while (timer <= 0f && shown < total)
            {
                char c = text.textInfo.characterInfo[shown].character;
                shown++;
                text.maxVisibleCharacters = shown;

                if (audioSource != null && !char.IsWhiteSpace(c) && shown % typeSoundEvery == 0)
                {
                    audioSource.pitch = Random.Range(0.92f, 1.08f);
                    audioSource.PlayOneShot(typeSound, typeSoundVolume);
                }
                else if (typeSound == null && !char.IsWhiteSpace(c))
                {
                    SoundManager.Typing();   // Type Sound가 비어 있으면 SoundLibrary의 타이핑 소리 (겹치지 않고, 쉬는 곳에서 멈춤)
                }

                timer += interval;
                if (c == '.' || c == '!' || c == '?' || c == ',' || c == '\n') timer += punctuationPause;
            }
            yield return null;
        }

        text.maxVisibleCharacters = 99999; // 남은 글자 전부
        yield return null;                 // 같은 클릭으로 다음 장까지 넘어가지 않게
    }

    // =========================================================
    // UI 안내 (투명 → 서서히)
    // =========================================================
    private IEnumerator ShowGuide()
    {
        if (guidePage != null)
        {
            if (storyText != null) storyText.text = "";
            CanvasGroup g = guidePage.GetComponent<CanvasGroup>();
            if (g == null) g = guidePage.AddComponent<CanvasGroup>();
            g.alpha = 0f;
            guidePage.SetActive(true);
            yield return Tween(guideFadeDuration, t => g.alpha = t);

            int length = 0;
            foreach (TMP_Text t in guidePage.GetComponentsInChildren<TMP_Text>()) length += t.GetParsedText().Length;
            yield return WaitOrNext(ReadTime(length));
        }
        else if (storyText != null && !string.IsNullOrWhiteSpace(guideContent))
        {
            // 글자 + 아이콘을 함께 서서히 (CanvasGroup)
            CanvasGroup g = storyText.GetComponent<CanvasGroup>();
            if (g == null) g = storyText.gameObject.AddComponent<CanvasGroup>();
            g.alpha = 0f;
            storyText.alpha = 1f;
            storyText.maxVisibleCharacters = 99999;
            storyText.text = BuildGuideText(guideContent);
            PlaceIcons(storyText);

            yield return Tween(guideFadeDuration, t => g.alpha = t);
            yield return WaitOrNext(ReadTime(storyText.GetParsedText().Length));
        }
    }

    // {도감} → 투명한 자리 표시 글자 (나중에 그 위치에 아이콘 이미지를 올림)
    private string BuildGuideText(string content)
    {
        for (int i = 0; i < guideIcons.Count; i++)
        {
            GuideIcon icon = guideIcons[i];
            if (icon == null || string.IsNullOrEmpty(icon.key)) continue;
            string token = "{" + icon.key + "}";
            string pad = $"<space={Mathf.Max(0f, iconSpace - 1f) * 0.5f:0.##}em>";
            string replace = icon.sprite != null
                ? $"{pad}<link=\"icon{i}\"><color=#00000000>M</color></link>{pad}"
                : $"<b>{icon.key}</b>:";
            content = content.Replace(token, replace);
        }
        return content;
    }

    private void PlaceIcons(TMP_Text text)
    {
        text.ForceMeshUpdate();
        TMP_TextInfo info = text.textInfo;
        for (int l = 0; l < info.linkCount; l++)
        {
            TMP_LinkInfo link = info.linkInfo[l];
            string id = link.GetLinkID();
            if (!id.StartsWith("icon") || !int.TryParse(id.Substring(4), out int index)) continue;
            if (index < 0 || index >= guideIcons.Count || guideIcons[index].sprite == null) continue;
            if (link.linkTextLength <= 0) continue;

            TMP_CharacterInfo c = info.characterInfo[link.linkTextfirstCharacterIndex];
            TMP_LineInfo line = info.lineInfo[c.lineNumber];
            float lineHeight = line.ascender - line.descender;
            float size = lineHeight * iconScale;

            GameObject go = new GameObject("GuideIcon_" + guideIcons[index].key, typeof(RectTransform), typeof(UnityEngine.UI.Image));
            RectTransform rt = (RectTransform)go.transform;
            rt.SetParent(text.rectTransform, false);
            rt.sizeDelta = new Vector2(size, size);
            rt.localPosition = new Vector3((c.origin + c.xAdvance) * 0.5f, (line.ascender + line.descender) * 0.5f, 0f);

            UnityEngine.UI.Image img = go.GetComponent<UnityEngine.UI.Image>();
            img.sprite = guideIcons[index].sprite;
            img.preserveAspect = true;
            img.raycastTarget = false;
        }
    }

    // =========================================================
    // 마무리: 패널 닫기 → UI 표시 → 스폰 시작
    // =========================================================
    private IEnumerator FinishRoutine(bool instant)
    {
        PlayerPrefs.SetInt(SeenKey, 1);

        if (!instant)
        {
            float startAlpha = introPanel.alpha;
            yield return Tween(panelCloseDuration, t =>
            {
                introPanel.alpha = Mathf.Lerp(startAlpha, 0f, t);
                introPanel.transform.localScale = panelBaseScale * Mathf.Lerp(1f, 0.92f, t);
            });
        }
        introPanel.alpha = 0f;
        introPanel.blocksRaycasts = false;

        yield return RestoreUI(instant ? 0f : uiFadeInDuration);

        if (!instant && delayBeforeSpawn > 0f) yield return new WaitForSecondsRealtime(delayBeforeSpawn);

        IsPlaying = false;
        if (enemyManager != null && startSpawnAfter) enemyManager.StartSpawning();
        if (!string.IsNullOrEmpty(startNotice) && BattleUIManager.Instance != null) BattleUIManager.Instance.ShowNotice(startNotice);

        introPanel.transform.localScale = panelBaseScale;
        gameObject.SetActive(false);
    }

    // =========================================================
    // UI 숨기기 / 되돌리기 (꺼지지 않고 투명하게만)
    // =========================================================
    private void HideUI()
    {
        foreach (GameObject go in hideDuringIntro)
        {
            if (go == null) continue;
            CanvasGroup g = go.GetComponent<CanvasGroup>();
            bool added = g == null;
            if (added) g = go.AddComponent<CanvasGroup>();
            hidden.Add(new HiddenUI
            {
                group = g,
                addedGroup = added,
                alpha = g.alpha,
                interactable = g.interactable,
                blocksRaycasts = g.blocksRaycasts,
            });
            g.alpha = 0f;
            g.interactable = false;
            g.blocksRaycasts = false;
        }
    }

    private IEnumerator RestoreUI(float duration)
    {
        if (duration > 0f)
        {
            yield return Tween(duration, t =>
            {
                foreach (HiddenUI h in hidden) if (h.group != null) h.group.alpha = h.alpha * t;
            });
        }
        foreach (HiddenUI h in hidden)
        {
            if (h.group == null) continue;
            h.group.alpha = h.alpha;
            h.group.interactable = h.interactable;
            h.group.blocksRaycasts = h.blocksRaycasts;
            if (h.addedGroup) Destroy(h.group); // 원래 없던 CanvasGroup은 정리
        }
        hidden.Clear();
    }

    // =========================================================
    // 도우미
    // =========================================================
    private float ReadTime(int chars) => readBaseTime + chars * readTimePerChar;

    private bool NextPressed()
    {
        if (!allowSkip) return false;
        return Input.GetKeyDown(nextKey) || Input.GetKeyDown(KeyCode.KeypadEnter) || (clickToNext && Input.GetMouseButtonDown(0));
    }

    // 시간이 지나거나, 다음 키를 누르거나, 전체 건너뛰기면 끝
    private IEnumerator WaitOrNext(float seconds)
    {
        float end = Time.unscaledTime + seconds;
        while (Time.unscaledTime < end && !skipAll)
        {
            if (NextPressed()) { yield return null; yield break; }
            yield return null;
        }
    }

    private IEnumerator FadeText(TMP_Text text, float from, float to, float duration)
    {
        yield return Tween(duration, t => text.alpha = Mathf.Lerp(from, to, t));
    }

    private IEnumerator Tween(float duration, System.Action<float> step)
    {
        if (duration <= 0f) { step(1f); yield break; }
        float time = 0f;
        while (time < duration)
        {
            time += Time.unscaledDeltaTime;
            step(Mathf.Clamp01(time / duration));
            yield return null;
        }
        step(1f);
    }

    private static float EaseOutBack(float t)
    {
        const float c1 = 1.2f, c3 = c1 + 1f;
        float x = t - 1f;
        return 1f + c3 * x * x * x + c1 * x * x;
    }

    [ContextMenu("인트로 다시 보기 기록 초기화 (테스트용)")]
    private void ClearSeen() => PlayerPrefs.DeleteKey(SeenKey);
}