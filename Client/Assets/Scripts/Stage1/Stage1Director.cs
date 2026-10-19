using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// 스테이지 1 진행 담당 (GameScene의 [SYSTEM_MANAGERS] 아래 빈 오브젝트에 붙이기)
//
// 1. 휴식 지점: N마리(기본 10) 처치마다 몹 등장이 멈추고 체력 회복(최대 체력의 50%)
//               → 도전 패널 "마왕에게 도전할까요? / 더 모으기"
// 2. 상성 처치 회복: 상성 우위 마법으로 마지막 일격을 넣으면 체력 +5
// 3. 도전 버튼: N마리(기본 10)부터 화면에 「마왕에게 도전」 버튼 → 누르면 확인 패널 (그동안 게임 일시정지)
//               보스 클리어 이력이 있으면(2회차) 처음부터 「스테이지 1 건너뛰기」로 표시
// 4. 최대 처치 수(GameSessionManager의 Max Stage1 Kills, 기본 50) 도달 → 자동으로 BossScene
public class Stage1Director : MonoBehaviour
{
    [Header("연결 (비우면 자동)")]
    public EnemyManager enemyManager;
    public GameSessionManager session;

    [Header("1. 휴식 지점")]
    [Tooltip("몇 마리 처치마다 휴식할지 (0 = 휴식 없음)")]
    [Min(0)] public int restEvery = 10;
    [Tooltip("휴식 때 회복량 (최대 체력 대비 비율, 0.5 = 50%)")]
    [Range(0f, 1f)] public float restHealRatio = 0.5f;
    [Tooltip("휴식 알림 후 회복까지 대기 (초)")]
    public float healDelay = 1f;
    [Tooltip("회복 후 다음 진행까지 최소 대기 (초)")]
    public float restHoldTime = 2f;
    public string restNotice = "The monsters have paused their march... Take a breather.";
    [Tooltip("{heal} = 회복량")]
    public string healNotice = "Recovered {heal} HP!";
    [Tooltip("휴식 때 도전 패널 띄우기 (끄면 Rest Hold Time 뒤 바로 다음 몹)")]
    public bool askAtRest = true;

    [Header("2. 상성 처치 회복")]
    public bool healOnWeaknessKill = true;
    public float weaknessKillHeal = 5f;

    [Header("3. 도전 버튼")]
    [Tooltip("화면 구석의 「마왕에게 도전」 버튼 (처음엔 숨겨 두고 조건이 되면 나타남)")]
    public Button challengeButton;
    [Tooltip("(선택) 버튼 글자")]
    public TMP_Text challengeButtonLabel;
    [Tooltip("이 처치 수부터 버튼 표시")]
    [Min(0)] public int challengeUnlockKills = 10;
    public string challengeLabel = "Challenge!";
    [Tooltip("보스 클리어 이력이 있을 때(2회차), 버튼이 처음부터 이 글자로 나타남")]
    public string skipLabel = "Skip";

    [Header("도전 확인 패널")]
    [Tooltip("확인 패널 (처음엔 꺼 두기)")]
    public GameObject promptPanel;
    public TMP_Text promptTitle;
    public TMP_Text promptBody;
    [Tooltip("「도전」 버튼")]
    public Button confirmButton;
    [Tooltip("「더 모으기」 버튼")]
    public Button cancelButton;
    public string promptTitleText = "Challenge the Demon Lord?";
    public string restTitleText = "Catching your breath...";
    [Tooltip("{kills} {max} {spells} {elements} {hp} {hpmax} {missing}")]
    [TextArea(3, 8)]
    public string promptBodyFormat =
        "Monsters defeated  {kills} / {max}\n" +
        "Spells collected  {spells}\n" +
        "{elements}\n" +
        "HP  {hp} / {hpmax}\n" +
        "{missing}";
    [Tooltip("{element} = 아직 하나도 없는 속성들")]
    public string missingFormat = "<size=85%>No {element} spells yet.</size>";
    public string allElementsText = "<size=85%>You have spells of every element!</size>";
    [Tooltip("속성 사이 구분 기호. 폰트에 없는 글자(·)는 네모로 보이니 / | , 처럼 기본 글자 사용")]
    public string elementSeparator = "   /   ";

    [Header("4. 최대 처치 수 도달")]
    public string limitNotice = "The monster horde has ended... You sense the Demon Lord's presence!";
    [Tooltip("알림 후 보스 씬으로 넘어가기까지 (초)")]
    public float limitMoveDelay = 2.5f;

    [Header("씬 이동")]
    public string bossSceneName = "BossScene";
    [Tooltip("(선택) 검은 화면 CanvasGroup. 비우면 자동 생성")]
    public CanvasGroup fadeOverlay;
    public float fadeOutTime = 1.2f;
    public string leaveNotice = "Heading to the Demon Lord's castle...";

    private bool busy;          // 휴식 중 / 이동 준비 중
    private bool leaving;       // 씬 이동 시작
    private bool promptOpen;
    private int promptChoice;   // 0 = 대기, 1 = 도전, -1 = 더 모으기
    private bool buttonShown;
    private Vector3 buttonBaseScale = Vector3.one;

    private void Awake()
    {
        if (enemyManager == null) enemyManager = FindFirstObjectByType<EnemyManager>();
        if (session == null) session = FindFirstObjectByType<GameSessionManager>();
        StageProgress.ResetRun();

        if (challengeButton != null)
        {
            buttonBaseScale = challengeButton.transform.localScale;
            challengeButton.onClick.AddListener(OnClickChallenge);
            challengeButton.gameObject.SetActive(false);
        }
        if (confirmButton != null) confirmButton.onClick.AddListener(() => promptChoice = 1);
        if (cancelButton != null) cancelButton.onClick.AddListener(() => promptChoice = -1);
        if (promptPanel != null) promptPanel.SetActive(false);
    }

    private void OnEnable()
    {
        if (session != null)
        {
            session.OnKillCountChanged += HandleKillCount;
            session.OnStage1KillLimitReached += HandleKillLimit;
        }
        if (enemyManager != null) enemyManager.OnEnemySpawned += HandleEnemySpawned;
    }

    private void OnDisable()
    {
        if (session != null)
        {
            session.OnKillCountChanged -= HandleKillCount;
            session.OnStage1KillLimitReached -= HandleKillLimit;
        }
        if (enemyManager != null) enemyManager.OnEnemySpawned -= HandleEnemySpawned;
        if (promptOpen) GamePause.Release(this);
    }

    private int Kills => session != null ? session.TotalKills : 0;
    private int MaxKills => session != null ? session.maxStage1Kills : 50;
    private bool PlayerDead => PlayerHealth.Instance != null && PlayerHealth.Instance.IsDead;

    // =========================================================
    // 도전 버튼 표시
    // =========================================================
    private void Update()
    {
        bool unlocked = StageProgress.BossCleared || Kills >= challengeUnlockKills;
        bool show = challengeButton != null && unlocked && !leaving && !PlayerDead && !IntroSequence.IsPlaying;
        if (show == buttonShown) return;

        buttonShown = show;
        if (challengeButton == null) return;
        challengeButton.gameObject.SetActive(show);
        if (show) StartCoroutine(PopIn(challengeButton.transform));
        if (challengeButtonLabel != null)
        {
            bool skip = StageProgress.BossCleared && Kills < challengeUnlockKills;
            challengeButtonLabel.text = skip ? skipLabel : challengeLabel;
        }
    }

    private void HandleKillCount(int kills, int max)
    {
        if (challengeButtonLabel != null && kills >= challengeUnlockKills) challengeButtonLabel.text = challengeLabel;

        if (kills <= 0 || kills >= max || restEvery <= 0) return; // 최대치는 HandleKillLimit에서
        if (kills % restEvery != 0 || busy || leaving) return;
        StartCoroutine(RestRoutine());
    }

    // =========================================================
    // 1. 휴식 지점
    // =========================================================
    private IEnumerator RestRoutine()
    {
        busy = true;
        if (enemyManager != null) enemyManager.StopSpawning(); // 다음 몹 예약 막기 (지금 몹은 쓰러지는 중)

        yield return WaitForFieldClear();
        if (PlayerDead || leaving) { busy = false; yield break; }

        Notice(restNotice);
        yield return new WaitForSecondsRealtime(healDelay);

        PlayerHealth hp = PlayerHealth.Instance;
        if (hp != null && restHealRatio > 0f)
        {
            float healed = hp.Heal(hp.maxHP * restHealRatio, "Rest");
            if (healed > 0f) Notice(healNotice.Replace("{heal}", Mathf.CeilToInt(healed).ToString()));
        }
        yield return new WaitForSecondsRealtime(restHoldTime);

        if (askAtRest && promptPanel != null)
        {
            yield return AskChallenge(restTitleText);
            if (promptChoice == 1) { yield return GoToBoss(); yield break; }
        }

        busy = false;
        if (!leaving && !PlayerDead && enemyManager != null) enemyManager.StartSpawning();
    }

    // =========================================================
    // 2. 상성 처치 회복
    // =========================================================
    private void HandleEnemySpawned(EnemyController enemy)
    {
        if (enemy != null) enemy.OnDied += HandleEnemyDied;
    }

    private void HandleEnemyDied(EnemyController enemy)
    {
        enemy.OnDied -= HandleEnemyDied;
        if (!healOnWeaknessKill || weaknessKillHeal <= 0f || PlayerHealth.Instance == null) return;
        if (enemy.LastHitMultiplier > 1f) PlayerHealth.Instance.Heal(weaknessKillHeal, "Weakness kill");
    }

    // =========================================================
    // 3. 도전 버튼 / 확인 패널
    // =========================================================
    public void OnClickChallenge()
    {
        if (promptOpen || leaving || PlayerDead) return;
        StartCoroutine(ChallengeFromButton());
    }

    private IEnumerator ChallengeFromButton()
    {
        if (promptPanel == null)
        {
            // 패널이 없으면 바로 이동
            yield return GoToBoss();
            yield break;
        }
        yield return AskChallenge(promptTitleText);
        if (promptChoice == 1) yield return GoToBoss();
    }

    // 패널을 띄우고 선택을 기다림 (그동안 게임 일시정지)
    private IEnumerator AskChallenge(string title)
    {
        promptChoice = 0;
        promptOpen = true;
        if (promptTitle != null) promptTitle.text = title;
        if (promptBody != null) promptBody.text = BuildBody();
        promptPanel.SetActive(true);
        GamePause.Request(this);

        while (promptChoice == 0 && !leaving) yield return null;

        GamePause.Release(this);
        promptPanel.SetActive(false);
        promptOpen = false;
    }

    private string BuildBody()
    {
        int[] count = new int[4];
        int total = 0;
        if (SkillManager.Instance != null)
        {
            foreach (SpellData s in SkillManager.Instance.UnlockedSpells)
            {
                if (s == null) continue;
                count[(int)s.elementType]++;
                total++;
            }
        }

        MagicElement[] order = { MagicElement.Normal, MagicElement.Water, MagicElement.Fire, MagicElement.Grass };
        System.Text.StringBuilder elements = new System.Text.StringBuilder();
        System.Text.StringBuilder missing = new System.Text.StringBuilder();
        foreach (MagicElement e in order)
        {
            string hex = ColorUtility.ToHtmlStringRGB(e == MagicElement.Normal ? new Color(0.25f, 0.2f, 0.15f) : EnemyController.ElementColor(e));
            if (elements.Length > 0) elements.Append(elementSeparator);
            elements.Append($"<color=#{hex}>{EnemyController.ElementName(e)} {count[(int)e]}</color>");
            if (e != MagicElement.Normal && count[(int)e] == 0)
            {
                if (missing.Length > 0) missing.Append(", ");
                missing.Append($"<color=#{hex}>{EnemyController.ElementName(e)}</color>");
            }
        }

        PlayerHealth hp = PlayerHealth.Instance;
        return promptBodyFormat
            .Replace("{kills}", Kills.ToString())
            .Replace("{max}", MaxKills.ToString())
            .Replace("{spells}", total.ToString())
            .Replace("{elements}", elements.ToString())
            .Replace("{hp}", hp != null ? Mathf.CeilToInt(hp.CurrentHP).ToString() : "-")
            .Replace("{hpmax}", hp != null ? Mathf.CeilToInt(hp.maxHP).ToString() : "-")
            .Replace("{missing}", missing.Length > 0 ? missingFormat.Replace("{element}", missing.ToString()) : allElementsText);
    }

    // =========================================================
    // 4. 최대 처치 수 → 자동 이동
    // =========================================================
    private void HandleKillLimit()
    {
        if (!leaving) StartCoroutine(KillLimitRoutine());
    }

    private IEnumerator KillLimitRoutine()
    {
        busy = true;
        if (enemyManager != null) enemyManager.StopSpawning();
        yield return WaitForFieldClear();
        if (PlayerDead || leaving) yield break;

        Notice(limitNotice);
        yield return new WaitForSecondsRealtime(limitMoveDelay);
        yield return GoToBoss();
    }

    // =========================================================
    // 씬 이동
    // =========================================================
    private IEnumerator GoToBoss()
    {
        if (leaving) yield break;

        if (!Application.CanStreamedLevelBeLoaded(bossSceneName))
        {
            Debug.LogError($"[Stage1Director] '{bossSceneName}' 씬이 Build Settings에 없습니다. File → Build Profiles(Build Settings)에 추가해 주세요.");
            Notice($"{bossSceneName} is not in Build Settings");
            busy = false;
            if (!PlayerDead && enemyManager != null) enemyManager.StartSpawning();
            yield break;
        }

        leaving = true;
        if (enemyManager != null) enemyManager.StopSpawning();

        StageProgress.Stage1Kills = Kills;
        StageProgress.SkippedStage1 = Kills < challengeUnlockKills && StageProgress.BossCleared;
        if (PlayerHealth.Instance != null) StageProgress.Stage1HpRatio = PlayerHealth.Instance.Ratio;

        Notice(leaveNotice);

        if (fadeOverlay == null) fadeOverlay = CreateFadeOverlay();
        fadeOverlay.gameObject.SetActive(true);
        fadeOverlay.blocksRaycasts = true;
        float start = fadeOverlay.alpha;
        float t = 0f;
        while (t < fadeOutTime)
        {
            t += Time.unscaledDeltaTime;
            fadeOverlay.alpha = Mathf.Lerp(start, 1f, t / fadeOutTime);
            yield return null;
        }
        fadeOverlay.alpha = 1f;

        // 서버에 스테이지 1 클리어 저장 (세션이 있을 때만)
        if (GameManager.Instance != null && NetworkManager.Instance != null && NetworkManager.Instance.HasSession)
        {
            GameManager.Instance.SetState(GameState.StageClear);
        }

        Time.timeScale = 1f;
        SceneManager.LoadScene(bossSceneName);
    }

    // =========================================================
    // 도우미
    // =========================================================
    // 쓰러지는 몹이 사라지고, 떨어진 주문(드랍)까지 다 주울 때까지 대기
    private IEnumerator WaitForFieldClear()
    {
        float limit = 12f;
        while (limit > 0f)
        {
            bool enemyGone = enemyManager == null || enemyManager.CurrentEnemy == null;
            bool dropsGone = FindObjectsByType<DropItem>(FindObjectsSortMode.None).Length == 0;
            if (enemyGone && dropsGone) break;
            limit -= Time.unscaledDeltaTime;
            yield return null;
        }
        yield return new WaitForSecondsRealtime(0.5f);
    }

    private static void Notice(string message)
    {
        if (!string.IsNullOrEmpty(message) && BattleUIManager.Instance != null) BattleUIManager.Instance.ShowNotice(message);
    }

    private IEnumerator PopIn(Transform t)
    {
        float time = 0f;
        const float duration = 0.35f;
        while (time < duration && t != null)
        {
            time += Time.unscaledDeltaTime;
            float x = Mathf.Clamp01(time / duration) - 1f;
            float e = 1f + 2.2f * x * x * x + 1.2f * x * x; // ease out back
            t.localScale = buttonBaseScale * e;
            yield return null;
        }
        if (t != null) t.localScale = buttonBaseScale;
    }

    private CanvasGroup CreateFadeOverlay()
    {
        GameObject go = new GameObject("StageFadeOverlay", typeof(RectTransform), typeof(Canvas), typeof(CanvasGroup), typeof(Image));
        Canvas canvas = go.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 999;
        Image img = go.GetComponent<Image>();
        img.color = Color.black;
        CanvasGroup g = go.GetComponent<CanvasGroup>();
        g.alpha = 0f;
        return g;
    }

    [ContextMenu("보스 클리어 기록 지우기 (테스트용)")]
    private void ClearBossRecord() => StageProgress.ClearBossRecord();

    [ContextMenu("보스 클리어 기록 만들기 (2회차 테스트용)")]
    private void FakeBossCleared() => StageProgress.MarkBossCleared();
}