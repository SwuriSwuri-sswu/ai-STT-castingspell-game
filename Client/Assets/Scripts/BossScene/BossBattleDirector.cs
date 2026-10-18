using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// 스테이지 2 (보스전) 진행 담당 (BossScene의 [SYSTEM_MANAGERS] 아래 빈 오브젝트에 붙이기)
//
// 흐름: 입장(카메라가 성 안으로 걸어 들어감) → 보스 일어남 + 체력바 등장
//       → [내 턴(주문 1번, 제한 시간) → 보스 턴] 반복, 체력에 따라 페이즈 1→2→3
//       → 승리: 보스 쓰러짐 → 결과(등급) → EndingScene / 패배(체력 0 또는 턴 소진) → EndingScene ('다시하기'는 보스전부터)
//
// 페이즈마다 보스 속성이 바뀜(물→불→풀) / 페이즈 2부터 내 턴에 하수인 등장(제한 시간 안에 못 잡으면 공격)
// 회복: 페이즈 전환 30% · 하수인 처치 +5 · 상성 적중 +3 / 상성 2연속 적중 → 보스 기절(보스 턴 1회 쉼)
public class BossBattleDirector : MonoBehaviour
{
    [Header("연결")]
    public BossController boss;
    public BossHUD hud;

    [Header("입장 연출")]
    public bool playEntrance = true;
    [Tooltip("카메라가 걸어갈 지점들 (성 밖 → 성문 → 성 안 → 왕좌 앞). 마지막 지점에서 멈춤")]
    public Transform[] entrancePath;
    [Tooltip("천천히 걷는 속도")]
    public float walkSpeed = 1.6f;
    [Tooltip("걸을 때 위아래 흔들림 (발걸음마다 살짝 내려앉음)")]
    public float headBob = 0.035f;
    [Tooltip("걸을 때 좌우 흔들림 (두 걸음에 한 번 왼쪽→오른쪽)")]
    public float headSway = 0.025f;
    [Tooltip("걸을 때 고개가 좌우로 살짝 기우는 각도 (도)")]
    public float headRoll = 0.8f;
    [Tooltip("발걸음마다 고개가 살짝 끄덕이는 각도 (도)")]
    public float headNod = 0.5f;
    [Tooltip("1초에 걷는 걸음 수")]
    public float stepsPerSecond = 1.7f;
    [Tooltip("Esc로 입장 연출 건너뛰기")]
    public bool allowSkipEntrance = true;

    [Header("입장 중 왕좌 줌인 (멀리 앉아 있는 마왕 보여 주기)")]
    [Tooltip("이 번호의 지점(0부터)에 도착하면 줌인 (-1 = 줌인 없음). 예: 성문 지점")]
    public int zoomAtPoint = 1;
    [Tooltip("줌인했을 때 시야각 (작을수록 크게 당겨짐). 기본 시야각은 60")]
    public float zoomFOV = 14f;
    public float zoomInTime = 1.4f;
    public float zoomHoldTime = 2f;
    public float zoomOutTime = 1f;
    [Tooltip("줌인할 때 숨길 것 (지팡이 WandRig 등, 같이 확대되면 어색함)")]
    public GameObject[] hideDuringZoom;
    [Tooltip("줌인할 때 자막 (비우면 없음)")]
    public string zoomNotice = "왕좌에 누군가가 앉아 있다…";

    [Header("입장 동안 숨길 UI (체력바·버튼·자막 등)")]
    [Tooltip("꺼지지 않고 투명하게만 → 보스전 시작 때 서서히 나타남")]
    public List<GameObject> hideDuringIntro = new List<GameObject>();
    public float uiFadeInTime = 0.6f;

    [Header("보스 등장 후 안내 패널")]
    [Tooltip("(선택) 보스전 규칙 안내 패널. 비우면 생략")]
    public GameObject tutorialPanel;
    [Tooltip("(선택) 안내 글. 비우면 패널에 적어 둔 글 그대로")]
    public TMPro.TMP_Text tutorialText;
    [TextArea(4, 10)]
    public string tutorialContent =
        "<b>턴제 전투!</b> 나의 턴마다 <b>주문을 한 번</b> 외칠 수 있습니다.\n" +
        "마왕은 체력이 줄 때마다 <b>속성이 바뀝니다.</b> 위쪽의 '약점'을 노리세요.\n" +
        "상성 마법으로 맞히면 체력을 회복하고, 마왕이 모으는 마법도 끊어낼 수 있습니다.\n" +
        "하수인이 나타나면 먼저 처치하세요!\n" +
        "<b>30턴</b> 안에 마왕을 쓰러뜨려야 합니다.";
    [Tooltip("안내를 보여 주는 시간 (클릭 / Enter로 바로 닫기)")]
    public float tutorialTime = 7f;
    public string entranceNotice = "침묵의 마왕이 모습을 드러냈다!";

    [Header("턴")]
    public float playerTurnTime = 10f;
    [Tooltip("페이즈 3의 내 턴 시간")]
    public float phase3TurnTime = 8f;
    public int maxTurns = 30;
    [Tooltip("시간이 끝나는 순간 녹음·판정·시전 연출 중이면 이만큼(초)까지 기다려 줌")]
    public float castGraceTime = 4f;
    [Tooltip("마법이 맞은 뒤 보스 턴까지")]
    public float afterHitDelay = 1f;
    public string playerTurnText = "나의 턴!";
    public string bossTurnText = "마왕의 턴";
    public Color playerTurnColor = new Color(1f, 0.85f, 0.3f);
    public Color bossTurnColor = new Color(0.95f, 0.25f, 0.2f);
    [Tooltip("내 턴이 아닐 때 영창 자막 대신 보여 줄 안내")]
    public string waitHint = "마왕의 턴입니다…";
    public string timeoutText = "시간 초과!";

    [Header("보스 공격 패턴 (페이즈 1·2·3 확률)")]
    public float[] chargeChance = { 0f, 0.35f, 0.3f };
    public float[] doubleChance = { 0f, 0f, 0.3f };
    [Tooltip("{weak} = 약점 속성")]
    public string chargeNotice = "마왕이 힘을 모으고 있다! {weak} 속성 마법으로 끊어내자!";
    public string chargeBreakNotice = "마왕의 마법을 끊어냈다!";
    public string chargeReleaseNotice = "모아 둔 마법이 터진다!";

    [Header("하수인 (페이즈 2부터)")]
    [Tooltip("하급 몹 프리팹들 (랜덤)")]
    public EnemyController[] minionPrefabs;
    [Tooltip("하수인이 나타날 위치들 (보스 옆)")]
    public Transform[] minionPoints;
    public float[] summonChance = { 0f, 0.35f, 0.55f };
    public int[] maxMinions = { 0, 1, 2 };
    [Tooltip("이 시간(초, 내 턴 동안만 흐름) 안에 처치하지 못하면 공격")]
    public float minionFuse = 12f;
    public float minionDamage = 5f;
    [Tooltip("(선택) 하수인이 나타날 때 이펙트")]
    public GameObject minionSpawnEffect;
    public string summonNotice = "하수인이 소환되었다! 먼저 처치하자!";

    [Header("회복")]
    [Range(0f, 1f)] public float phaseHealRatio = 0.3f;
    public float minionKillHeal = 5f;
    public float weaknessHitHeal = 3f;

    [Header("기절")]
    public bool stunOnWeaknessStreak = true;
    [Tooltip("상성 마법을 이만큼 연속으로 맞히면 보스가 기절해 다음 보스 턴을 쉼")]
    public int stunStreak = 2;
    public string stunNotice = "마왕이 비틀거린다! 공격할 틈이 없다!";

    [Header("결과")]
    public string endingSceneName = "EndingScene";
    public string victoryNotice = "침묵의 마왕을 물리쳤다!";
    public string turnOutNotice = "시간이 다했다… 마왕의 침묵이 성을 뒤덮는다.";
    public float fadeOutTime = 1.5f;
    [Tooltip("등급 기준 점수 (S / A / B, 나머지는 C). 점수 = 남은 턴 40 + 상성 적중률 30 + 남은 체력 20 + 영창 세기 10")]
    public float gradeS = 70f, gradeA = 55f, gradeB = 40f;

    // 상태
    private int turnsUsed;
    private int bossHits, weaknessHits, weaknessStreak;
    private int casts;
    private float powerSum;
    private bool stunned;
    private bool ended;
    private float startTime;
    private EnemyController castTarget;
    private bool castThisTurn;

    private class Minion
    {
        public EnemyController enemy;
        public float fuse;
    }
    private readonly List<Minion> minions = new List<Minion>();

    private VoiceSender sender;
    private VoiceRecorder recorder;

    private bool PlayerDead => PlayerHealth.Instance != null && PlayerHealth.Instance.IsDead;
    private bool BossDown => boss == null || boss.Enemy.IsDying || boss.Enemy.CurrentHP <= 0f;

    // =========================================================
    // 시작
    // =========================================================
    private void Start()
    {
        if (boss == null) boss = FindFirstObjectByType<BossController>();
        if (hud == null) hud = FindFirstObjectByType<BossHUD>();
        sender = FindFirstObjectByType<VoiceSender>();
        recorder = FindFirstObjectByType<VoiceRecorder>();
        if (boss == null)
        {
            Debug.LogError("[BossBattleDirector] 씬에 BossController가 없습니다.");
            return;
        }

        Block(waitHint);
        GameResult.ClearBoss();
        GameResult.IsBossFight = true;
        GameResult.BossMaxTurns = maxTurns;
        startTime = Time.time;

        boss.Enemy.OnDamaged += HandleBossDamaged;
        if (SkillManager.Instance != null) SkillManager.Instance.OnSpellCast += HandleSpellCast;
        if (PlayerHealth.Instance != null) PlayerHealth.Instance.OnDied += HandlePlayerDied;
        if (hud != null) hud.Hide();

        StartCoroutine(Run());
    }

    private void OnDestroy()
    {
        // 다른 씬에 영창 잠금이 남지 않게
        SkillManager.CastBlocked = false;
        SkillManager.BlockedHint = null;
        if (boss != null && boss.Enemy != null) boss.Enemy.OnDamaged -= HandleBossDamaged;
        if (SkillManager.Instance != null) SkillManager.Instance.OnSpellCast -= HandleSpellCast;
        if (PlayerHealth.Instance != null) PlayerHealth.Instance.OnDied -= HandlePlayerDied;
    }

    private static void Block(string hint)
    {
        SkillManager.CastBlocked = true;
        SkillManager.BlockedHint = hint;
    }

    private static void Unblock()
    {
        SkillManager.CastBlocked = false;
        SkillManager.BlockedHint = null;
    }

    private static void Notice(string message)
    {
        if (!string.IsNullOrEmpty(message) && BattleUIManager.Instance != null) BattleUIManager.Instance.ShowNotice(message);
    }

    // =========================================================
    // 전체 흐름
    // =========================================================
    private IEnumerator Run()
    {
        yield return Entrance();
        RefreshHUD();

        while (!ended)
        {
            if (PlayerDead) yield break;
            yield return PlayerTurn();
            if (PlayerDead || ended) yield break;

            if (BossDown) { yield return Victory(); yield break; }

            turnsUsed++;
            RefreshHUD();
            if (turnsUsed >= maxTurns) { yield return Defeat(); yield break; }

            int targetPhase = boss.PhaseForHP();
            if (targetPhase > boss.Phase)
            {
                // 페이즈 전환이 보스 턴을 대신함 (숨 돌릴 틈)
                while (boss.Phase < targetPhase) yield return PhaseTransition(boss.Phase + 1);
            }
            else if (stunned)
            {
                stunned = false;
                Notice(stunNotice);
                yield return new WaitForSeconds(1.5f);
            }
            else
            {
                yield return BossTurn();
            }
            if (PlayerDead) yield break;

            yield return MaybeSummon();
        }
    }

    // =========================================================
    // 입장
    // =========================================================
    private IEnumerator Entrance()
    {
        HideUI();
        if (tutorialPanel != null) tutorialPanel.SetActive(false);

        Camera cam = Camera.main;
        if (playEntrance && cam != null && entrancePath != null && entrancePath.Length > 0)
        {
            Transform ct = cam.transform;
            float stepPhase = 0f;    // 한 걸음 = π
            float walkBlend = 0f;    // 출발·정지 때 흔들림을 부드럽게 키우고 줄임
            Quaternion baseRot = ct.rotation;
            Vector3 flatPos = ct.position;   // 흔들림을 뺀 실제 걷는 위치
            for (int i = 0; i < entrancePath.Length; i++)
            {
                Transform point = entrancePath[i];
                if (point == null) continue;
                Vector3 target = point.position;
                while (!skipEntrance && (flatPos - target).sqrMagnitude > 0.0004f)
                {
                    CheckSkip();
                    flatPos = Vector3.MoveTowards(flatPos, target, walkSpeed * Time.deltaTime);
                    walkBlend = Mathf.MoveTowards(walkBlend, 1f, Time.deltaTime * 1.5f);
                    stepPhase += Time.deltaTime * stepsPerSecond * Mathf.PI;

                    Vector3 dir = target - flatPos;
                    dir.y = 0f;
                    if (dir.sqrMagnitude > 0.01f)
                    {
                        Quaternion look = Quaternion.LookRotation(dir.normalized, Vector3.up);
                        baseRot = Quaternion.Slerp(baseRot, look, Time.deltaTime * 2.5f);
                    }
                    ApplyWalkShake(ct, flatPos, baseRot, stepPhase, walkBlend);
                    yield return null;
                }
                if (skipEntrance) break;

                // 지점에서 멈추는 경우(줌인)엔 흔들림을 가라앉히고 멈춤
                if (i == zoomAtPoint)
                {
                    for (float t = 0f; t < 0.35f; t += Time.deltaTime)
                    {
                        walkBlend = Mathf.MoveTowards(walkBlend, 0f, Time.deltaTime / 0.35f);
                        ApplyWalkShake(ct, target, baseRot, stepPhase, walkBlend);
                        yield return null;
                    }
                    ct.position = target;
                    ct.rotation = baseRot;
                }

                // 성문 근처: 멀리 왕좌에 앉은 마왕을 줌인으로 보여 줌
                if (i == zoomAtPoint)
                {
                    yield return ThroneZoom(cam);
                    baseRot = ct.rotation;
                }
            }

            // 마지막 지점: 흔들림을 가라앉히며 멈춤
            Vector3 endPos = entrancePath[entrancePath.Length - 1] != null ? entrancePath[entrancePath.Length - 1].position : ct.position;
            for (float t = 0f; t < 0.4f && !skipEntrance; t += Time.deltaTime)
            {
                walkBlend = Mathf.MoveTowards(walkBlend, 0f, Time.deltaTime / 0.4f);
                ApplyWalkShake(ct, endPos, baseRot, stepPhase, walkBlend);
                yield return null;
            }
            ct.rotation = baseRot;

            Transform last = entrancePath[entrancePath.Length - 1];
            if (last != null) ct.position = last.position;
            cam.fieldOfView = baseFOV > 0f ? baseFOV : cam.fieldOfView;
            SetActiveAll(hideDuringZoom, true);

            // 마왕을 마주 봄
            yield return TurnCamera(ct, LookAtBoss(ct), 0.9f);
        }

        // 마왕이 일어나며 이름 + 체력바 등장
        yield return boss.DoStandUp();
        Notice(entranceNotice);
        if (hud != null) yield return hud.Reveal(boss.bossName);
        yield return new WaitForSeconds(0.4f);

        // 보스전 안내
        yield return ShowTutorial();

        // UI 등장 → 전투 시작
        yield return RestoreUI(uiFadeInTime);
    }

    // 걷는 흔들림: 발걸음마다 살짝 내려앉고(위아래), 두 걸음에 한 번 좌우로 흔들리며 고개가 기울어짐
    private void ApplyWalkShake(Transform ct, Vector3 pos, Quaternion baseRot, float phase, float blend)
    {
        float step = Mathf.Abs(Mathf.Sin(phase));       // 0 → 1 → 0 (한 걸음)
        float side = Mathf.Sin(phase * 0.5f);            // -1 ↔ 1 (두 걸음)
        Vector3 right = baseRot * Vector3.right;
        ct.position = pos
                    + Vector3.up * (step - 0.5f) * 2f * headBob * blend
                    + right * side * headSway * blend;
        ct.rotation = baseRot * Quaternion.Euler((0.5f - step) * 2f * headNod * blend, 0f, -side * headRoll * blend);
    }

    private bool skipEntrance;
    private float baseFOV;

    private void CheckSkip()
    {
        if (allowSkipEntrance && Input.GetKeyDown(KeyCode.Escape)) skipEntrance = true;
    }

    private Quaternion LookAtBoss(Transform ct)
    {
        Vector3 dir = boss.Enemy.AimPoint - ct.position;
        return dir.sqrMagnitude > 0.0001f ? Quaternion.LookRotation(dir.normalized, Vector3.up) : ct.rotation;
    }

    private IEnumerator TurnCamera(Transform ct, Quaternion to, float time)
    {
        Quaternion from = ct.rotation;
        for (float t = 0f; t < 1f; t += Time.deltaTime / Mathf.Max(0.01f, time))
        {
            ct.rotation = Quaternion.Slerp(from, to, Mathf.SmoothStep(0f, 1f, t));
            yield return null;
        }
        ct.rotation = to;
    }

    // 왕좌 줌인: 마왕 쪽으로 고개를 돌리고 시야각을 좁혀 당겨 봄 → 잠시 멈춤 → 원래 시점으로
    private IEnumerator ThroneZoom(Camera cam)
    {
        Transform ct = cam.transform;
        baseFOV = cam.fieldOfView;
        Quaternion walkRot = ct.rotation;
        Quaternion lookRot = LookAtBoss(ct);

        SetActiveAll(hideDuringZoom, false);
        Notice(zoomNotice);

        for (float t = 0f; t < 1f && !skipEntrance; t += Time.deltaTime / Mathf.Max(0.01f, zoomInTime))
        {
            CheckSkip();
            float k = Mathf.SmoothStep(0f, 1f, t);
            ct.rotation = Quaternion.Slerp(walkRot, lookRot, k);
            cam.fieldOfView = Mathf.Lerp(baseFOV, zoomFOV, k);
            yield return null;
        }
        // 살짝 더 당기면서 머묾
        float holdFrom = zoomFOV;
        for (float t = 0f; t < zoomHoldTime && !skipEntrance; t += Time.deltaTime)
        {
            CheckSkip();
            cam.fieldOfView = Mathf.Lerp(holdFrom, holdFrom * 0.85f, t / zoomHoldTime);
            yield return null;
        }
        float fovNow = cam.fieldOfView;
        Quaternion rotNow = ct.rotation;
        for (float t = 0f; t < 1f && !skipEntrance; t += Time.deltaTime / Mathf.Max(0.01f, zoomOutTime))
        {
            CheckSkip();
            float k = Mathf.SmoothStep(0f, 1f, t);
            ct.rotation = Quaternion.Slerp(rotNow, walkRot, k);
            cam.fieldOfView = Mathf.Lerp(fovNow, baseFOV, k);
            yield return null;
        }
        cam.fieldOfView = baseFOV;
        ct.rotation = walkRot;
        SetActiveAll(hideDuringZoom, true);
    }

    private static void SetActiveAll(GameObject[] objects, bool active)
    {
        if (objects == null) return;
        foreach (GameObject go in objects) if (go != null) go.SetActive(active);
    }

    // 보스전 규칙 안내 패널 (서서히 나타났다가 시간이 지나거나 클릭/Enter로 닫힘)
    private IEnumerator ShowTutorial()
    {
        if (tutorialPanel == null) yield break;
        if (tutorialText != null && !string.IsNullOrWhiteSpace(tutorialContent))
        {
            tutorialText.text = tutorialContent.Replace("30턴", maxTurns + "턴");
        }
        CanvasGroup g = tutorialPanel.GetComponent<CanvasGroup>();
        if (g == null) g = tutorialPanel.AddComponent<CanvasGroup>();
        g.alpha = 0f;
        tutorialPanel.SetActive(true);
        for (float t = 0f; t < 0.4f; t += Time.deltaTime) { g.alpha = t / 0.4f; yield return null; }
        g.alpha = 1f;

        yield return null;
        for (float t = 0f; t < tutorialTime; t += Time.deltaTime)
        {
            if (Input.GetMouseButtonDown(0) || Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter)) break;
            yield return null;
        }
        for (float t = 0f; t < 0.4f; t += Time.deltaTime) { g.alpha = 1f - t / 0.4f; yield return null; }
        tutorialPanel.SetActive(false);
    }

    // ── 입장 동안 UI 숨기기 (IntroSequence와 같은 방식: 꺼지지 않고 투명하게만) ──
    private class HiddenUI
    {
        public CanvasGroup group;
        public bool added;
        public float alpha;
        public bool interactable, blocksRaycasts;
    }
    private readonly List<HiddenUI> hiddenUI = new List<HiddenUI>();

    private void HideUI()
    {
        foreach (GameObject go in hideDuringIntro)
        {
            if (go == null) continue;
            CanvasGroup g = go.GetComponent<CanvasGroup>();
            bool added = g == null;
            if (added) g = go.AddComponent<CanvasGroup>();
            hiddenUI.Add(new HiddenUI { group = g, added = added, alpha = g.alpha, interactable = g.interactable, blocksRaycasts = g.blocksRaycasts });
            g.alpha = 0f;
            g.interactable = false;
            g.blocksRaycasts = false;
        }
    }

    private IEnumerator RestoreUI(float time)
    {
        for (float t = 0f; t < time; t += Time.deltaTime)
        {
            foreach (HiddenUI h in hiddenUI) if (h.group != null) h.group.alpha = h.alpha * (t / time);
            yield return null;
        }
        foreach (HiddenUI h in hiddenUI)
        {
            if (h.group == null) continue;
            h.group.alpha = h.alpha;
            h.group.interactable = h.interactable;
            h.group.blocksRaycasts = h.blocksRaycasts;
            if (h.added) Destroy(h.group);
        }
        hiddenUI.Clear();
    }

    // =========================================================
    // 내 턴
    // =========================================================
    private bool VoiceBusy =>
        (sender != null && (sender.IsRecording || sender.IsWaitingForResult)) ||
        (recorder != null && recorder.CastInProgress);

    private IEnumerator PlayerTurn()
    {
        castThisTurn = false;
        castTarget = null;
        Unblock();
        if (hud != null) hud.ShowBanner(playerTurnText, playerTurnColor);

        float total = boss.Phase >= 3 ? phase3TurnTime : playerTurnTime;
        float remaining = total;
        float grace = 0f;

        while (!castThisTurn && !PlayerDead && !BossDown)
        {
            float dt = Time.deltaTime;
            TickMinions(dt);
            remaining -= dt;
            if (remaining <= 0f)
            {
                remaining = 0f;
                // 말하는 중·판정 중·시전 연출 중이면 조금 더 기다려 줌
                if (VoiceBusy && grace < castGraceTime) grace += dt;
                else break;
            }
            if (hud != null) hud.SetTimer(remaining, total);
            yield return null;
        }

        if (hud != null) hud.SetTimer(-1f, 1f);
        Block(waitHint);

        if (!castThisTurn)
        {
            if (!PlayerDead && !BossDown) Notice(timeoutText);
            yield return new WaitForSeconds(1f);
            yield break;
        }

        // 마법이 맞을 때까지
        float wait = 0f;
        while (wait < 6f)
        {
            bool landed = castTarget == null || castTarget.IsDying || castTarget.PendingDamage <= 0f;
            if (landed) break;
            wait += Time.deltaTime;
            yield return null;
        }
        yield return new WaitForSeconds(afterHitDelay);
    }

    private void HandleSpellCast(SpellData spell, EnemyController target, float power)
    {
        if (castThisTurn) return;
        castThisTurn = true;
        castTarget = target;
        casts++;
        powerSum += power;
        Block(waitHint); // 한 턴에 한 번만
    }

    private void HandleBossDamaged(EnemyController e, SpellData spell, float damage, float multiplier)
    {
        bossHits++;
        bool weakness = multiplier > 1f;
        if (weakness)
        {
            weaknessHits++;
            weaknessStreak++;
            if (PlayerHealth.Instance != null) PlayerHealth.Instance.Heal(weaknessHitHeal, "상성 적중");

            if (boss.IsCharging)
            {
                boss.CancelCharge();
                Notice(chargeBreakNotice);
            }
            else if (stunOnWeaknessStreak && weaknessStreak >= stunStreak)
            {
                stunned = true;
                weaknessStreak = 0;
            }
        }
        else
        {
            weaknessStreak = 0;
        }
        RefreshHUD();
    }

    // =========================================================
    // 보스 턴
    // =========================================================
    private IEnumerator BossTurn()
    {
        if (hud != null) hud.ShowBanner(bossTurnText, bossTurnColor);
        yield return new WaitForSeconds(0.7f);

        int i = boss.Phase - 1;
        if (boss.IsCharging)
        {
            Notice(chargeReleaseNotice);
            yield return boss.ReleaseCharge();
        }
        else
        {
            float r = Random.value;
            float pDouble = At(doubleChance, i);
            float pCharge = At(chargeChance, i);
            if (r < pDouble)
            {
                yield return boss.DoDoubleAttack();
            }
            else if (r < pDouble + pCharge)
            {
                string weak = EnemyController.TryGetWeakness(boss.Element, out MagicElement w) ? EnemyController.ElementToKorean(w) : "";
                Notice(chargeNotice.Replace("{weak}", weak));
                yield return boss.BeginCharge();
            }
            else
            {
                yield return boss.DoBasicAttack();
            }
        }
        yield return new WaitForSeconds(0.6f);
    }

    private static float At(float[] arr, int i) => arr != null && i >= 0 && i < arr.Length ? arr[i] : 0f;
    private static int At(int[] arr, int i) => arr != null && i >= 0 && i < arr.Length ? arr[i] : 0;

    // =========================================================
    // 페이즈 전환
    // =========================================================
    private IEnumerator PhaseTransition(int newPhase)
    {
        MagicElement next = boss.phaseElements != null && boss.phaseElements.Length >= newPhase ? boss.phaseElements[newPhase - 1] : boss.Element;
        string hex = ColorUtility.ToHtmlStringRGB(BossController.ElementColorOf(next));
        Notice($"마왕이 <color=#{hex}>{EnemyController.ElementToKorean(next)}</color> 속성의 힘을 두른다!");
        StartCoroutine(ShakeCamera(0.9f, 0.08f));
        yield return boss.DoPowerUp(newPhase);
        RefreshHUD();

        if (PlayerHealth.Instance != null && phaseHealRatio > 0f)
        {
            float healed = PlayerHealth.Instance.Heal(PlayerHealth.Instance.maxHP * phaseHealRatio, "페이즈 전환");
            if (healed > 0f) Notice($"마왕이 힘을 모으는 사이 숨을 돌렸다! 체력 +{Mathf.CeilToInt(healed)}");
        }
        yield return new WaitForSeconds(1f);
    }

    // =========================================================
    // 하수인
    // =========================================================
    private IEnumerator MaybeSummon()
    {
        int i = boss.Phase - 1;
        minions.RemoveAll(x => x.enemy == null || x.enemy.IsDying);
        if (minionPrefabs == null || minionPrefabs.Length == 0 || minionPoints == null || minionPoints.Length == 0) yield break;
        if (minions.Count >= At(maxMinions, i) || Random.value >= At(summonChance, i)) yield break;

        yield return boss.DoSummon();

        Transform point = FreeMinionPoint();
        EnemyController prefab = minionPrefabs[Random.Range(0, minionPrefabs.Length)];
        if (point == null || prefab == null) yield break;

        Quaternion rot = point.rotation;
        Camera cam = Camera.main;
        if (cam != null)
        {
            Vector3 dir = cam.transform.position - point.position;
            dir.y = 0f;
            if (dir.sqrMagnitude > 0.01f) rot = Quaternion.LookRotation(dir);
        }
        if (minionSpawnEffect != null) EffectUtil.SpawnAndAutoDestroy(minionSpawnEffect, point.position, Quaternion.identity);

        EnemyController m = Instantiate(prefab, point.position, rot);
        m.canAttack = false;       // 공격은 아래 제한 시간으로
        m.targetPriority = 1;      // 마법이 하수인부터 노림
        m.OnDied += HandleMinionDied;
        minions.Add(new Minion { enemy = m, fuse = minionFuse });
        Notice(summonNotice);
        yield return new WaitForSeconds(0.8f);
    }

    private Transform FreeMinionPoint()
    {
        List<Transform> free = new List<Transform>();
        foreach (Transform p in minionPoints)
        {
            if (p == null) continue;
            bool taken = minions.Exists(m => m.enemy != null && (m.enemy.transform.position - p.position).sqrMagnitude < 1f);
            if (!taken) free.Add(p);
        }
        return free.Count > 0 ? free[Random.Range(0, free.Count)] : null;
    }

    private void TickMinions(float dt)
    {
        float soonest = -1f;
        for (int i = minions.Count - 1; i >= 0; i--)
        {
            Minion m = minions[i];
            if (m.enemy == null || m.enemy.IsDying) { minions.RemoveAt(i); continue; }
            if (m.enemy.IsDoomed) continue; // 이미 마법이 날아가는 중
            m.fuse -= dt;
            if (m.fuse <= 0f)
            {
                minions.RemoveAt(i);
                StartCoroutine(MinionStrike(m.enemy));
                continue;
            }
            if (soonest < 0f || m.fuse < soonest) soonest = m.fuse;
        }
        if (hud != null) hud.SetMinionTimer(soonest);
    }

    private IEnumerator MinionStrike(EnemyController m)
    {
        if (m == null) yield break;
        m.OnDied -= HandleMinionDied;
        Vector3 start = m.transform.position;
        Vector3 toCam = Camera.main != null ? Camera.main.transform.position - start : Vector3.zero;
        toCam.y = 0f;
        Vector3 lunge = start + toCam.normalized * 1.2f;

        for (float t = 0f; t < 0.12f && m != null; t += Time.deltaTime)
        {
            m.transform.position = Vector3.Lerp(start, lunge, t / 0.12f);
            yield return null;
        }
        if (PlayerHealth.Instance != null) PlayerHealth.Instance.TakeDamage(minionDamage, "하수인의 공격");

        // 공격 후 사라짐
        if (m == null) yield break;
        Vector3 scale = m.transform.localScale;
        foreach (Collider c in m.GetComponentsInChildren<Collider>()) c.enabled = false;
        for (float t = 0f; t < 0.35f && m != null; t += Time.deltaTime)
        {
            m.transform.localScale = Vector3.Lerp(scale, Vector3.zero, t / 0.35f);
            yield return null;
        }
        if (m != null) Destroy(m.gameObject);
    }

    private void HandleMinionDied(EnemyController m)
    {
        m.OnDied -= HandleMinionDied;
        minions.RemoveAll(x => x.enemy == m);
        if (PlayerHealth.Instance != null && minionKillHeal > 0f) PlayerHealth.Instance.Heal(minionKillHeal, "하수인 처치");
    }

    private void ClearMinions()
    {
        foreach (Minion m in minions)
        {
            if (m.enemy != null) Destroy(m.enemy.gameObject);
        }
        minions.Clear();
        if (hud != null) hud.SetMinionTimer(-1f);
    }

    // =========================================================
    // 승리 / 패배
    // =========================================================
    private IEnumerator Victory()
    {
        ended = true;
        Block("");
        ClearMinions();
        Notice(victoryNotice);
        StartCoroutine(boss.DoDeath());

        // 슬로모션
        Time.timeScale = 0.35f;
        yield return new WaitForSecondsRealtime(1.4f);
        Time.timeScale = 1f;
        yield return new WaitForSeconds(2f);

        StageProgress.MarkBossCleared();
        RecordResult(true);
        if (GameManager.Instance != null && NetworkManager.Instance != null && NetworkManager.Instance.HasSession)
        {
            GameManager.Instance.SetState(GameState.StageClear);
        }
        yield return FadeAndLoad();
    }

    private IEnumerator Defeat()
    {
        ended = true;
        Block("");
        ClearMinions();
        Notice(turnOutNotice);
        yield return new WaitForSeconds(2.5f);
        RecordResult(false);
        yield return FadeAndLoad();
    }

    // 플레이어가 쓰러짐 → 게임 오버 처리는 PlayerHealth가 함 (EndingScene). 여기선 결과만 채움
    private void HandlePlayerDied()
    {
        if (ended) return;
        ended = true;
        StopAllCoroutines();
        Block("");
        if (hud != null) { hud.SetTimer(-1f, 1f); hud.SetMinionTimer(-1f); }
        FillBossResult();
    }

    private void RecordResult(bool cleared)
    {
        GameResult.Record(cleared, Time.time - startTime);
        FillBossResult();
    }

    private void FillBossResult()
    {
        GameResult.IsBossFight = true;
        GameResult.BossTurnsUsed = turnsUsed;
        GameResult.BossMaxTurns = maxTurns;
        GameResult.BossWeaknessRate = bossHits > 0 ? (float)weaknessHits / bossHits : 0f;
        GameResult.BossHpLeftRatio = boss != null ? boss.HPRatio : 0f;
        GameResult.PlayerHpLeft = PlayerHealth.Instance != null ? PlayerHealth.Instance.CurrentHP : 0f;
        GameResult.BossGrade = Grade();
    }

    private string Grade()
    {
        float turnScore = 40f * Mathf.Clamp01(1f - (float)turnsUsed / Mathf.Max(1, maxTurns));
        float weakScore = 30f * (bossHits > 0 ? (float)weaknessHits / bossHits : 0f);
        float hpScore = 20f * (PlayerHealth.Instance != null ? PlayerHealth.Instance.Ratio : 0f);
        float avgPower = casts > 0 ? powerSum / casts : 1f;
        float voiceScore = 10f * Mathf.Clamp01((avgPower - 1f) / 0.5f);
        float score = turnScore + weakScore + hpScore + voiceScore;
        Debug.Log($"[보스전] 점수 {score:F1} (턴 {turnScore:F1} + 상성 {weakScore:F1} + 체력 {hpScore:F1} + 영창 {voiceScore:F1})");
        if (score >= gradeS) return "S";
        if (score >= gradeA) return "A";
        if (score >= gradeB) return "B";
        return "C";
    }

    private IEnumerator FadeAndLoad()
    {
        CanvasGroup fade = CreateFadeOverlay();
        for (float t = 0f; t < fadeOutTime; t += Time.unscaledDeltaTime)
        {
            fade.alpha = t / fadeOutTime;
            yield return null;
        }
        fade.alpha = 1f;
        Time.timeScale = 1f;
        Unblock();
        SceneManager.LoadScene(endingSceneName);
    }

    // =========================================================
    // 도우미
    // =========================================================
    private void RefreshHUD()
    {
        if (hud == null || boss == null) return;
        hud.SetHP(boss.HPRatio, boss.Enemy.CurrentHP, boss.maxHP);
        hud.SetElement(boss.Element, boss.CurrentColor);
        hud.SetTurnsLeft(maxTurns - turnsUsed);
    }

    private IEnumerator ShakeCamera(float duration, float strength)
    {
        Camera cam = Camera.main;
        if (cam == null) yield break;
        Transform t = cam.transform;
        Vector3 origin = t.position;
        for (float time = 0f; time < duration; time += Time.deltaTime)
        {
            float k = 1f - time / duration;
            t.position = origin + Random.insideUnitSphere * strength * k;
            yield return null;
        }
        t.position = origin;
    }

    private static CanvasGroup CreateFadeOverlay()
    {
        GameObject go = new GameObject("BossFadeOverlay", typeof(RectTransform), typeof(Canvas), typeof(CanvasGroup), typeof(Image));
        Canvas canvas = go.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 999;
        go.GetComponent<Image>().color = Color.black;
        CanvasGroup g = go.GetComponent<CanvasGroup>();
        g.alpha = 0f;
        return g;
    }
}