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
