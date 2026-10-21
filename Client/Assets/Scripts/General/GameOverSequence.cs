using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using UnityEngine.SceneManagement;

// 게임 오버 연출 (GameScene / BossScene에 하나씩. 빈 오브젝트에 붙이기)
//   쓰러지는 순간: 슬로 모션 → 지팡이 숨김 → 그 자리에 여우가 나타나 쓰러짐(Fall) → 카메라가 뒤·위로 빠져 3인칭
//   → 반투명 검은 화면 → GAME OVER / DEFEAT → 결과 패널 (버튼은 패널 안에 직접 넣고 OnClickRetry / OnClickTitle 연결)
// PlayerHealth(체력 0)와 BossBattleDirector(턴 초과)가 이 연출을 부름. 씬에 없으면 예전처럼 EndingScene으로 넘어감
// 테스트: 플레이 중 F9 키 또는 Inspector ⋮ 메뉴 → "테스트: 게임 오버"
public class GameOverSequence : MonoBehaviour
{
    public static GameOverSequence Instance { get; private set; }

    public enum Kind { GameOver, BossDefeat, TimeOver }

    [Header("제목")]
    public string gameOverTitle = "GAME OVER";
    [Tooltip("보스전에서 체력이 0이 됐을 때")]
    public string bossDefeatTitle = "DEFEAT";
    [Tooltip("보스전에서 턴을 다 써 버렸을 때")]
    public string timeOverTitle = "TIME OVER";
    public Color titleColor = new Color(0.85f, 0.15f, 0.15f);

    [Header("UI 연결 (Canvas 맨 아래에 두어야 다른 UI 위에 보임)")]
    [Tooltip("화면 전체를 덮는 검은 Image (CanvasGroup 자동 추가). 비우면 코드로 만듦")]
    public GameObject darkOverlay;
    [Range(0f, 1f)] public float overlayAlpha = 0.65f;
    public TMP_Text titleText;
    [Tooltip("결과 패널 (안에 결과 글자 + Retry / Title 버튼)")]
    public GameObject resultPanel;
    public TMP_Text resultText;
    [Tooltip("게임 오버가 시작되면 숨길 UI (체력바·버튼·자막 등)")]
    public GameObject[] hideUI;

    [Header("쓰러지는 주인공 (3인칭)")]
    [Tooltip("여우 모델 (fox.glb 또는 그 프리팹). 비우면 카메라만 뒤로 빠짐")]
    public GameObject playerModel;
    [Tooltip("쓰러지는 동작 (Fall.anim)")]
    public AnimationClip fallClip;
    [Tooltip("동작 클립이 기준으로 삼는 뼈대 이름 (Fall.anim은 'IdleDemo'). 이 오브젝트의 부모에 Animator를 붙여 재생")]
    public string clipRootName = "IdleDemo";
    [Tooltip("여우 키를 이 높이(m)에 자동으로 맞춤 (모델 원래 크기가 제각각이라). 0 = 자동 맞춤 안 함 (Model Scale만)")]
    public float modelHeightMeters = 1.1f;
    [Tooltip("자동 맞춤 뒤 추가 배율")]
    public float modelScale = 1f;
    [Tooltip("모델 방향 보정 (도). 0 = 카메라가 보던 방향(몹 쪽)을 바라봄")]
    public float modelYaw = 0f;
    [Tooltip("모델 높이 보정 (바닥에 묻히거나 뜨면)")]
    public float modelHeight = 0f;
    [Header("우당탕 넘어지기 (코드 연출, Fall 동작 위에 더해짐)")]
    public bool comicFall = true;
    public enum FallDir { Back, Forward, Left, Right }
    [Tooltip("넘어지는 방향")]
    public FallDir fallDirection = FallDir.Back;
    [Tooltip("넘어지기 전 펄쩍 뛰는 높이 (키의 배수)")]
    public float hopHeight = 0.45f;
    [Tooltip("뛰는 동안 제자리에서 도는 바퀴 수")]
    public float spinTurns = 1f;
    [Tooltip("바닥에 닿은 뒤 튕기는 횟수")]
    public int bounces = 2;
    [Tooltip("바닥에 닿을 때 찌그러지는 정도 (0.3 = 30%)")]
    [Range(0f, 0.6f)] public float squash = 0.3f;
    [Tooltip("(선택) 바닥에 닿는 순간 이펙트 (먼지·별 등)")]
    public GameObject landEffect;
    public float landEffectScale = 1f;
    [Tooltip("바닥에 닿을 때 화면 흔들림 세기 (0 = 없음)")]
    public float landShake = 1.2f;
    [Tooltip("Fall 동작 재생 (끄면 코드 연출만)")]
    public bool playFallClip = true;
    [Tooltip("숨길 1인칭 오브젝트 (지팡이 WandRig 등). 비우면 WandCaster를 찾아 숨김")]
    public GameObject[] hideFirstPerson;

    [Header("자리 직접 지정 (추천)")]
    [Tooltip("여우가 나타날 자리 (빈 오브젝트). 파란 화살표(앞)가 여우가 바라볼 방향. 비우면 플레이어 발밑")]
    public Transform modelSpawnPoint;
    [Tooltip("카메라가 옮겨 갈 자리 (빈 오브젝트). 넣으면 아래 Cam 값 대신 이 자리에서 여우를 바라봄")]
    public Transform cameraPoint;

    [Header("카메라 (주인공 기준, m) - Camera Point가 비어 있을 때")]
    [Tooltip("켜면 여우 앞쪽에서 얼굴을 비춤 / 끄면 뒤에서")]
    public bool cameraInFront = true;
    [Tooltip("뒤로 빠지는 거리")]
    public float camBack = 3.2f;
    [Tooltip("위로 올라가는 높이")]
    public float camUp = 2.2f;
    [Tooltip("옆으로 비켜서는 거리 (+ 오른쪽)")]
    public float camSide = 1.2f;
    [Tooltip("바라보는 높이 (주인공 발 기준)")]
    public float lookHeight = 0.6f;
    [Tooltip("카메라가 뒤로 빠지는 시간 (실제 초)")]
    public float camMoveTime = 0.9f;
    [Tooltip("여우가 서 있다가 넘어지기 시작하는 시점 (카메라가 빠지기 시작한 뒤, 실제 초). 카메라가 거의 다 빠진 뒤 넘어지게")]
    public float fallStartDelay = 0.6f;

    [Header("시간")]
    [Range(0.05f, 1f)] public float slowMotion = 0.35f;
    [Tooltip("슬로 모션 시간 (실제 초)")]
    public float slowMotionTime = 1.2f;
    [Tooltip("카메라가 빠지기 시작한 뒤 검은 화면이 깔리기까지 (실제 초)")]
    public float overlayDelay = 1.4f;
    public float fadeTime = 0.8f;
    [Tooltip("제목이 뜬 뒤 결과 패널이 나오기까지 (실제 초)")]
    public float panelDelay = 0.8f;
    public float titleStartScale = 1.6f;

    [Header("결과 문구 타이핑")]
    public bool typeResult = true;
    [Tooltip("글자 하나가 나오는 시간 (초)")]
    public float typeInterval = 0.035f;
    [Tooltip("줄이 바뀔 때 잠깐 쉬는 시간 (초)")]
    public float typeLinePause = 0.25f;
    [Tooltip("타이핑 중 클릭·아무 키를 누르면 바로 다 보여 줌")]
    public bool skipTypingOnInput = true;

    [Header("씬")]
    public string titleSceneName = "TitleScene";

    [Header("테스트")]
    [Tooltip("에디터·개발 빌드에서만: 이 키를 누르면 바로 게임 오버")]
    public KeyCode testKey = KeyCode.F9;

    public bool Running { get; private set; }
    private PlayableGraph graph;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(this); return; }
        Instance = this;
        if (resultPanel != null) resultPanel.SetActive(false);
        if (titleText != null) titleText.gameObject.SetActive(false);
        if (darkOverlay != null) darkOverlay.SetActive(false);
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
        if (graph.IsValid()) graph.Destroy();
    }

    private void Update()
    {
        if (Running || testKey == KeyCode.None || !(Application.isEditor || Debug.isDebugBuild)) return;
        if (Input.GetKeyDown(testKey)) TestGameOver();
    }

    [ContextMenu("테스트: 게임 오버 (플레이 중)")]
    private void TestGameOver()
    {
        if (!Application.isPlaying) { Debug.Log("[GameOver] 플레이 중에만 테스트할 수 있습니다."); return; }
        // 실제와 같은 길: 체력을 0으로 → PlayerHealth → 이 연출
        if (PlayerHealth.Instance != null && !PlayerHealth.Instance.IsDead) PlayerHealth.Instance.Kill("Test");
        else Begin(SceneManager.GetActiveScene().name == "BossScene" ? Kind.BossDefeat : Kind.GameOver);
    }

    /// <summary>게임 오버 연출 시작 (한 번만)</summary>
    public void Begin(Kind kind)
    {
        if (Running) return;
        Running = true;
        StartCoroutine(Run(kind));
    }

    private IEnumerator Run(Kind kind)
    {
        SkillManager.CastBlocked = true;
        SkillManager.BlockedHint = "";
        BattleUIManager.Instance?.ClearNotices();
        SoundManager.StopBgm(1f);   // 전투 BGM 끄기 → 제목이 뜰 때 게임 오버 BGM
        Camera cam = Camera.main;

        // 1. 슬로 모션
        Time.timeScale = slowMotion;
        yield return new WaitForSecondsRealtime(slowMotionTime);
        Time.timeScale = 1f;

        // 2. 1인칭 숨기고 그 자리에 쓰러지는 여우
        SetActiveAll(hideUI, false);
        HideFirstPerson(cam);
        Vector3 feet = cam != null ? FloorBelow(cam.transform.position) : transform.position;
        Vector3 fwd = cam != null ? cam.transform.forward : Vector3.forward;
        if (modelSpawnPoint != null)
        {
            feet = FloorBelow(modelSpawnPoint.position + Vector3.up * 0.5f);
            fwd = modelSpawnPoint.forward;
        }
        fwd.y = 0f;
        fwd = fwd.sqrMagnitude > 0.001f ? fwd.normalized : Vector3.forward;
        SpawnFallingModel(feet, fwd);

        // 3. 카메라가 뒤·위로 빠지며 주인공을 내려다봄 (동시에 검은 화면 → 제목 → 패널)
        if (cam != null) StartCoroutine(MoveCamera(cam.transform, feet, fwd));
        yield return new WaitForSecondsRealtime(overlayDelay);

        CanvasGroup overlay = OverlayGroup();
        if (overlay != null) StartCoroutine(FadeGroup(overlay, 0f, overlayAlpha, fadeTime));
        yield return new WaitForSecondsRealtime(fadeTime * 0.6f);

        if (titleText != null)
        {
            titleText.text = kind == Kind.BossDefeat ? bossDefeatTitle : kind == Kind.TimeOver ? timeOverTitle : gameOverTitle;
            titleText.color = titleColor;
            titleText.gameObject.SetActive(true);
            SoundManager.PlayBgm(L => L.bgmGameOver, 0.3f, SoundManager.Lib != null && SoundManager.Lib.gameOverBgmLoop);
            yield return TitlePop(titleText.transform);
        }
        yield return new WaitForSecondsRealtime(panelDelay);

        // 4. 결과 패널 + 버튼
        if (resultText != null)
        {
            resultText.text = ResultText(kind);
            if (typeResult) resultText.maxVisibleCharacters = 0;
        }
        // 버튼은 타이핑이 끝난 뒤에 나타남
        UnityEngine.UI.Button[] buttons = resultPanel != null ? resultPanel.GetComponentsInChildren<UnityEngine.UI.Button>(true) : new UnityEngine.UI.Button[0];
        foreach (UnityEngine.UI.Button b in buttons) Group(b.gameObject).alpha = 0f;
        if (resultPanel != null)
        {
            resultPanel.SetActive(true);
            CanvasGroup g = Group(resultPanel);
            g.interactable = false;
            yield return FadeGroup(g, 0f, 1f, fadeTime);
        }
        if (resultText != null && typeResult) yield return TypeResult(resultText);
        foreach (UnityEngine.UI.Button b in buttons) StartCoroutine(FadeGroup(Group(b.gameObject), 0f, 1f, fadeTime * 0.6f));
        if (resultPanel != null)
        {
            CanvasGroup g = Group(resultPanel);
            g.interactable = true;
            g.blocksRaycasts = true;
        }
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    // 결과 문구를 한 글자씩 (색 태그 등은 글자 수에서 빠짐)
    private IEnumerator TypeResult(TMP_Text text)
    {
        text.ForceMeshUpdate();
        TMP_TextInfo info = text.textInfo;
        int total = info.characterCount;
        float wait = 0f;
        for (int i = 0; i < total; i++)
        {
            text.maxVisibleCharacters = i + 1;
            char c = info.characterInfo[i].character;
            if (!char.IsWhiteSpace(c)) SoundManager.Typing();
            wait = c == '\n' ? typeLinePause : (c == ' ' ? typeInterval * 0.5f : typeInterval);
            for (float t = 0f; t < wait; t += Time.unscaledDeltaTime)
            {
                if (skipTypingOnInput && (Input.anyKeyDown || Input.GetMouseButtonDown(0))) { i = total; break; }
                yield return null;
            }
        }
        text.maxVisibleCharacters = 99999;
        yield return new WaitForSecondsRealtime(0.2f);
    }

    // ---------- 결과 문구 ----------
    private static string ResultText(Kind kind)
    {
        if (kind == Kind.GameOver || !GameResult.IsBossFight)
        {
            return $"Monsters defeated  {GameResult.Kills}\n" +
                   $"Spells learned  {GameResult.LearnedSpells}\n" +
                   $"Time survived  {GameResult.PlayTimeText}\n" +
                   $"Score  {GameResult.Score}";
        }
        return $"Demon Lord HP left  {Mathf.CeilToInt(GameResult.BossHpLeftRatio * 100f)}%\n" +
               $"Turns used  {GameResult.BossTurnsUsed} / {GameResult.BossMaxTurns}\n" +
               $"Weakness hit rate  {Mathf.RoundToInt(GameResult.BossWeaknessRate * 100f)}%\n" +
               $"Spells learned  {GameResult.LearnedSpells}";
    }

    // ---------- 버튼 (패널 안 버튼의 OnClick에 연결) ----------
    // 다시하기: 진 씬부터 (게임씬 → 게임씬 처음 / 보스전 → 보스전 처음)
    public void OnClickRetry()
    {
        ResetState();
        SceneManager.LoadScene(SceneManager.GetActiveScene().name);
    }

    public void OnClickTitle()
    {
        ResetState();
        SceneManager.LoadScene(titleSceneName);
    }

    private static void ResetState()
    {
        Time.timeScale = 1f;
        SkillManager.CastBlocked = false;
        SkillManager.BlockedHint = null;
        GameResult.ClearBoss();
    }

    // ---------- 연출 도우미 ----------
    private void HideFirstPerson(Camera cam)
    {
        if (hideFirstPerson != null && hideFirstPerson.Length > 0)
        {
            SetActiveAll(hideFirstPerson, false);
            return;
        }
        // 지팡이(WandCaster)를 찾아 카메라 바로 아래 묶음(WandRig)째 숨김
        WandCaster wand = FindFirstObjectByType<WandCaster>();
        if (wand == null) return;
        // 카메라 아래에 있으면 카메라 바로 아래 묶음째, 아니면 WandCaster 오브젝트(WandRig)만
        //   (예전엔 끝까지 올라가 [OBJECTS] 같은 맵 묶음 전체를 꺼 버리던 버그)
        Transform t = wand.transform;
        if (cam != null && t.IsChildOf(cam.transform))
        {
            while (t.parent != null && t.parent != cam.transform) t = t.parent;
        }
        t.gameObject.SetActive(false);
    }

    private void SpawnFallingModel(Vector3 feet, Vector3 fwd)
    {
        if (playerModel == null) return;
        Quaternion rot = Quaternion.LookRotation(fwd) * Quaternion.Euler(0f, modelYaw, 0f);
        GameObject model = Instantiate(playerModel, feet + Vector3.up * modelHeight, rot);
        model.name = "GameOverPlayer";
        if (modelHeightMeters > 0f)
        {
            // 초상화용 모델은 원래 크기가 매우 커서 그대로 쓰면 화면을 다 가림 → 키를 맞춤
            float h = ModelHeight(model);
            if (h > 0.01f) model.transform.localScale *= modelHeightMeters / h;
        }
        model.transform.localScale *= modelScale;
        SetLayer(model.transform, 0);   // 초상화용(Portrait) 레이어면 메인 카메라에 안 보이므로 Default로
        StartCoroutine(StartFallLater(model));
    }

    // 카메라가 빠지는 동안은 서 있다가 → 넘어짐 시작
    private IEnumerator StartFallLater(GameObject model)
    {
        // 서 있는 자세 (Fall 첫 프레임에서 멈춤)
        bool clip = fallClip != null && playFallClip;
        if (clip) PlayFallClip(model, true);
        if (fallStartDelay > 0f) yield return new WaitForSecondsRealtime(fallStartDelay);
        if (model == null) yield break;
        SoundManager.Play(L => L.gameOverFall, 1f, 1f, false);
        if (clip && graph.IsValid()) graph.Play();
        if (comicFall) StartCoroutine(ComicTumble(model.transform));
    }

    private void PlayFallClip(GameObject model, bool paused)
    {

        if (fallClip.legacy)
        {
            Animation a = model.GetComponentInChildren<Animation>();
            if (a == null) a = model.AddComponent<Animation>();
            a.AddClip(fallClip, fallClip.name);
            a.Play(fallClip.name);
            return;
        }
        // 클립의 뼈대 경로(IdleDemo/...)가 맞도록, IdleDemo의 부모에 Animator를 둠
        Transform host = model.transform;
        Transform clipRoot = string.IsNullOrEmpty(clipRootName) ? null : FindDeep(model.transform, clipRootName);
        if (clipRoot != null && clipRoot.parent != null) host = clipRoot.parent;
        else if (!string.IsNullOrEmpty(clipRootName)) Debug.LogWarning($"[GameOver] 모델에서 '{clipRootName}'을(를) 찾지 못했습니다. Fall 동작이 재생되지 않을 수 있습니다.");
        foreach (Animator other in model.GetComponentsInChildren<Animator>(true)) if (other.transform != host) other.enabled = false;   // 다른 Animator가 덮어쓰지 않게
        foreach (Animation legacy in model.GetComponentsInChildren<Animation>(true)) { legacy.Stop(); legacy.enabled = false; }   // 프리팹의 Idle(Animation)이 덮어쓰지 않게
        Animator animator = host.GetComponent<Animator>();
        if (animator == null) animator = host.gameObject.AddComponent<Animator>();
        animator.runtimeAnimatorController = null;
        animator.avatar = null;
        animator.applyRootMotion = false;
        animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        animator.Rebind();
        Debug.Log($"[GameOver] Fall 재생: {fallClip.name} (Animator 위치: {host.name})");
        AnimationPlayableUtilities.PlayClip(animator, fallClip, out graph);   // 끝나면 마지막 자세 유지
        if (paused)
        {
            graph.Evaluate(0f);   // 첫 프레임 자세로
            graph.Stop();
        }
    }

    // 우당탕: 펄쩍 뛰며 빙글 → 발을 축으로 벌러덩 → 쿵 (찌그러짐 + 흔들림 + 먼지) → 통통 튀다가 뻗음
    private IEnumerator ComicTumble(Transform m)
    {
        Vector3 basePos = m.position;
        Quaternion baseRot = m.rotation;
        Vector3 baseScale = m.localScale;
        float h = ModelHeight(m.gameObject);

        // 넘어지는 축 (모델 기준)
        Vector3 axis;
        switch (fallDirection)
        {
            case FallDir.Forward: axis = m.right; break;
            case FallDir.Left: axis = m.forward; break;
            case FallDir.Right: axis = -m.forward; break;
            default: axis = -m.right; break;   // 뒤로 벌러덩
        }
        Vector3 up = Vector3.up;

        // 1. 펄쩍 + 빙글
        const float hopTime = 0.35f;
        for (float t = 0f; t < hopTime; t += Time.deltaTime)
        {
            float k = t / hopTime;
            float y = Mathf.Sin(k * Mathf.PI) * hopHeight * h;
            m.position = basePos + up * y;
            m.rotation = Quaternion.AngleAxis(360f * spinTurns * Mathf.SmoothStep(0f, 1f, k), up) * baseRot;
            float stretch = 1f + 0.15f * Mathf.Sin(k * Mathf.PI);   // 뛰는 동안 살짝 길쭉
            m.localScale = new Vector3(baseScale.x / Mathf.Sqrt(stretch), baseScale.y * stretch, baseScale.z / Mathf.Sqrt(stretch));
            yield return null;
        }
        m.position = basePos;
        m.localScale = baseScale;

        // 2. 벌러덩 (발을 축으로 90도 + 살짝 지나쳤다가)
        const float topple = 0.32f;
        for (float t = 0f; t < topple; t += Time.deltaTime)
        {
            float k = t / topple;
            float a = Mathf.Lerp(0f, 98f, k * k);   // 점점 빨라지며 넘어감
            m.rotation = Quaternion.AngleAxis(a, axis) * baseRot;
            yield return null;
        }
        Quaternion lying = Quaternion.AngleAxis(90f, axis) * baseRot;

        // 3. 쿵!
        if (landShake > 0f) CameraKick.Shake(landShake, 0.35f);
        if (landEffect != null) EffectUtil.SpawnScaled(landEffect, basePos + up * 0.05f, Quaternion.identity, landEffectScale);

        // 4. 찌그러졌다가 통통 튀며 뻗음
        float bounceH = 0.18f * h;
        for (int b = 0; b <= bounces; b++)
        {
            // 찌그러짐 (누운 상태라 세계 기준 위아래 = 모델 앞뒤 축이 바뀌므로 전체를 납작하게)
            const float squashTime = 0.12f;
            for (float t = 0f; t < squashTime; t += Time.deltaTime)
            {
                float s = Mathf.Sin(t / squashTime * Mathf.PI) * squash / (b + 1);
                m.localScale = new Vector3(baseScale.x * (1f + s), baseScale.y * (1f - s), baseScale.z * (1f + s));
                m.rotation = Quaternion.Slerp(m.rotation, lying, 0.35f);
                yield return null;
            }
            m.localScale = baseScale;
            if (b == bounces) break;

            float hb = bounceH / (b + 1);
            float bt = 0.22f / (b + 1) + 0.08f;
            for (float t = 0f; t < bt; t += Time.deltaTime)
            {
                float k = t / bt;
                m.position = basePos + up * Mathf.Sin(k * Mathf.PI) * hb;
                m.rotation = Quaternion.AngleAxis(Mathf.Sin(k * Mathf.PI) * -12f / (b + 1), axis) * lying;   // 튀면서 살짝 들썩
                yield return null;
            }
            m.position = basePos;
        }
        m.position = basePos;
        m.rotation = lying;
        m.localScale = baseScale;
    }

    private static float ModelHeight(GameObject go)
    {
        bool has = false;
        Bounds b = new Bounds(go.transform.position, Vector3.zero);
        foreach (Renderer r in go.GetComponentsInChildren<Renderer>())
        {
            if (!has) { b = r.bounds; has = true; } else b.Encapsulate(r.bounds);
        }
        return has ? Mathf.Max(0.3f, b.size.y) : 1f;
    }

    private IEnumerator MoveCamera(Transform ct, Vector3 feet, Vector3 fwd)
    {
        Vector3 right = Vector3.Cross(Vector3.up, fwd);
        Vector3 fromPos = ct.position;
        Quaternion fromRot = ct.rotation;
        float side = cameraInFront ? -1f : 1f;   // 앞에서 볼 때는 앞쪽(+fwd)으로
        Vector3 toPos = cameraPoint != null ? cameraPoint.position
                      : feet - fwd * camBack * side + Vector3.up * camUp + right * camSide;
        Vector3 look = feet + Vector3.up * lookHeight;
        for (float t = 0f; t < camMoveTime; t += Time.unscaledDeltaTime)
        {
            float k = Mathf.SmoothStep(0f, 1f, t / camMoveTime);
            ct.position = Vector3.Lerp(fromPos, toPos, k);
            ct.rotation = Quaternion.Slerp(fromRot, Quaternion.LookRotation(look - ct.position), k);
            yield return null;
        }
        ct.position = toPos;
        ct.rotation = Quaternion.LookRotation(look - toPos);
    }

    private IEnumerator TitlePop(Transform t)
    {
        Vector3 baseScale = t.localScale;
        CanvasGroup g = Group(t.gameObject);
        for (float x = 0f; x < fadeTime; x += Time.unscaledDeltaTime)
        {
            float k = Mathf.Clamp01(x / fadeTime);
            float e = 1f - (1f - k) * (1f - k);
            t.localScale = baseScale * Mathf.Lerp(titleStartScale, 1f, e);
            g.alpha = k;
            yield return null;
        }
        t.localScale = baseScale;
        g.alpha = 1f;
    }

    private CanvasGroup OverlayGroup()
    {
        if (darkOverlay == null)
        {
            // 연결하지 않았으면 화면 전체 검은 막을 코드로 만듦
            darkOverlay = new GameObject("GameOverOverlay", typeof(RectTransform), typeof(Canvas), typeof(UnityEngine.UI.Image));
            Canvas c = darkOverlay.GetComponent<Canvas>();
            c.renderMode = RenderMode.ScreenSpaceOverlay;
            c.sortingOrder = -100;   // 다른 UI(제목·패널)보다 아래, 게임 화면보다는 위
            darkOverlay.GetComponent<UnityEngine.UI.Image>().color = Color.black;
            darkOverlay.GetComponent<UnityEngine.UI.Image>().raycastTarget = false;
        }
        darkOverlay.SetActive(true);
        CanvasGroup g = Group(darkOverlay);
        g.alpha = 0f;
        g.blocksRaycasts = false;
        return g;
    }

    private static CanvasGroup Group(GameObject go)
    {
        CanvasGroup g = go.GetComponent<CanvasGroup>();
        if (g == null) g = go.AddComponent<CanvasGroup>();
        return g;
    }

    private static IEnumerator FadeGroup(CanvasGroup g, float from, float to, float time)
    {
        for (float t = 0f; t < time && g != null; t += Time.unscaledDeltaTime)
        {
            g.alpha = Mathf.Lerp(from, to, t / time);
            yield return null;
        }
        if (g != null) g.alpha = to;
    }

    private static Vector3 FloorBelow(Vector3 p)
    {
        if (Physics.Raycast(p, Vector3.down, out RaycastHit hit, 10f, ~0, QueryTriggerInteraction.Ignore)) return hit.point;
        return new Vector3(p.x, p.y - 1.6f, p.z);
    }

    private static Transform FindDeep(Transform root, string name)
    {
        if (root.name == name) return root;
        foreach (Transform c in root)
        {
            Transform r = FindDeep(c, name);
            if (r != null) return r;
        }
        return null;
    }

    private static void SetLayer(Transform t, int layer)
    {
        t.gameObject.layer = layer;
        foreach (Transform c in t) SetLayer(c, layer);
    }

    private static void SetActiveAll(GameObject[] list, bool active)
    {
        if (list == null) return;
        foreach (GameObject go in list) if (go != null) go.SetActive(active);
    }
}