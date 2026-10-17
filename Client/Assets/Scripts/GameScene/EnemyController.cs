using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public enum MonsterRank { Low, Mid, High } // 하급, 중급, 상급

// 몹 프리팹 루트에 붙이는 스크립트 (3D)
// 피격 → 상성 데미지 → HP 0이면 콜라이더 끄기 → Death 애니메이션 끝까지 대기 → 스펠 드랍 → 삭제
public class EnemyController : MonoBehaviour
{
    // ---------------------------------------------------------
    // 살아있는 몹 목록 (타겟팅용). 태그 없이도 가장 가까운 몹을 찾을 수 있음
    // ---------------------------------------------------------
    private static readonly List<EnemyController> aliveEnemies = new List<EnemyController>();
    public static IReadOnlyList<EnemyController> AliveEnemies => aliveEnemies;

    public static EnemyController GetNearest(Vector3 from)
    {
        EnemyController nearest = null;
        float best = float.MaxValue;
        foreach (EnemyController enemy in aliveEnemies)
        {
            // 스폰포인트에 도착하지 않았거나, 쓰러지는 중이거나,
            // 이미 날아가는 마법만으로 쓰러질 몹은 새 타겟이 될 수 없음
            if (enemy == null || !enemy.IsTargetable) continue;
            float d = (enemy.AimPoint - from).sqrMagnitude;
            if (d < best)
            {
                best = d;
                nearest = enemy;
            }
        }
        return nearest;
    }

    // 스폰포인트에 도착해서 공격할 수 있는 몹이 하나라도 있는지 (= 마법 시전 가능 여부)
    public static bool AnyReady
    {
        get
        {
            foreach (EnemyController enemy in aliveEnemies)
            {
                if (enemy != null && enemy.IsTargetable) return true;
            }
            return false;
        }
    }

    [Header("몹 설정")]
    public MagicElement enemyType = MagicElement.Normal;
    public MonsterRank rank = MonsterRank.Low;
    [Tooltip("등장 자막·몹 도감에 표시할 이름. 비우면 프리팹 이름에서 자동 (High_Demon → Demon)\n" +
             "한글로 적으면 '데몬이 나타났습니다'처럼 조사(이/가)가 정확해집니다")]
    public string displayName = "";
    [Tooltip("체크하면 등급에 따라 HP 자동 설정 (하급 20 / 중급 50 / 상급 100)")]
    public bool useRankDefaultHP = true;
    public float maxHP = 20f;

    [Header("애니메이션")]
    public Animator anim;
    [Tooltip("Animator의 사망 Trigger 파라미터 이름")]
    public string dieTrigger = "Die";
    [Tooltip("Animator의 사망 State 이름 (또는 State의 Tag)")]
    public string deathStateName = "Die";
    [Tooltip("피격 Trigger 파라미터 이름 (없으면 비워두기)")]
    public string hitTrigger = "";
    [Tooltip("Animator가 없거나 Die State를 못 찾았을 때 대기 시간")]
    public float deathFallbackDuration = 1.5f;
    [Tooltip("Death 애니메이션이 끝난 뒤(마지막 자세로 멈춘 채) 사라지기 시작할 때까지의 시간")]
    public float destroyDelayAfterDeath = 0.2f;
    [Tooltip("Death 애니메이션이 끝나면 마지막 자세에서 멈춤 (Death 클립이 반복 재생돼서\n" +
             "다시 일어서는 듯 보이는 문제 방지)")]
    public bool freezeOnDeathEnd = true;
    [Tooltip("[애니메이션 있는 몹] 쓰러진 뒤 작아지면서 사라지는 시간(초). 0이면 바로 사라짐")]
    public float despawnShrinkDuration = 0.35f;

    [Header("애니메이션이 없을 때의 대체 연출")]
    [Tooltip("[Animator 없는 몹] 사망 시 작아지면서 사라짐. 끄면 Death Fallback Duration만큼 서 있다가 사라짐")]
    public bool shrinkOnDeath = true;
    [Tooltip("작아져서 사라지는 데 걸리는 시간(초)")]
    public float shrinkDuration = 0.4f;
    [Tooltip("[피격 애니메이션이 없는 몹] 맞았을 때 살짝 움찔하는 효과.\nAnimator가 없거나 Hit Trigger를 비워 둔 몹에 적용")]
    public bool punchOnHit = true;
    [Tooltip("움찔하는 크기 (0.15 = 15% 커졌다 돌아옴)")]
    public float punchScale = 0.15f;

    [Header("사망 이펙트 (선택)")]
    [Tooltip("사망 순간 몸 중앙에 생성될 이펙트 (연기·빛 등). 모든 몹에 사용 가능, 비워도 됨")]
    public GameObject deathEffectPrefab;

    [Header("드랍")]
    public DropItem dropItemPrefab;
    [Tooltip("두루마리가 처음 생겨나는 위치 보정 (발밑 기준). 여기서 몹과 플레이어 사이 공중으로 날아감")]
    public Vector3 dropOffset = new Vector3(0f, 1f, 0f);

    [Header("높이")]
    [Tooltip("바닥에서 떠 있는 높이 (날아다니는 몹만). 걷는 몹은 0.\n" +
             "스폰 지점들은 바닥 높이에 두고, 이 값만큼 띄워서 이동합니다.\n" +
             "장판 마법·드랍 두루마리는 여기서 이 높이만큼 아래(바닥)를 기준으로 생깁니다.")]
    [Min(0f)] public float hoverHeight = 0f;

    [Header("방향")]
    [Tooltip("모델이 엉뚱한 방향을 볼 때 돌려 주는 각도 (Y축).\n" +
             "뒤돌아 있으면 180, 옆을 보면 90 또는 -90")]
    public float facingYawOffset = 0f;
    private Quaternion FacingOffset => Quaternion.Euler(0f, facingYawOffset, 0f);

    [Header("공격 (플레이어 체력 깎기)")]
    [Tooltip("스폰포인트에 도착한 뒤 일정 시간마다 플레이어를 공격")]
    public bool canAttack = true;
    [Tooltip("체크하면 자동: 몹별 추천값(EnemyController의 AttackPresets 표) → 표에 없으면 등급 기본값\n" +
             "(하급 9초·8 / 중급 8초·12 / 상급 7초·20). 끄면 아래 두 칸 값을 그대로 사용")]
    public bool useRankDefaultAttack = true;
    [Tooltip("공격 간격 (초)")]
    public float attackInterval = 9f;
    [Tooltip("한 번 공격할 때 플레이어가 잃는 체력")]
    public float attackDamage = 8f;
    [Tooltip("공격 직전 부르르 떠는 예고 시간 (초)")]
    public float attackTelegraph = 0.7f;
    [Tooltip("예고 때 떨리는 크기")]
    public float telegraphShake = 0.05f;
    [Tooltip("공격할 때 플레이어 쪽으로 튀어나오는 거리")]
    public float lungeDistance = 0.8f;
    [Tooltip("마법에 맞으면 공격 타이머가 이만큼(초) 뒤로 밀림 (경직)")]
    public float hitStagger = 1.5f;

    [Header("등장 이동 (성 밖 → 스폰포인트)")]
    [Tooltip("이동 속도 (초당 미터)")]
    public float moveSpeed = 3f;
    [Tooltip("회전 속도 (초당 각도)")]
    public float turnSpeed = 360f;
    [Tooltip("이동 중에 켤 Animator Bool 파라미터 (걷기/달리기 몹은 'Move').\n" +
             "날아다니는 몹이나 애니메이션 없는 몹은 비워두기 → Idle 그대로 이동")]
    public string moveBool = "";

    public float CurrentHP { get; private set; }
    public bool IsDying { get; private set; }
    // 스폰포인트에 도착해 공격받을 수 있는 상태 (이동 없이 생성되면 바로 true)
    public bool IsReady { get; private set; } = true;

    // 시전은 됐지만 아직 맞지 않은(날아가는 중·차지 중·장판 연출 중) 마법의 데미지 합
    public float PendingDamage { get; private set; }
    // 이미 시전된 마법만으로 HP가 0 이하가 될 예정 → 더 이상 영창을 받지 않음
    public bool IsDoomed => !IsDying && CurrentHP - PendingDamage <= 0f;
    // 새 마법의 타겟이 될 수 있는 상태
    public bool IsTargetable => IsReady && !IsDying && !IsDoomed;

    [Tooltip("예약된 마법이 이 시간(초) 안에 맞지 않으면 예약을 풀어 다시 영창할 수 있게 함 (안전장치)")]
    public float pendingDamageTimeout = 8f;
    private float lastReserveTime;

    // 스폰포인트에 도착한 순간
    public event Action<EnemyController> OnReady;

    // HP가 0이 된 순간 (Death 애니메이션 시작 시점)
    public event Action<EnemyController> OnDied;
    // Death 애니메이션·드랍까지 끝나고 맵에서 사라지는 순간 (다음 몹 스폰 기준)
    public event Action<EnemyController> OnDespawned;

    private Collider[] colliders;
    private Renderer[] renderers;
    private Vector3 originalScale;
    private Coroutine punchRoutine;

    // Animator가 없거나 Controller가 비어 있으면 '애니메이션 없는 몹'으로 취급
    private bool HasAnimation => anim != null && anim.runtimeAnimatorController != null;

    // 마법이 날아갈 몸 중앙 좌표
    public Vector3 AimPoint
    {
        get
        {
            if (renderers != null && renderers.Length > 0)
            {
                Bounds bounds = new Bounds();
                bool hasBounds = false;
                foreach (Renderer r in renderers)
                {
                    if (r == null || !r.enabled) continue;
                    if (!hasBounds) { bounds = r.bounds; hasBounds = true; }
                    else bounds.Encapsulate(r.bounds);
                }
                if (hasBounds) return bounds.center;
            }
            return transform.position + Vector3.up;
        }
    }

    // 장판형 마법이 생성될 발밑 좌표
    // 다음 공격까지 진행도 (0 → 1이 되면 공격). HUD 공격 게이지에 사용
    public float AttackProgress => (canAttack && IsReady && attackInterval > 0f) ? Mathf.Clamp01(attackTimer / attackInterval) : 0f;
    public bool IsAttacking => attackRoutine != null;
    private float attackTimer;
    private Coroutine attackRoutine;
    private Vector3 attackBasePos;

    public static void RankDefaultAttack(MonsterRank r, out float interval, out float damage)
    {
        switch (r)
        {
            case MonsterRank.Mid: interval = 8f; damage = 12f; break;
            case MonsterRank.High: interval = 7f; damage = 20f; break;
            default: interval = 9f; damage = 8f; break;
        }
    }

    public Vector3 GroundPoint => transform.position - Vector3.up * hoverHeight;

    public static float DefaultHP(MonsterRank r)
    {
        switch (r)
        {
            case MonsterRank.Mid: return 50f;
            case MonsterRank.High: return 100f;
            default: return 20f;
        }
    }

    public static string RankToKorean(MonsterRank r)
    {
        switch (r)
        {
            case MonsterRank.Mid: return "중급";
            case MonsterRank.High: return "상급";
            default: return "하급";
        }
    }

    // 처치 자막 등에 쓰는 속성 색 (주문 자막 색과 동일)
    public static Color ElementColor(MagicElement e)
    {
        switch (e)
        {
            case MagicElement.Water: return new Color(0.31f, 0.76f, 1f);    // 하늘색
            case MagicElement.Fire: return new Color(1f, 0.35f, 0.21f);     // 주황빛 빨강
            case MagicElement.Grass: return new Color(0.42f, 0.86f, 0.36f); // 연두
            default: return new Color(1f, 0.84f, 0f);                       // 노말: 금색
        }
    }

    // 표시 이름: displayName → 없으면 프리팹/오브젝트 이름에서 등급 접두어와 (Clone) 제거
    public string DisplayName => GetDisplayName(this);

    public static string GetDisplayName(EnemyController enemy)
    {
        if (enemy == null) return "";
        if (!string.IsNullOrWhiteSpace(enemy.displayName)) return enemy.displayName.Trim();
        return CleanName(enemy.gameObject.name);
    }

    public static string CleanName(string raw)
    {
        string n = (raw ?? "").Replace("(Clone)", "").Trim();
        foreach (string prefix in new[] { "Low_", "Mid_", "High_" })
        {
            if (n.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) { n = n.Substring(prefix.Length); break; }
        }
        return SplitWords(n);
    }

    // MagmaDemon → Magma Demon, Goblin_Shark → Goblin Shark
    private static string SplitWords(string n)
    {
        System.Text.StringBuilder sb = new System.Text.StringBuilder(n.Length + 4);
        for (int i = 0; i < n.Length; i++)
        {
            char c = n[i];
            if (c == '_' || c == '-') { if (sb.Length > 0 && sb[sb.Length - 1] != ' ') sb.Append(' '); continue; }
            if (i > 0 && char.IsUpper(c) && (char.IsLower(n[i - 1]) || char.IsDigit(n[i - 1])) && sb.Length > 0 && sb[sb.Length - 1] != ' ')
                sb.Append(' ');
            sb.Append(c);
        }
        return sb.ToString().Trim();
    }

    // 도감·통계용 고유 키 (프리팹 이름 기준, (Clone) 제거)
    public static string KeyOf(EnemyController enemy) =>
        enemy == null ? "" : enemy.gameObject.name.Replace("(Clone)", "").Trim();

    // 이 몹을 1.5배로 때리는 마법 속성 (노말 몹은 약점 없음 → false)
    public static bool TryGetWeakness(MagicElement defend, out MagicElement weakness)
    {
        switch (defend)
        {
            case MagicElement.Fire: weakness = MagicElement.Water; return true;
            case MagicElement.Grass: weakness = MagicElement.Fire; return true;
            case MagicElement.Water: weakness = MagicElement.Grass; return true;
            default: weakness = MagicElement.Normal; return false;
        }
    }

    // 실제로 적용될 공격 간격·데미지 (프리팹에서도 사용 가능)
    public void GetAttackStats(out float interval, out float damage)
    {
        if (!useRankDefaultAttack) { interval = attackInterval; damage = attackDamage; return; }
        if (AttackPresets.TryGetValue(KeyOf(this), out Vector2 p)) { interval = p.x; damage = p.y; return; }
        RankDefaultAttack(rank, out interval, out damage);
    }

    // 몹별 추천 공격값 (프리팹 이름 → (공격 간격 초, 데미지))
    // 기준: 등급마다 초당 피해가 비슷하도록(하급 ≈0.9 / 중급 ≈1.5 / 상급 ≈2.9),
    //       작고 날쌘 몹은 '자주 약하게', 크고 무거운 몹은 '가끔 세게'
    public static readonly Dictionary<string, Vector2> AttackPresets = new Dictionary<string, Vector2>
    {
        // 하급 (HP 20)
        { "Low_Bat",         new Vector2(7f,   6f)  },  // 노말: 빠른 박쥐
        { "Low_Demon",       new Vector2(9f,   8f)  },  // 불: 기본
        { "Low_Spirit",      new Vector2(8f,   7f)  },  // 불: 가벼운 정령
        { "Low_Bee",         new Vector2(6.5f, 6f)  },  // 풀: 자주 쏘는 벌
        { "Low_Slime",       new Vector2(10f,  9f)  },  // 풀: 느릿한 슬라임
        { "Low_Anglerfish",  new Vector2(9f,   8f)  },  // 물: 기본
        { "Low_Crab",        new Vector2(10f,  10f) },  // 물: 느리지만 묵직한 집게
        { "Low_Fish",        new Vector2(7f,   6f)  },  // 물: 날쌘 물고기
        // 중급 (HP 50)
        { "Mid_Zombie",      new Vector2(9f,   14f) },  // 노말: 느리고 아픔
        { "Mid_Dragon",      new Vector2(8f,   13f) },  // 불: 기본
        { "Mid_BabyDragon",  new Vector2(6.5f, 10f) },  // 불: 작고 빠른 아기 용
        { "Mid_Skeleton Mage",new Vector2(7.5f, 11f) },  // 불: 해골 마법사
        { "Mid_Tribal",      new Vector2(7f,   11f) },  // 풀: 창 찌르기
        { "Mid_BlueDemon",   new Vector2(8f,   13f) },  // 물: 기본
        { "Mid_GoblinShark", new Vector2(7f,   11f) },  // 물: 물어뜯기
        // 상급 (HP 100)
        { "High_Skeleton",   new Vector2(6.5f, 18f) },  // 노말: 빠른 상급
        { "High_MagmaDemon", new Vector2(7f,   20f) },  // 불: 마그마 데몬
        { "High_FlameDragon",new Vector2(8.5f, 26f) },  // 불: 느리지만 강한 화염 브레스
        { "High_MushroomKing",new Vector2(8.5f,25f) },  // 풀: 묵직한 버섯왕
        { "High_Yeti",       new Vector2(8f,   24f) },  // 물: 강한 한 방
    };

    // 한국어 조사 고르기: Josa("데몬", "이", "가") → "이"
    // 영어 이름은 발음 끝소리를 대략 추정 (정확히 하려면 displayName을 한글로)
    public static string Josa(string word, string withFinal, string withoutFinal)
    {
        if (string.IsNullOrEmpty(word)) return withoutFinal;
        char c = word[word.Length - 1];
        if (c >= 0xAC00 && c <= 0xD7A3) return ((c - 0xAC00) % 28) != 0 ? withFinal : withoutFinal;
        if (char.IsDigit(c)) return "013678".IndexOf(c) >= 0 ? withFinal : withoutFinal;

        string w = word.ToLowerInvariant();
        if (w.EndsWith("ng") || w.EndsWith("m") || w.EndsWith("n") || w.EndsWith("l")) return withFinal;
        if (w.EndsWith("b") || w.EndsWith("d") || w.EndsWith("g") || w.EndsWith("p") || w.EndsWith("t")) return withFinal;
        // 묵음 e: Slime(슬라임), Stone(스톤) → 앞 글자가 m/n/l이면 받침
        if (w.Length >= 2 && w.EndsWith("e") && "mnl".IndexOf(w[w.Length - 2]) >= 0) return withFinal;
        return withoutFinal;
    }

    public static string ElementToKorean(MagicElement e)
    {
        switch (e)
        {
            case MagicElement.Water: return "물";
            case MagicElement.Fire: return "불";
            case MagicElement.Grass: return "풀";
            default: return "노말";
        }
    }

    private void Awake()
    {
        if (anim == null) anim = GetComponentInChildren<Animator>();
        colliders = GetComponentsInChildren<Collider>();
        renderers = GetComponentsInChildren<Renderer>();
        originalScale = transform.localScale;

        // 날아다니는 몹은 바닥 높이의 생성 지점에서 hoverHeight만큼 띄워서 시작
        if (hoverHeight > 0f) transform.position += Vector3.up * hoverHeight;
        // 모델 방향 보정 (생성 직후 방향에도 적용)
        if (facingYawOffset != 0f) transform.rotation *= FacingOffset;

        if (useRankDefaultHP) maxHP = DefaultHP(rank);
        if (useRankDefaultAttack) GetAttackStats(out attackInterval, out attackDamage);
        CurrentHP = maxHP;
    }

    private void Update()
    {
        // 안전장치: 투사체가 사라지는 등 예약된 마법이 끝내 맞지 않으면 예약을 풀어 준다
        if (PendingDamage > 0f && !IsDying && Time.time - lastReserveTime > pendingDamageTimeout)
        {
            Debug.LogWarning($"[{gameObject.name}] 예약된 마법이 {pendingDamageTimeout}초 동안 맞지 않아 예약을 해제합니다.");
            PendingDamage = 0f;
        }

        UpdateAttack();
    }

    // =========================================================
    // 공격: 도착 후 attackInterval마다 예고(떨림) → 튀어나오며 공격 → 제자리
    // =========================================================
    private void UpdateAttack()
    {
        if (!canAttack || !IsReady || IsDying || attackRoutine != null) return;
        if (PlayerHealth.Instance == null || PlayerHealth.Instance.IsDead) return;
        // 이미 날아가는 마법으로 쓰러질 예정이면 공격하지 않음 (억울하지 않게)
        if (IsDoomed) return;

        attackTimer += Time.deltaTime;
        if (attackTimer >= attackInterval - attackTelegraph)
        {
            attackRoutine = StartCoroutine(AttackRoutine());
        }
    }

    private IEnumerator AttackRoutine()
    {
        attackBasePos = transform.position;

        // 1. 예고: 부르르 떨기 (점점 강하게)
        float t = 0f;
        while (t < attackTelegraph)
        {
            if (IsDying || IsDoomed) { EndAttack(false); yield break; }
            t += Time.deltaTime;
            attackTimer += Time.deltaTime;
            float k = t / Mathf.Max(0.01f, attackTelegraph);
            Vector3 jitter = new Vector3(UnityEngine.Random.Range(-1f, 1f), 0f, UnityEngine.Random.Range(-1f, 1f));
            transform.position = attackBasePos + jitter * telegraphShake * k;
            yield return null;
        }
        transform.position = attackBasePos;

        // 2. 플레이어 쪽으로 튀어나오기
        Vector3 toPlayer = Vector3.zero;
        if (Camera.main != null)
        {
            toPlayer = Camera.main.transform.position - attackBasePos;
            toPlayer.y = 0f;
            toPlayer = toPlayer.normalized;
        }
        Vector3 lungePos = attackBasePos + toPlayer * lungeDistance;
        const float lungeTime = 0.1f, returnTime = 0.25f;

        for (t = 0f; t < lungeTime; t += Time.deltaTime)
        {
            transform.position = Vector3.Lerp(attackBasePos, lungePos, t / lungeTime);
            yield return null;
        }
        transform.position = lungePos;

        // 3. 적중 (그 사이 마법에 쓰러졌으면 취소)
        if (!IsDying && PlayerHealth.Instance != null)
        {
            PlayerHealth.Instance.TakeDamage(attackDamage, $"{RankToKorean(rank)} {ElementToKorean(enemyType)} 몬스터의 공격");
        }

        // 4. 제자리로
        for (t = 0f; t < returnTime; t += Time.deltaTime)
        {
            transform.position = Vector3.Lerp(lungePos, attackBasePos, t / returnTime);
            yield return null;
        }
        EndAttack(true);
    }

    private void EndAttack(bool attacked)
    {
        transform.position = attackBasePos;
        if (attacked) attackTimer = 0f;
        attackRoutine = null;
    }

    // 사망 등으로 공격을 중간에 끊을 때
    private void CancelAttack()
    {
        if (attackRoutine == null) return;
        StopCoroutine(attackRoutine);
        transform.position = attackBasePos;
        attackRoutine = null;
    }

    private void OnEnable() => aliveEnemies.Add(this);
    private void OnDisable() => aliveEnemies.Remove(this);

    // =========================================================
    // 등장 이동
    // =========================================================
    /// <summary>
    /// 경로(waypoints)를 따라 이동한 뒤, 마지막 지점에서 lookAt 쪽으로 몸을 돌리고 공격 가능 상태가 됩니다.
    /// </summary>
    public void BeginApproach(IList<Vector3> waypoints, Vector3 lookAt, float speedMultiplier = 1f)
    {
        approachSpeedMultiplier = Mathf.Max(0.1f, speedMultiplier);
        if (waypoints == null || waypoints.Count == 0)
        {
            MarkReady();
            return;
        }
        IsReady = false;
        List<Vector3> path = new List<Vector3>(waypoints.Count);
        foreach (Vector3 p in waypoints) path.Add(p + Vector3.up * hoverHeight); // 경로도 같은 높이로
        StartCoroutine(ApproachRoutine(path, lookAt));
    }

    private float approachSpeedMultiplier = 1f;

    private IEnumerator ApproachRoutine(List<Vector3> waypoints, Vector3 lookAt)
    {
        SetMoveAnimation(true);
        float speed = moveSpeed * approachSpeedMultiplier;
        float turn = turnSpeed * approachSpeedMultiplier;

        foreach (Vector3 point in waypoints)
        {
            while ((transform.position - point).sqrMagnitude > 0.0025f) // 5cm 이내면 도착
            {
                // 진행 방향으로 몸 돌리기 (위아래 기울기 없이)
                Vector3 dir = point - transform.position;
                dir.y = 0f;
                if (dir.sqrMagnitude > 0.0001f)
                {
                    Quaternion targetRot = Quaternion.LookRotation(dir) * FacingOffset;
                    transform.rotation = Quaternion.RotateTowards(transform.rotation, targetRot, turn * Time.deltaTime);
                }

                transform.position = Vector3.MoveTowards(transform.position, point, speed * Time.deltaTime);
                yield return null;
            }
            transform.position = point;
        }

        SetMoveAnimation(false);

        // 도착 후 플레이어(카메라) 쪽으로 몸 돌리기
        Vector3 face = lookAt - transform.position;
        face.y = 0f;
        if (face.sqrMagnitude > 0.0001f)
        {
            Quaternion targetRot = Quaternion.LookRotation(face) * FacingOffset;
            float timer = 0f;
            while (Quaternion.Angle(transform.rotation, targetRot) > 1f && timer < 1.5f)
            {
                transform.rotation = Quaternion.RotateTowards(transform.rotation, targetRot, turn * Time.deltaTime);
                timer += Time.deltaTime;
                yield return null;
            }
            transform.rotation = targetRot;
        }

        MarkReady();
    }

    private void MarkReady()
    {
        IsReady = true;
        OnReady?.Invoke(this);
    }

    private void SetMoveAnimation(bool moving)
    {
        if (!HasAnimation || string.IsNullOrEmpty(moveBool)) return;

        // 파라미터가 없는 Controller에 SetBool을 하면 경고가 뜨므로 확인 후 설정
        foreach (AnimatorControllerParameter p in anim.parameters)
        {
            if (p.name == moveBool && p.type == AnimatorControllerParameterType.Bool)
            {
                anim.SetBool(moveBool, moving);
                return;
            }
        }
        Debug.LogWarning($"[{gameObject.name}] Animator에 Bool 파라미터 '{moveBool}'이(가) 없습니다.");
    }

    // =========================================================
    // 피격
    // =========================================================
    // 이펙트 연출 시간만큼 기다린 뒤 데미지를 주고 싶을 때 사용
    // power: 영창 보너스 (VoiceBonusCalculator 결과, 기본 1.0)
    public void ReceiveSpell(SpellData spell, float delay, float power = 1f)
    {
        if (IsDying || spell == null) return;

        if (delay <= 0f) TakeDamage(spell, power);
        else StartCoroutine(DelayedDamage(spell, delay, power));
    }

    private IEnumerator DelayedDamage(SpellData spell, float delay, float power)
    {
        yield return new WaitForSeconds(delay);
        TakeDamage(spell, power);
    }

    // 이 스펠이 이 몹에게 줄 데미지 = baseDamage × power × 상성
    public float CalculateDamage(SpellData spell, float power = 1f)
    {
        if (spell == null) return 0f;
        return spell.baseDamage * power * GetTypeMultiplier(spell.elementType, enemyType);
    }

    /// <summary>
    /// 마법이 시전된 순간 SkillManager가 호출. 아직 맞기 전이지만 '들어올 데미지'로 예약해 둡니다.
    /// 예약 데미지가 남은 HP 이상이면 IsDoomed = true → 즉시 영창이 막힙니다.
    /// </summary>
    public void ReserveDamage(SpellData spell, float power = 1f)
    {
        if (IsDying || spell == null) return;
        PendingDamage += CalculateDamage(spell, power);
        lastReserveTime = Time.time;
        if (IsDoomed)
        {
            Debug.Log($"[{gameObject.name}] 시전된 마법으로 쓰러질 예정 → 영창 일시 정지 (HP {CurrentHP} / 예약 데미지 {PendingDamage})");
        }
    }

    public void TakeDamage(SpellData spell, float power = 1f)
    {
        if (IsDying || spell == null) return;

        // 1. 상성 배율 계산
        float multiplier = GetTypeMultiplier(spell.elementType, enemyType);

        // 2. 최종 데미지 = baseDamage × power(영창 보너스) × 상성 (예약해 둔 데미지는 실제로 맞았으니 차감)
        float finalDamage = spell.baseDamage * power * multiplier;
        PendingDamage = Mathf.Max(0f, PendingDamage - finalDamage);
        CurrentHP -= finalDamage;

        Debug.Log($"[{gameObject.name}] {spell.spellName} 적중! 데미지: {finalDamage} (영창: {power}x, 상성: {multiplier}x) 남은 HP: {CurrentHP}");

        // 3. UI 텍스트 출력
        if (BattleUIManager.Instance != null)
        {
            BattleUIManager.Instance.ShowEffectiveness(multiplier);
        }

        // ※ 폭발 이펙트는 ProjectileController / SkillManager에서만 생성합니다 (중복 방지)

        // 맞으면 공격이 잠깐 늦춰짐 (경직)
        attackTimer = Mathf.Max(0f, attackTimer - hitStagger);

        // 4. 사망 판정
        if (CurrentHP <= 0f)
        {
            Die();
        }
        else if (HasAnimation && !string.IsNullOrEmpty(hitTrigger))
        {
            anim.SetTrigger(hitTrigger);
        }
        else if (punchOnHit)
        {
            // 피격 애니메이션이 없는 몹 (Animator 없음 또는 Hit Trigger 비움): 살짝 커졌다 돌아오는 움찔 효과
            if (punchRoutine != null) StopCoroutine(punchRoutine);
            punchRoutine = StartCoroutine(PunchScale());
        }
    }

    private IEnumerator PunchScale()
    {
        const float duration = 0.15f;
        Vector3 big = originalScale * (1f + punchScale);
        float t = 0f;
        while (t < duration)
        {
            t += Time.deltaTime;
            float k = Mathf.Sin(Mathf.Clamp01(t / duration) * Mathf.PI); // 0 → 1 → 0
            transform.localScale = Vector3.Lerp(originalScale, big, k);
            yield return null;
        }
        transform.localScale = originalScale;
        punchRoutine = null;
    }

    // 공격 속성과 방어(몹) 속성을 비교하여 배율 반환
    public static float GetTypeMultiplier(MagicElement attackType, MagicElement defendType)
    {
        if (attackType == MagicElement.Normal || defendType == MagicElement.Normal || attackType == defendType)
            return 1.0f;

        // 강점 (물->불, 불->풀, 풀->물)
        if (attackType == MagicElement.Water && defendType == MagicElement.Fire) return 1.5f;
        if (attackType == MagicElement.Fire && defendType == MagicElement.Grass) return 1.5f;
        if (attackType == MagicElement.Grass && defendType == MagicElement.Water) return 1.5f;

        // 약점 (물->풀, 불->물, 풀->불)
        return 0.5f;
    }

    // =========================================================
    // 사망
    // =========================================================
    private void Die()
    {
        IsDying = true;
        aliveEnemies.Remove(this); // 다른 마법이 더 이상 노리지 않도록

        // 추가 피격 방지 (3D 콜라이더 전부 끄기)
        foreach (Collider c in colliders)
        {
            if (c != null) c.enabled = false;
        }

        CancelAttack(); // 공격 중이었다면 제자리로

        // 남아 있는 피격 트리거가 사망 애니메이션을 방해하지 않도록 초기화
        if (anim != null && !string.IsNullOrEmpty(hitTrigger))
        {
            anim.ResetTrigger(hitTrigger);
        }

        if (HasAnimation && !string.IsNullOrEmpty(dieTrigger))
        {
            anim.SetTrigger(dieTrigger);
        }

        // 움찔 효과 중이었다면 멈추고 원래 크기로
        if (punchRoutine != null)
        {
            StopCoroutine(punchRoutine);
            punchRoutine = null;
            transform.localScale = originalScale;
        }

        // 사망 이펙트 (선택)
        if (deathEffectPrefab != null)
        {
            EffectUtil.SpawnAndAutoDestroy(deathEffectPrefab, AimPoint, Quaternion.identity);
        }

        // 처치 자막
        if (BattleUIManager.Instance != null)
        {
            string hex = ColorUtility.ToHtmlStringRGB(ElementColor(enemyType));
            BattleUIManager.Instance.ShowKill(
                $"{RankToKorean(rank)} <color=#{hex}>{ElementToKorean(enemyType)} 속성</color> 몬스터 처치!");
        }

        // 처치 카운트 + 드랍 여부 결정 (여기서 미리 정해두고, 애니메이션이 끝나면 생성)
        SpellData dropSpell = null;
        if (GameSessionManager.Instance != null)
        {
            dropSpell = GameSessionManager.Instance.RegisterKill(this);
        }

        MonsterCollection.AddKill(KeyOf(this)); // 몹 도감 처치 수
        OnDied?.Invoke(this);
        StartCoroutine(DeathSequence(dropSpell));
    }

    private IEnumerator DeathSequence(SpellData dropSpell)
    {
        yield return WaitForDeathAnimation();

        // 스펠 드랍
        if (dropSpell != null && dropItemPrefab != null)
        {
            DropItem drop = Instantiate(dropItemPrefab, transform.position + dropOffset, Quaternion.identity);
            drop.Initialize(dropSpell, GroundPoint); // 바닥 높이 기준
        }

        // 애니메이션 없는 몹은 이미 작아져서 안 보이므로 바로 삭제
        if (HasAnimation || !shrinkOnDeath)
        {
            yield return new WaitForSeconds(destroyDelayAfterDeath);

            // 쓰러진 자세 그대로 작아지면서 사라짐 (툭 끊기듯 사라지지 않게)
            if (HasAnimation && despawnShrinkDuration > 0f)
            {
                Vector3 from = transform.localScale;
                float t = 0f;
                while (t < despawnShrinkDuration)
                {
                    t += Time.deltaTime;
                    float k = Mathf.Clamp01(t / despawnShrinkDuration);
                    transform.localScale = Vector3.Lerp(from, Vector3.zero, k * k);
                    yield return null;
                }
            }
        }
        OnDespawned?.Invoke(this);
        Destroy(gameObject);
    }

    // SetTrigger 직후에는 아직 이전 State이므로, Die State에 들어갈 때까지 기다린 뒤 길이를 계산
    private IEnumerator WaitForDeathAnimation()
    {
        // 애니메이션 없는 몹
        if (!HasAnimation)
        {
            if (shrinkOnDeath)
            {
                // 작아지면서 사라짐
                float t = 0f;
                while (t < shrinkDuration)
                {
                    t += Time.deltaTime;
                    float k = Mathf.Clamp01(t / shrinkDuration);
                    transform.localScale = Vector3.Lerp(originalScale, Vector3.zero, k * k); // 처음엔 천천히, 끝에 빠르게
                    yield return null;
                }
                transform.localScale = Vector3.zero;
            }
            else
            {
                yield return new WaitForSeconds(deathFallbackDuration);
            }
            yield break;
        }

        // 1. Die State 진입 대기 (최대 1초)
        float timer = 0f;
        while (!IsInDeathState() && timer < 1f)
        {
            timer += Time.deltaTime;
            yield return null;
        }

        if (!IsInDeathState())
        {
            Debug.LogWarning($"[{gameObject.name}] '{deathStateName}' State를 찾지 못해 기본 대기 시간을 사용합니다.");
            yield return new WaitForSeconds(deathFallbackDuration);
            yield break;
        }

        // 2. Die 애니메이션이 끝까지 재생될 때까지 대기
        //    (반복 재생 클립이면 끝나는 순간 다시 처음 자세로 돌아가므로, 끝나기 직전에 멈춤)
        float safety = 0f;
        while (safety < 10f)
        {
            AnimatorStateInfo info = anim.GetCurrentAnimatorStateInfo(0);
            float oneFrame = info.length > 0f ? (Time.deltaTime * 1.5f) / info.length : 0f;
            if (!anim.IsInTransition(0) && IsInDeathState() && info.normalizedTime >= 1f - oneFrame) break;
            safety += Time.deltaTime;
            yield return null;
        }

        // 3. 마지막 자세로 고정
        if (freezeOnDeathEnd) anim.speed = 0f;
    }

    private bool IsInDeathState()
    {
        AnimatorStateInfo info = anim.GetCurrentAnimatorStateInfo(0);
        return info.IsName(deathStateName) || info.IsTag(deathStateName);
    }
}