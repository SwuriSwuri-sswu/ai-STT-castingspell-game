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

    [Header("입장 중 지팡이 (WandCaster)")]
    [Tooltip("비우면 씬에서 자동으로 찾음")]
    public WandCaster wand;
    [Tooltip("줌인할 때 지팡이를 꺼 버리는 대신 화면 아래로 내렸다가 다시 올림")]
    public bool lowerWandDuringZoom = true;

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
    public string zoomNotice = "Someone is sitting on the throne...";

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
        "<size=120%>Turn-based battle!</size>\n" +
        "You can chant one spell on each of your turns.\n" +
        "The Demon Lord's element changes as his HP drops.\n" +
        "Watch the <color=#FF5555>color</color> of his HP bar!\n\n" +
        "Hitting him with a strong element heals you\n" +
        "and can break the magic he is charging.\n\n" +
        "If minions appear, defeat them first!\n" +
        "You must defeat the Demon Lord within <size=120%>30 turns</size>.\n\n" +
        "Good luck...";
    [Tooltip("안내를 보여 주는 시간 (클릭 / Enter로 바로 닫기)")]
    public float tutorialTime = 7f;
    public string entranceNotice = "The Demon Lord of Silence has appeared!";

    [Header("턴")]
    public float playerTurnTime = 10f;
    [Tooltip("페이즈 3의 내 턴 시간")]
    public float phase3TurnTime = 8f;
    public int maxTurns = 20;
    [Tooltip("시간이 끝나는 순간 녹음·판정·시전 연출 중이면 이만큼(초)까지 기다려 줌")]
    public float castGraceTime = 4f;
    [Tooltip("마법이 맞은 뒤 보스 턴까지")]
    public float afterHitDelay = 0.5f;
    [Tooltip("'마왕의 턴!' 배너 뒤 공격 시작까지 (초)")]
    public float bossTurnLead = 0.4f;
    [Tooltip("마왕의 공격이 끝난 뒤 다음 진행까지 (초)")]
    public float bossTurnTail = 0.3f;
    public string playerTurnText = "Your turn!";
    public string bossTurnText = "Demon Lord's turn!";
    public Color playerTurnColor = new Color(1f, 0.85f, 0.3f);
    public Color bossTurnColor = new Color(0.95f, 0.25f, 0.2f);
    [Tooltip("내 턴이 아닐 때 영창 자막 대신 보여 줄 안내 (비우면 빈칸)")]
    public string waitHint = "";
    [Tooltip("영창 자막 (Subtitle_Main). 마왕의 턴 동안, 그리고 '나의 턴!'이 떠 있는 동안 숨김")]
    public GameObject castPrompt;
    [Tooltip("'나의 턴!'을 보여 주는 시간 (초). 이 시간이 지나야 영창 자막이 나오고 주문을 외칠 수 있음 (제한 시간도 그 뒤부터)")]
    public float turnBannerHold = 1.2f;
    [Tooltip("영창 자막이 나타나는/사라지는 시간")]
    public float promptFadeTime = 0.25f;
    public string timeoutText = "Time's up!";

    [Header("보스 공격 패턴 (페이즈 1·2·3 확률)")]
    public float[] chargeChance = { 0f, 0.35f, 0.3f };
    public float[] doubleChance = { 0f, 0f, 0.3f };
    [Tooltip("{weak} = 약점 속성")]
    public string chargeNotice = "The Demon Lord is charging power! Break it with {weak} magic!";
    public string chargeBreakNotice = "You broke the Demon Lord's spell!";
    public string chargeReleaseNotice = "The charged magic explodes!";

    [Header("하수인 (페이즈 2부터)")]
    [Tooltip("하급 몹 프리팹들 (랜덤)")]
    public EnemyController[] minionPrefabs;
    [Tooltip("하수인이 나타날 위치들 (보스 옆)")]
    public Transform[] minionPoints;
    public float[] summonChance = { 0f, 0.25f, 0.35f };
    public int[] maxMinions = { 0, 1, 1 };
    [Tooltip("하수인이 살아 있는 동안은 새로 소환하지 않음")]
    public bool onlyWhenNoMinion = true;
    [Tooltip("한 번 소환한 뒤 최소 이만큼의 턴이 지나야 다시 소환")]
    public int summonCooldownTurns = 3;
    [Tooltip("페이즈마다 소환할 수 있는 최대 횟수 (0 = 제한 없음)")]
    public int maxSummonsPerPhase = 2;
    [Tooltip("이 시간(초, 내 턴 동안만 흐름) 안에 처치하지 못하면 공격")]
    public float minionFuse = 12f;
    public float minionDamage = 5f;
    [Tooltip("(선택) 하수인이 나타날 때 이펙트")]
    public GameObject minionSpawnEffect;

    [Header("하수인 소환 포탈")]
    [Tooltip("(선택) 포탈 이펙트 프리팹. 비우면 포탈 없이 바로 나타남")]
    public GameObject portalPrefab;
    [Tooltip("포탈이 하수인 자리에서 뒤로(플레이어 반대쪽) 떨어진 거리")]
    public float portalBehind = 0.6f;
    [Tooltip("포탈 높이 (하수인 자리 기준)")]
    public float portalHeight = 0.8f;
    [Tooltip("포탈 크기 (프리팹 크기에 곱함)")]
    public float portalScale = 1f;
    [Tooltip("포탈 회전 보정. 포탈은 기본으로 플레이어를 바라봄 → 누워 있으면 X 90 등으로 세움")]
    public Vector3 portalRotation = Vector3.zero;
    [Tooltip("포탈이 커지며 열리는 시간")]
    public float portalOpenTime = 0.4f;
    [Tooltip("포탈이 열린 뒤 하수인이 나오기까지")]
    public float portalLeadTime = 0.4f;
    [Tooltip("하수인이 포탈에서 걸어 나오는(커지며 앞으로 나오는) 시간")]
    public float minionEmergeTime = 0.5f;
    [Tooltip("하수인이 나온 뒤 포탈이 남아 있는 시간")]
    public float portalLinger = 2f;
    [Tooltip("포탈이 작아지며 사라지는 시간")]
    public float portalCloseTime = 0.6f;
    public string summonNotice = "A minion has been summoned! Defeat it first!";

    [Header("하수인 크기·위치")]
    [Tooltip("하수인 크기 (프리팹 크기에 곱함). 보스 맵이 커서 작아 보이면 키우기")]
    public float minionScale = 1f;
    [Tooltip("하수인을 Minion Point 아래의 실제 바닥(Collider 윗면)에 붙여 세우기")]
    public bool snapMinionToFloor = true;
    [Tooltip("포탈을 하수인 크기에 맞춰 자동으로 키움 (하수인 키 × 이 값 = 포탈 높이). 0 = Portal Scale만 사용")]
    public float portalFitMinion = 1.4f;

    [Header("페이즈 전환 연출 (속성이 바뀔 때)")]
    [Tooltip("카메라가 마왕 옆으로 옮겨 가 슬로 모션으로 파워업을 보여 줌")]
    public bool phaseCinematic = true;
    [Tooltip("(추천) 연출 시작 카메라 자리. 빈 오브젝트를 만들어 Scene 뷰에서 원하는 구도로 옮겨 두고 넣기\n" +
             "넣으면 아래 Offset 값은 무시하고, 이 자리에서 마왕을 바라보며 왼쪽으로 돎")]
    public Transform phaseCamStart;
    [Tooltip("켜면 아래 위치를 '마왕 키의 몇 배'로 계산 (맵·마왕 크기가 달라도 비슷한 구도). 끄면 m 단위")]
    public bool phaseCamByBossHeight = true;
    [Tooltip("시작 카메라 위치 (마왕 발 기준, 원래 카메라 방향으로 X 오른쪽 / Y 위 / Z 플레이어 쪽)\n" +
             "마왕 키 배수로 쓸 때 예: (1.2, 0.8, 2.2) / m 단위일 때 예: (5, 3, 9)")]
    public Vector3 phaseCamOffset = new Vector3(1.2f, 0.8f, 2.2f);
    [Tooltip("연출 동안 마왕 둘레를 도는 각도 (도)")]
    public float phaseOrbitAngle = 40f;
    [Tooltip("끄면 카메라가 (자기 화면 기준) 왼쪽으로, 켜면 오른쪽으로 돎")]
    public bool phaseOrbitToRight = false;
    [Tooltip("연출 동안 시야각 (작을수록 크게)")]
    public float phaseFOV = 50f;
    [Tooltip("슬로 모션 배속 (0.35 = 35% 속도)")]
    [Range(0.05f, 1f)] public float phaseSlowMotion = 0.35f;
    [Tooltip("카메라가 옆으로 옮겨 가는 시간 / 돌아오는 시간 (실제 초)")]
    public float phaseCamMoveTime = 0.5f;
    [Tooltip("파워업 동작이 끝난 뒤 그 자리에서 조금 더 보여 주는 시간 (실제 초)")]
    public float phaseHoldAfter = 0.4f;
    [Tooltip("연출 동안 UI 가리기 (Hide During Intro 목록 + 마왕 체력바). 알림 자막은 그대로 보임")]
    public bool hideUIDuringPhase = true;

    [Header("지팡이 끝 빛 (WandTipLight)")]
    [Tooltip("비우면 씬에서 자동으로 찾음. 입장 연출 동안 꺼 두고 전투 시작 때 켬")]
    public WandTipLight wandLight;

    [Header("회복")]
    [Range(0f, 1f)] public float phaseHealRatio = 0.3f;
    public float minionKillHeal = 5f;
    public float weaknessHitHeal = 3f;

    [Header("기절")]
    public bool stunOnWeaknessStreak = true;
    [Tooltip("상성 마법을 이만큼 연속으로 맞히면 보스가 기절해 다음 보스 턴을 쉼")]
    public int stunStreak = 2;
    [Tooltip("기절하는 순간 뜨는 자막")]
    public string stunNotice = "The Demon Lord staggers! He'll skip his next turn!";
    [Tooltip("기절해서 쉬는 마왕의 턴에 뜨는 자막")]
    public string stunSkipNotice = "The Demon Lord is still dazed...";
    [Tooltip("마왕이 이미 일어난 뒤 쉬는 턴에 뜨는 자막 (Boss의 Stun Auto Recover가 켜져 있을 때)")]
    public string stunRecoveredNotice = "The Demon Lord is catching his breath... Your chance!";
    [Tooltip("이미 일어난 뒤 쉬는 턴의 길이 (초)")]
    public float stunRecoveredTime = 1.5f;
    [Tooltip("쉬는 턴에 웅크리고 있는 시간 (그 뒤 일어남)")]
    public float stunSkipTime = 3.5f;

    [Header("결과")]
    public string endingSceneName = "EndingScene";
    public string victoryNotice = "You defeated the Demon Lord of Silence!";
    public string turnOutNotice = "Time has run out... The Demon Lord's silence engulfs the castle.";
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
    private Coroutine runRoutine;
    private int lastSummonTurn = -999;
    private int summonsThisPhase;
    private int summonPhase = 1;
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
        // 영창 자막을 숨겼다 켰다 할 CanvasGroup (입장 연출이 임시로 붙였다 지우는 것과 겹치지 않게 미리 붙여 둠)
        if (castPrompt != null)
        {
            promptGroup = castPrompt.GetComponent<CanvasGroup>();
            if (promptGroup == null) promptGroup = castPrompt.AddComponent<CanvasGroup>();
        }
        GameResult.ClearBoss();
        GameResult.IsBossFight = true;
        GameResult.BossMaxTurns = maxTurns;
        startTime = Time.time;

        boss.Enemy.OnDamaged += HandleBossDamaged;
        boss.Enemy.OnDied += HandleBossDied;   // 마지막 일격이 언제 들어가든 바로 승리
        if (wand == null) wand = FindFirstObjectByType<WandCaster>();
        if (wandLight == null) wandLight = FindFirstObjectByType<WandTipLight>();
        if (SkillManager.Instance != null) SkillManager.Instance.OnSpellCast += HandleSpellCast;
        if (PlayerHealth.Instance != null) PlayerHealth.Instance.OnDied += HandlePlayerDied;
        if (hud != null) hud.Hide();

        runRoutine = StartCoroutine(Run());
    }

    private void OnDisable()
    {
        if (slowMoActive) { Time.timeScale = 1f; slowMoActive = false; }
    }

    private void OnDestroy()
    {
        // 다른 씬에 영창 잠금이 남지 않게
        SkillManager.CastBlocked = false;
        SkillManager.BlockedHint = null;
        if (boss != null && boss.Enemy != null)
        {
            boss.Enemy.OnDamaged -= HandleBossDamaged;
            boss.Enemy.OnDied -= HandleBossDied;
        }
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
            bool skipSummon = false;
            if (targetPhase > boss.Phase)
            {
                // 기절 중이었으면 먼저 일어남
                if (stunned)
                {
                    stunned = false;
                    yield return boss.EndStun();
                }
                // 페이즈 전환이 보스 턴을 대신함 (숨 돌릴 틈)
                while (boss.Phase < targetPhase) yield return PhaseTransition(boss.Phase + 1);
            }
            else if (stunned)
            {
                // 기절: 이번 보스 턴은 쉼 (공격·하수인 소환 없음) → 일어남
                stunned = false;
                skipSummon = true;
                if (boss.IsStunned && boss.stunAutoRecover)
                {
                    // 아직 웅크려 있음 → 마왕이 정해진 시간(Stun Hold Time)만큼 멈춰 있다가 스스로 일어날 때까지 기다림
                    Notice(stunSkipNotice);
                    float safety = boss.stunHoldTime + 5f;
                    while ((boss.IsStunned || boss.IsRecovering) && safety > 0f && !BossDown)
                    {
                        safety -= Time.deltaTime;
                        yield return null;
                    }
                    if (boss.IsStunned) yield return boss.EndStun();
                    yield return new WaitForSeconds(0.3f);
                }
                else if (boss.IsStunned)
                {
                    // 아직 웅크려 있음 → 잠시 뒤 일어남
                    Notice(stunSkipNotice);
                    yield return new WaitForSeconds(stunSkipTime);
                    yield return boss.EndStun();
                }
                else
                {
                    // 이미 스스로 일어났음 (Stun Auto Recover) → 숨 고르며 턴을 쉼
                    Notice(stunRecoveredNotice);
                    yield return new WaitForSeconds(stunRecoveredTime);
                }
            }
            else
            {
                yield return BossTurn();
            }
            if (PlayerDead) yield break;

            if (!skipSummon) yield return MaybeSummon();
        }
    }

    // =========================================================
    // 입장
    // =========================================================
    private IEnumerator Entrance()
    {
        HideUI();
        if (tutorialPanel != null) tutorialPanel.SetActive(false);
        if (wandLight != null && playEntrance) wandLight.SetOn(false, 0f);   // 걸어가는 동안 지팡이 빛 꺼 둠

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
            SetZoomHidden(false);
            if (wand != null) wand.SetWalk(0f, 0f);

            // 마왕을 마주 봄
            yield return TurnCamera(ct, LookAtBoss(ct), 0.9f);
        }

        // 마왕이 일어나며 이름 + 체력바 등장 (지팡이를 치켜들며 전투 준비)
        if (wand != null) { wand.SetWalk(0f, 0f); wand.SetLowered(false); wand.PlayReady(); }
        yield return boss.DoStandUp();
        if (boss.HasBattlePoint) yield return DescendWithCamera();
        boss.ShowCircle(true);   // 발밑 마법진 (이미 켜져 있으면 그대로)
        Notice(entranceNotice);
        if (hud != null) yield return hud.Reveal(boss.bossName);
        yield return new WaitForSeconds(0.4f);

        // 보스전 안내
        yield return ShowTutorial();

        // UI 등장 → 전투 시작 (지팡이 빛도 켜짐)
        if (wandLight != null) wandLight.SetOn(true, 0.6f);
        yield return RestoreUI(uiFadeInTime);
    }

    // 걷는 흔들림: 발걸음마다 살짝 내려앉고(위아래), 두 걸음에 한 번 좌우로 흔들리며 고개가 기울어짐
    private void ApplyWalkShake(Transform ct, Vector3 pos, Quaternion baseRot, float phase, float blend)
    {
        if (wand != null) wand.SetWalk(blend, phase);   // 지팡이도 발걸음에 맞춰 출렁임
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

    // 마왕이 왕좌에서 내려오는 동안 카메라가 따라 봄
    private IEnumerator DescendWithCamera()
    {
        Transform ct = Camera.main != null ? Camera.main.transform : null;
        bool done = false;
        StartCoroutine(RunThen(boss.DoDescend(), () => done = true));
        while (!done)
        {
            if (ct != null) ct.rotation = Quaternion.Slerp(ct.rotation, LookAtBoss(ct), 1f - Mathf.Exp(-5f * Time.deltaTime));
            yield return null;
        }
        if (ct != null) yield return TurnCamera(ct, LookAtBoss(ct), 0.35f);
    }

    private static IEnumerator RunThen(IEnumerator routine, System.Action then)
    {
        yield return routine;
        then();
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

        SetZoomHidden(true);
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
        SetZoomHidden(false);
    }

    // 줌인할 때 숨길 것: 지팡이는 내렸다 올리고(lowerWandDuringZoom), 나머지는 끄고 켬
    private void SetZoomHidden(bool hidden)
    {
        if (hideDuringZoom == null) return;
        foreach (GameObject go in hideDuringZoom)
        {
            if (go == null) continue;
            WandCaster w = lowerWandDuringZoom ? go.GetComponentInChildren<WandCaster>(true) : null;
            if (w != null)
            {
                go.SetActive(true);
                w.SetLowered(hidden);
            }
            else go.SetActive(!hidden);
        }
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
            tutorialText.text = tutorialContent.Replace("30 turns", maxTurns + " turns");
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
            foreach (HiddenUI h in hiddenUI) if (h.group != null && h.group != promptGroup) h.group.alpha = h.alpha * (t / time);
            yield return null;
        }
        foreach (HiddenUI h in hiddenUI)
        {
            if (h.group == null) continue;
            h.group.alpha = h.group == promptGroup ? 0f : h.alpha; // 영창 자막은 '나의 턴!'이 끝난 뒤에 나타남
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
        float total = boss.Phase >= 3 ? phase3TurnTime : playerTurnTime;

        // '나의 턴!' 배너가 떠 있는 동안은 영창 자막을 숨기고 주문도 막아 둠 (안내 문구는 빈칸)
        Block("");
        SetPrompt(false, true);
        if (hud != null)
        {
            hud.ShowBanner(playerTurnText, playerTurnColor);
            hud.SetTimer(total, total);
        }
        if (turnBannerHold > 0f) yield return new WaitForSeconds(turnBannerHold);
        if (PlayerDead || BossDown) yield break;

        // 배너가 끝나면 영창 자막이 나타나고 주문을 외칠 수 있음
        Unblock();
        SetPrompt(true);

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
            SetPrompt(false);
            if (!PlayerDead && !BossDown) Notice(timeoutText);
            yield return new WaitForSeconds(1f);
            yield break;
        }

        // 마법이 맞을 때까지
        float wait = 0f;
        while (wait < 6f)
        {
            bool landed = (castTarget == null || castTarget.IsDying || castTarget.PendingDamage <= 0f)
                          && (boss.Enemy.IsDying || boss.Enemy.PendingDamage <= 0f);   // 장판·차지형처럼 늦게 들어가는 데미지까지
            if (BossDown) break;
            if (landed) break;
            wait += Time.deltaTime;
            yield return null;
        }
        yield return new WaitForSeconds(afterHitDelay);
        SetPrompt(false); // 마왕의 턴 동안 숨김
    }

    // 영창 자막 보이기 / 숨기기 (꺼 버리지 않고 투명하게만 → 자막 스크립트는 계속 동작)
    private CanvasGroup promptGroup;
    private Coroutine promptRoutine;

    private void SetPrompt(bool show, bool instant = false)
    {
        if (promptGroup == null) return;
        if (promptRoutine != null) StopCoroutine(promptRoutine);
        promptRoutine = null;
        float target = show ? 1f : 0f;
        if (instant || promptFadeTime <= 0f) promptGroup.alpha = target;
        else promptRoutine = StartCoroutine(FadePrompt(target));
    }

    private IEnumerator FadePrompt(float target)
    {
        float from = promptGroup.alpha;
        for (float t = 0f; t < promptFadeTime; t += Time.deltaTime)
        {
            promptGroup.alpha = Mathf.Lerp(from, target, t / promptFadeTime);
            yield return null;
        }
        promptGroup.alpha = target;
        promptRoutine = null;
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
            if (PlayerHealth.Instance != null) PlayerHealth.Instance.Heal(weaknessHitHeal, "Weakness hit");

            if (boss.IsCharging)
            {
                boss.CancelCharge();
                Notice(chargeBreakNotice);
            }
            else if (stunOnWeaknessStreak && weaknessStreak >= stunStreak && !BossDown && !stunned)
            {
                stunned = true;
                weaknessStreak = 0;
                boss.BeginStun();   // Hit_Big → 웅크림 + 머리 위 별
                Notice(stunNotice);
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
        yield return new WaitForSeconds(bossTurnLead);

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
                string weak = EnemyController.TryGetWeakness(boss.Element, out MagicElement w) ? EnemyController.ElementName(w) : "";
                Notice(chargeNotice.Replace("{weak}", weak));
                yield return boss.BeginCharge();
            }
            else
            {
                yield return boss.DoBasicAttack();
            }
        }
        yield return new WaitForSeconds(bossTurnTail);
    }

    private static float At(float[] arr, int i) => arr != null && i >= 0 && i < arr.Length ? arr[i] : 0f;
    private static int At(int[] arr, int i) => arr != null && i >= 0 && i < arr.Length ? arr[i] : 0;

    // =========================================================
    // 페이즈 전환
    // =========================================================
    private IEnumerator PhaseTransition(int newPhase)
    {
        yield return boss.WaitHitSettled();   // 방금 맞고 휘청이는 중이면 자세가 돌아온 뒤 연출 시작
        MagicElement next = boss.phaseElements != null && boss.phaseElements.Length >= newPhase ? boss.phaseElements[newPhase - 1] : boss.Element;
        string hex = ColorUtility.ToHtmlStringRGB(BossController.ElementColorOf(next));
        Notice($"The Demon Lord takes on the power of <color=#{hex}>{EnemyController.ElementName(next)}</color>!");
        if (phaseCinematic && Camera.main != null)
        {
            yield return PhaseCinematic(newPhase);
        }
        else
        {
            StartCoroutine(ShakeCamera(0.9f, 0.08f));
            yield return boss.DoPowerUp(newPhase);
        }
        RefreshHUD();

        if (PlayerHealth.Instance != null && phaseHealRatio > 0f)
        {
            float healed = PlayerHealth.Instance.Heal(PlayerHealth.Instance.maxHP * phaseHealRatio, "Phase change");
            if (healed > 0f) Notice($"You caught your breath while he powered up! HP +{Mathf.CeilToInt(healed)}");
        }
        yield return new WaitForSeconds(1f);
    }

    private bool slowMoActive;

    // 카메라를 마왕 오른쪽 위로 옮김 → 슬로 모션 파워업을 보며 왼쪽으로 천천히 돎 (시선은 마왕 고정) → 원래 자리로
    private IEnumerator PhaseCinematic(int newPhase)
    {
        Camera cam = Camera.main;
        Transform ct = cam.transform;
        Vector3 savedPos = ct.position;
        Quaternion savedRot = ct.rotation;
        float savedFOV = cam.fieldOfView;
        SetZoomHidden(true);   // 지팡이 내리기 (줌인 때와 같은 처리)
        if (hideUIDuringPhase) StartCoroutine(FadeOutUI(phaseCamMoveTime, true));

        // 기준 방향: 원래 카메라의 오른쪽 / 마왕 → 플레이어 방향
        Vector3 feet = boss.transform.position;
        Vector3 toPlayer = savedPos - feet; toPlayer.y = 0f;
        toPlayer = toPlayer.sqrMagnitude > 0.01f ? toPlayer.normalized : boss.transform.forward;
        Vector3 right = Vector3.Cross(toPlayer, Vector3.up);   // 플레이어가 마왕을 볼 때의 오른쪽
        Vector3 startOffset;
        if (phaseCamStart != null)
        {
            startOffset = phaseCamStart.position - feet;   // Scene 뷰에서 잡아 둔 자리 그대로
        }
        else
        {
            Vector3 o = phaseCamByBossHeight ? phaseCamOffset * boss.BodyHeight : phaseCamOffset;
            startOffset = right * o.x + Vector3.up * o.y + toPlayer * o.z;
        }
        // 도는 방향 = 연출 카메라 화면 기준 왼쪽 (카메라가 자기 왼쪽으로 옮겨 가며 마왕을 계속 바라봄)
        Vector3 startPos = feet + startOffset;
        Vector3 look = boss.Enemy.AimPoint - startPos; look.y = 0f;
        Vector3 camRight = look.sqrMagnitude > 0.001f ? Vector3.Cross(Vector3.up, look.normalized) : right;
        Vector3 testA = Quaternion.AngleAxis(10f, Vector3.up) * startOffset;
        float sign = Vector3.Dot(testA - startOffset, camRight) < 0f ? 1f : -1f;
        if (phaseOrbitToRight) sign = -sign;

        System.Func<Vector3> Look = () => boss.Enemy.AimPoint;
        System.Func<float, Vector3> OrbitPos = k => feet + Quaternion.AngleAxis(sign * phaseOrbitAngle * k, Vector3.up) * startOffset;

        // 1. 옆으로 이동
        for (float t = 0f; t < phaseCamMoveTime; t += Time.unscaledDeltaTime)
        {
            float k = Mathf.SmoothStep(0f, 1f, t / phaseCamMoveTime);
            ct.position = Vector3.Lerp(savedPos, OrbitPos(0f), k);
            ct.rotation = Quaternion.Slerp(savedRot, Quaternion.LookRotation(Look() - ct.position), k);
            cam.fieldOfView = Mathf.Lerp(savedFOV, phaseFOV, k);
            yield return null;
        }

        // 2. 슬로 모션 파워업 + 천천히 왼쪽으로
        Time.timeScale = phaseSlowMotion;
        slowMoActive = true;
        bool done = false;
        StartCoroutine(RunThen(boss.DoPowerUp(newPhase), () => done = true));
        float orbitTime = 0f;
        float expected = 2.4f / Mathf.Max(0.05f, phaseSlowMotion) * 0.5f;   // 대략적인 연출 길이 (실제 초)
        float holdLeft = phaseHoldAfter;
        while (!done || holdLeft > 0f)
        {
            float dt = Time.unscaledDeltaTime;
            orbitTime += dt;
            if (done) holdLeft -= dt;
            float k = Mathf.Clamp01(orbitTime / Mathf.Max(0.5f, expected + phaseHoldAfter));
            ct.position = OrbitPos(Mathf.SmoothStep(0f, 1f, k));
            ct.rotation = Quaternion.LookRotation(Look() - ct.position);
            yield return null;
        }
        Time.timeScale = 1f;
        slowMoActive = false;

        // 3. 원래 자리로
        Vector3 fromPos = ct.position;
        Quaternion fromRot = ct.rotation;
        float fromFOV = cam.fieldOfView;
        for (float t = 0f; t < phaseCamMoveTime; t += Time.unscaledDeltaTime)
        {
            float k = Mathf.SmoothStep(0f, 1f, t / phaseCamMoveTime);
            ct.position = Vector3.Lerp(fromPos, savedPos, k);
            ct.rotation = Quaternion.Slerp(fromRot, savedRot, k);
            cam.fieldOfView = Mathf.Lerp(fromFOV, savedFOV, k);
            yield return null;
        }
        ct.position = savedPos;
        ct.rotation = savedRot;
        cam.fieldOfView = savedFOV;
        SetZoomHidden(false);
        if (hideUIDuringPhase) yield return RestoreUI(0.4f);
    }

    // UI를 서서히 투명하게 (되돌리기는 RestoreUI)
    private IEnumerator FadeOutUI(float time, bool includeHud)
    {
        List<GameObject> list = new List<GameObject>(hideDuringIntro);
        if (includeHud && hud != null) list.Add(hud.gameObject);
        List<HiddenUI> added = new List<HiddenUI>();
        foreach (GameObject go in list)
        {
            if (go == null) continue;
            CanvasGroup g = go.GetComponent<CanvasGroup>();
            bool isNew = g == null;
            if (isNew) g = go.AddComponent<CanvasGroup>();
            if (hiddenUI.Exists(h => h.group == g)) continue;
            HiddenUI entry = new HiddenUI { group = g, added = isNew, alpha = g.alpha, interactable = g.interactable, blocksRaycasts = g.blocksRaycasts };
            hiddenUI.Add(entry);
            added.Add(entry);
            g.interactable = false;
            g.blocksRaycasts = false;
        }
        for (float t = 0f; t < time; t += Time.unscaledDeltaTime)
        {
            foreach (HiddenUI h in added) if (h.group != null) h.group.alpha = h.alpha * (1f - t / time);
            yield return null;
        }
        foreach (HiddenUI h in added) if (h.group != null) h.group.alpha = 0f;
    }

    // =========================================================
    // 하수인
    // =========================================================
    private IEnumerator MaybeSummon()
    {
        int i = boss.Phase - 1;
        minions.RemoveAll(x => x.enemy == null || x.enemy.IsDying);
        if (minionPrefabs == null || minionPrefabs.Length == 0 || minionPoints == null || minionPoints.Length == 0) yield break;
        if (boss.Phase != summonPhase) { summonPhase = boss.Phase; summonsThisPhase = 0; }
        if (onlyWhenNoMinion && minions.Count > 0) yield break;
        if (turnsUsed - lastSummonTurn < summonCooldownTurns) yield break;
        if (maxSummonsPerPhase > 0 && summonsThisPhase >= maxSummonsPerPhase) yield break;
        if (minions.Count >= At(maxMinions, i) || Random.value >= At(summonChance, i)) yield break;
        lastSummonTurn = turnsUsed;
        summonsThisPhase++;
        yield return SummonMinion();
    }

    // 플레이 중 Inspector의 BossBattleDirector 오른쪽 위 ⋮ 메뉴 → 이 항목으로 바로 소환해 크기·위치 확인
    [ContextMenu("테스트: 하수인 바로 소환 (플레이 중)")]
    private void TestSummon()
    {
        if (!Application.isPlaying) { Debug.Log("[BossBattleDirector] 플레이 중에만 소환할 수 있습니다."); return; }
        StartCoroutine(SummonMinion());
    }

    [ContextMenu("테스트: 페이즈 전환 연출 (플레이 중)")]
    private void TestPhase()
    {
        if (!Application.isPlaying || boss == null) return;
        StartCoroutine(PhaseCinematic(Mathf.Min(3, boss.Phase + 1)));
    }

    private IEnumerator SummonMinion()
    {
        yield return boss.DoSummon();

        Transform point = FreeMinionPoint();
        EnemyController prefab = minionPrefabs[Random.Range(0, minionPrefabs.Length)];
        if (point == null || prefab == null) yield break;
        Vector3 spot = MinionSpot(point.position, prefab);

        Quaternion rot = point.rotation;
        Camera cam = Camera.main;
        if (cam != null)
        {
            Vector3 dir = cam.transform.position - spot;
            dir.y = 0f;
            if (dir.sqrMagnitude > 0.01f) rot = Quaternion.LookRotation(dir);
        }
        // 포탈: 하수인 자리 뒤쪽에 열림
        Vector3 back = cam != null ? spot - cam.transform.position : -point.forward;
        back.y = 0f;
        back = back.sqrMagnitude > 0.01f ? back.normalized : Vector3.back;
        float fit = PortalFit(prefab);
        Vector3 portalPos = spot + back * portalBehind + Vector3.up * portalHeight * fit;
        GameObject portal = null;
        if (portalPrefab != null)
        {
            Quaternion portalRot = Quaternion.LookRotation(-back) * Quaternion.Euler(portalRotation);
            portal = Instantiate(portalPrefab, portalPos, portalRot);
            // 파티클도 오브젝트 크기를 따라 작아지게
            foreach (ParticleSystem ps in portal.GetComponentsInChildren<ParticleSystem>())
            {
                ParticleSystem.MainModule main = ps.main;
                main.scalingMode = ParticleSystemScalingMode.Hierarchy;
            }
            Vector3 full = portal.transform.localScale * portalScale * fit;
            portal.transform.localScale = Vector3.zero;
            yield return ScaleOver(portal.transform, Vector3.zero, full, portalOpenTime, true);
            if (portalLeadTime > 0f) yield return new WaitForSeconds(portalLeadTime);
        }

        if (minionSpawnEffect != null) EffectUtil.SpawnAndAutoDestroy(minionSpawnEffect, spot, Quaternion.identity);

        Vector3 emergeFrom = portal != null ? new Vector3(portalPos.x, spot.y, portalPos.z) : spot;
        EnemyController m = Instantiate(prefab, emergeFrom, rot);
        if (!Mathf.Approximately(minionScale, 1f)) m.transform.localScale *= minionScale;
        Vector3 lift = Vector3.up * m.hoverHeight;   // 공중 몹이 생성될 때 띄운 높이 (걸어 나오는 동안에도 유지)
        m.canAttack = false;       // 공격은 아래 제한 시간으로
        m.targetPriority = 1;      // 마법이 하수인부터 노림
        m.showKillText = false;    // 처치 자막 자리(Subtitle_Kill)는 '하수인 공격까지 n초'가 씀
        m.OnDied += HandleMinionDied;
        minions.Add(new Minion { enemy = m, fuse = minionFuse });

        // 포탈에서 커지며 앞으로 걸어 나옴
        if (portal != null && minionEmergeTime > 0f)
        {
            Vector3 fullScale = m.transform.localScale;
            for (float t = 0f; t < minionEmergeTime && m != null; t += Time.deltaTime)
            {
                float k = Mathf.SmoothStep(0f, 1f, t / minionEmergeTime);
                m.transform.position = Vector3.Lerp(emergeFrom, spot, k) + lift;
                m.transform.localScale = fullScale * Mathf.Lerp(0.3f, 1f, k);
                yield return null;
            }
            if (m != null)
            {
                m.transform.position = spot + lift;
                m.transform.localScale = fullScale;
            }
        }
        Notice(summonNotice);

        // 포탈은 잠시 뒤 작아지며 닫힘 (그동안 다음 진행은 계속)
        if (portal != null) StartCoroutine(ClosePortal(portal));
        yield return new WaitForSeconds(0.8f);
    }

    // 하수인이 설 자리: Minion Point 아래 실제 바닥 윗면 (마왕·다른 하수인 Collider는 무시)
    private Vector3 MinionSpot(Vector3 point, EnemyController prefab)
    {
        if (!snapMinionToFloor) return point;
        RaycastHit[] hits = Physics.RaycastAll(point + Vector3.up * 3f, Vector3.down, 12f, ~0, QueryTriggerInteraction.Ignore);
        float best = float.MaxValue;
        Vector3 floor = point;
        foreach (RaycastHit h in hits)
        {
            if (h.collider.GetComponentInParent<EnemyController>() != null) continue;
            if (h.distance < best) { best = h.distance; floor = h.point; }
        }
        return best == float.MaxValue ? point : floor;   // 공중에 뜨는 몹은 생성될 때 EnemyController가 Hover Height만큼 띄움
    }

    // 포탈 크기 배율: 하수인 키에 맞춤 (Portal Fit Minion이 0이면 1)
    private float PortalFit(EnemyController prefab)
    {
        if (portalFitMinion <= 0f || prefab == null || portalPrefab == null) return 1f;
        float minionH = RendererHeight(prefab.gameObject) * minionScale;
        float portalH = RendererHeight(portalPrefab);
        if (minionH <= 0.01f || portalH <= 0.01f) return 1f;
        return Mathf.Max(1f, minionH * portalFitMinion / (portalH * portalScale));
    }

    // 프리팹을 꺼내지 않고 메시 크기로 키를 잼 (파티클만 있는 포탈이면 파티클 크기로 대략)
    private static float RendererHeight(GameObject go)
    {
        float h = 0f;
        foreach (MeshFilter mf in go.GetComponentsInChildren<MeshFilter>(true))
            if (mf.sharedMesh != null) h = Mathf.Max(h, mf.sharedMesh.bounds.size.y * Mathf.Abs(mf.transform.lossyScale.y));
        foreach (SkinnedMeshRenderer smr in go.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            if (smr.sharedMesh != null) h = Mathf.Max(h, smr.sharedMesh.bounds.size.y * Mathf.Abs(smr.transform.lossyScale.y));
        if (h <= 0f)
        {
            foreach (ParticleSystem ps in go.GetComponentsInChildren<ParticleSystem>(true))
                h = Mathf.Max(h, ps.main.startSize.constantMax * Mathf.Abs(ps.transform.lossyScale.y));
        }
        return h;
    }

    private IEnumerator ClosePortal(GameObject portal)
    {
        if (portalLinger > 0f) yield return new WaitForSeconds(portalLinger);
        if (portal == null) yield break;
        yield return ScaleOver(portal.transform, portal.transform.localScale, Vector3.zero, portalCloseTime, false);
        if (portal != null)
        {
            float tail = EffectUtil.StopEmitting(portal);
            Destroy(portal, tail + 0.1f);
        }
    }

    // 크기 변화 (열릴 때는 살짝 커졌다가 자리 잡음 / 닫힐 때는 끝으로 갈수록 빠르게)
    private static IEnumerator ScaleOver(Transform target, Vector3 from, Vector3 to, float time, bool overshoot)
    {
        for (float t = 0f; t < time && target != null; t += Time.deltaTime)
        {
            float k = Mathf.Clamp01(t / Mathf.Max(0.01f, time));
            float e = overshoot ? 1f + 2.2f * Mathf.Pow(k - 1f, 3f) + 1.2f * Mathf.Pow(k - 1f, 2f) : k * k;
            target.localScale = Vector3.LerpUnclamped(from, to, e);
            yield return null;
        }
        if (target != null) target.localScale = to;
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
        if (PlayerHealth.Instance != null) PlayerHealth.Instance.TakeDamage(minionDamage, "Minion attack");

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
        if (PlayerHealth.Instance != null && minionKillHeal > 0f) PlayerHealth.Instance.Heal(minionKillHeal, "Minion defeated");
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
    // 마왕이 쓰러지는 순간 (내 턴 도중이든, 늦게 들어간 데미지로 마왕의 턴 도중이든) 바로 승리
    private void HandleBossDied(EnemyController e)
    {
        if (ended || PlayerDead) return;
        if (runRoutine != null) StopCoroutine(runRoutine);
        runRoutine = null;
        StartCoroutine(Victory());
    }

    private IEnumerator Victory()
    {
        if (ended) yield break;
        ended = true;
        Block("");
        SetPrompt(false);
        if (hud != null) { hud.SetTimer(-1f, 1f); hud.SetMinionTimer(-1f); }
        foreach (BossSpell sp in FindObjectsByType<BossSpell>(FindObjectsSortMode.None)) sp.Cancel();   // 날아오던 마왕 마법 지우기
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