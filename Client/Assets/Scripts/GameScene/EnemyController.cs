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
            // 스폰포인트에 도착하지 않았거나 쓰러지는 중인 몹은 타겟이 될 수 없음
            if (enemy == null || enemy.IsDying || !enemy.IsReady) continue;
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
                if (enemy != null && !enemy.IsDying && enemy.IsReady) return true;
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
    [Tooltip("Death 애니메이션이 끝난 뒤 사라지기까지의 여유 시간")]
    public float destroyDelayAfterDeath = 0.2f;

    [Header("애니메이션 없는 몹용 연출 (Animator가 없을 때 자동 적용)")]
    [Tooltip("사망 시 작아지면서 사라짐. 끄면 Death Fallback Duration만큼 서 있다가 사라짐")]
    public bool shrinkOnDeath = true;
    [Tooltip("작아져서 사라지는 데 걸리는 시간(초)")]
    public float shrinkDuration = 0.4f;
    [Tooltip("피격 시 살짝 움찔하는 효과")]
    public bool punchOnHit = true;
    [Tooltip("움찔하는 크기 (0.15 = 15% 커졌다 돌아옴)")]
    public float punchScale = 0.15f;

    [Header("사망 이펙트 (선택)")]
    [Tooltip("사망 순간 몸 중앙에 생성될 이펙트 (연기·빛 등). 모든 몹에 사용 가능, 비워도 됨")]
    public GameObject deathEffectPrefab;

    [Header("드랍")]
    public DropItem dropItemPrefab;
    [Tooltip("드랍 아이템 생성 위치 보정 (발밑 기준)")]
    public Vector3 dropOffset = new Vector3(0f, 1f, 0f);

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
    public Vector3 GroundPoint => transform.position;

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

        if (useRankDefaultHP) maxHP = DefaultHP(rank);
        CurrentHP = maxHP;
    }

    private void OnEnable() => aliveEnemies.Add(this);
    private void OnDisable() => aliveEnemies.Remove(this);

    // =========================================================
    // 등장 이동
    // =========================================================
    /// <summary>
    /// 경로(waypoints)를 따라 이동한 뒤, 마지막 지점에서 lookAt 쪽으로 몸을 돌리고 공격 가능 상태가 됩니다.
    /// </summary>
    public void BeginApproach(IList<Vector3> waypoints, Vector3 lookAt)
    {
        if (waypoints == null || waypoints.Count == 0)
        {
            MarkReady();
            return;
        }
        IsReady = false;
        StartCoroutine(ApproachRoutine(new List<Vector3>(waypoints), lookAt));
    }

    private IEnumerator ApproachRoutine(List<Vector3> waypoints, Vector3 lookAt)
    {
        SetMoveAnimation(true);

        foreach (Vector3 point in waypoints)
        {
            while ((transform.position - point).sqrMagnitude > 0.0025f) // 5cm 이내면 도착
            {
                // 진행 방향으로 몸 돌리기 (위아래 기울기 없이)
                Vector3 dir = point - transform.position;
                dir.y = 0f;
                if (dir.sqrMagnitude > 0.0001f)
                {
                    Quaternion targetRot = Quaternion.LookRotation(dir);
                    transform.rotation = Quaternion.RotateTowards(transform.rotation, targetRot, turnSpeed * Time.deltaTime);
                }

                transform.position = Vector3.MoveTowards(transform.position, point, moveSpeed * Time.deltaTime);
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
            Quaternion targetRot = Quaternion.LookRotation(face);
            float timer = 0f;
            while (Quaternion.Angle(transform.rotation, targetRot) > 1f && timer < 1.5f)
            {
                transform.rotation = Quaternion.RotateTowards(transform.rotation, targetRot, turnSpeed * Time.deltaTime);
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
    public void ReceiveSpell(SpellData spell, float delay)
    {
        if (IsDying || spell == null) return;

        if (delay <= 0f) TakeDamage(spell);
        else StartCoroutine(DelayedDamage(spell, delay));
    }

    private IEnumerator DelayedDamage(SpellData spell, float delay)
    {
        yield return new WaitForSeconds(delay);
        TakeDamage(spell);
    }

    public void TakeDamage(SpellData spell)
    {
        if (IsDying || spell == null) return;

        // 1. 상성 배율 계산
        float multiplier = GetTypeMultiplier(spell.elementType, enemyType);

        // 2. 최종 데미지 적용
        float finalDamage = spell.baseDamage * multiplier;
        CurrentHP -= finalDamage;

        Debug.Log($"[{gameObject.name}] {spell.spellName} 적중! 데미지: {finalDamage} (배율: {multiplier}x) 남은 HP: {CurrentHP}");

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
        else if (!HasAnimation && punchOnHit)
        {
            // 애니메이션 없는 몹: 살짝 커졌다 돌아오는 움찔 효과
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
            BattleUIManager.Instance.ShowKill($"{RankToKorean(rank)} {ElementToKorean(enemyType)} 속성 몬스터 처치!");
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
            drop.Initialize(dropSpell);
        }

        // 애니메이션 없는 몹은 이미 작아져서 안 보이므로 바로 삭제
        if (HasAnimation || !shrinkOnDeath)
        {
            yield return new WaitForSeconds(destroyDelayAfterDeath);
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
        float safety = 0f;
        while (safety < 10f)
        {
            AnimatorStateInfo info = anim.GetCurrentAnimatorStateInfo(0);
            if (!anim.IsInTransition(0) && IsInDeathState() && info.normalizedTime >= 1f) break;
            safety += Time.deltaTime;
            yield return null;
        }
    }

    private bool IsInDeathState()
    {
        AnimatorStateInfo info = anim.GetCurrentAnimatorStateInfo(0);
        return info.IsName(deathStateName) || info.IsTag(deathStateName);
    }
}