using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

// 보스 본체 (보스 모델 루트에 EnemyController와 함께 붙이기)
//   - 체력·페이즈(체력 비율로 1→2→3)·페이즈별 속성 변화
//   - 공격 동작 + 마법 발사(BossSpell), 차지, 소환, 페이즈 전환, 피격, 사망 연출
//   - 애니메이션: Animator Controller를 만들 필요 없이, 아래 칸에 Mixamo 클립만 넣으면 코드로 재생
//     (Animator 컴포넌트와 Avatar는 필요. Controller 칸은 비워 둬도 됨)
//   - 언제 무엇을 할지는 BossBattleDirector가 정함
[RequireComponent(typeof(EnemyController))]
public class BossController : MonoBehaviour
{
    [Header("기본")]
    public string bossName = "Demon Lord of Silence";
    public float maxHP = 800f;

    [Header("페이즈 (체력 비율 경계)")]
    [Range(0f, 1f)] public float phase2At = 0.66f;
    [Range(0f, 1f)] public float phase3At = 0.33f;
    [Tooltip("페이즈 1·2·3의 보스 속성 (약점 = 이 속성을 이기는 속성)")]
    public MagicElement[] phaseElements = { MagicElement.Water, MagicElement.Fire, MagicElement.Grass };

    [Header("공격력 (페이즈 1·2·3)")]
    public float[] basicDamage = { 6f, 8f, 10f };
    public float chargeDamage = 15f;

    [Header("마법 발사")]
    [Tooltip("마법이 나갈 위치 (손 뼈 아래 빈 오브젝트 추천). 비우면 몸 중앙 위")]
    public Transform castPoint;
    [Tooltip("공격 동작 시작 후 마법이 손을 떠나는 시점 (초)")]
    public float castDelay = 0.6f;
    public float spellSpeed = 14f;
    [Tooltip("(선택) 속성별 마법 비주얼 [노말, 물, 불, 풀]. 플레이어 마법의 투사체 프리팹을 넣어도 됨. 비우면 빛나는 구슬")]
    public GameObject[] spellPrefabs = new GameObject[4];
    [Tooltip("(선택) 마법이 플레이어에게 닿을 때 터질 이펙트")]
    public GameObject impactPrefab;
    public float spellScale = 1f;
    public float chargeSpellScale = 2.2f;

    [Header("지팡이 (일어난 뒤 손에 나타남)")]
    [Tooltip("보스 오른손 뼈 아래에 붙여 둔 지팡이 오브젝트. 앉아 있는 동안 숨겼다가 일어나면 나타남")]
    public GameObject staff;
    [Tooltip("지팡이가 커지며 나타나는 시간")]
    public float staffAppearTime = 0.5f;
    [Tooltip("(선택) 지팡이가 나타날 때 터지는 이펙트 (빛·연기 등)")]
    public GameObject staffAppearEffect;
    [Tooltip("Sit To Stand 동작의 몇 % 지점에서 지팡이가 나타날지 (1 = 다 일어난 뒤)")]
    [Range(0f, 1f)] public float staffAppearAt = 0.85f;

    [Header("일어난 뒤 왕좌에서 내려오기")]
    [Tooltip("싸울 자리 (왕좌 계단 아래 빈 오브젝트, 보스 '발' 위치). 비우면 왕좌 앞에 선 채로 싸움")]
    public Transform battlePoint;
    [Tooltip("내려오는 데 걸리는 시간 (초)")]
    public float descendTime = 2.2f;
    [Tooltip("내려오는 동안 공중에 떠오르는 높이 (0 = 바닥을 미끄러지듯)")]
    public float descendHover = 0.6f;
    [Tooltip("(선택) 내려오는 동안 반복할 동작 (걷기·떠다니기 등). 비우면 Idle 자세 그대로 떠서 내려옴")]
    public AnimationClip descendClip;
    [Tooltip("(선택) 떠오를 때 / 내려앉을 때 발밑에서 터질 이펙트 (연기·빛 등)")]
    public GameObject descendEffect;

    [Header("차지 (힘 모으기)")]
    [Tooltip("(선택) 손에 맺히는 이펙트. 비우면 커지는 빛 구슬")]
    public GameObject chargePrefab;
    public float chargeOrbSize = 0.9f;
    [Tooltip("Charge 클립(Casting_1)의 몇 % 지점에서 멈춰 '두 팔을 든 채' 버틸지. 다음 보스 턴까지 이 자세 유지")]
    [Range(0f, 1f)] public float chargeHoldAt = 0.45f;
    [Tooltip("멈췄던 동작을 다시 재생해서 몇 % 지점에서 마법이 나갈지 (지팡이를 땅에 꽂는 순간)")]
    [Range(0f, 1f)] public float chargeSlamAt = 0.75f;
    [Tooltip("(선택) 땅에 꽂는 순간 보스 발밑에서 터질 이펙트 (충격파·먼지 등)")]
    public GameObject chargeSlamEffect;

    [Header("연속 공격 (Attack_1H_Big: 한 바퀴 돌며 휘두르기)")]
    [Tooltip("동작 시작 후 첫 번째 마법이 나가는 시점 (초)")]
    public float doubleCastDelay = 0.5f;
    [Tooltip("두 번째 마법까지의 간격 (초). 플레이어 무적 시간(0.5초)보다 길게")]
    public float doubleCastGap = 0.65f;

    [Header("오라 (페이즈 속성 색)")]
    [Tooltip("보스 뒤·발밑의 Point Light (선택)")]
    public Light auraLight;
    public float auraIntensity = 3f;
    [Tooltip("(선택) 몸 주위 파티클. Start Color가 속성 색으로 바뀜")]
    public ParticleSystem[] auraParticles;

    [Header("애니메이션 클립 (Mixamo)")]
    public AnimationClip idleClip;
    [Tooltip("(선택) 등장 전 왕좌에 앉아 있는 자세 (반복)")]
    public AnimationClip sitClip;
    [Tooltip("(선택) 앉아 있다가 일어나는 동작")]
    public AnimationClip standUpClip;
    [Tooltip("기본 공격 (여러 개 넣으면 매번 랜덤)")]
    public AnimationClip[] attackClips;
    public AnimationClip doubleAttackClip;
    public AnimationClip chargeClip;
    public AnimationClip chargeReleaseClip;
    public AnimationClip summonClip;
    public AnimationClip powerUpClip;
    [Tooltip("보통 피격 (여러 개면 랜덤)")]
    public AnimationClip[] hitClips;
    [Tooltip("상성 피격 / 차지가 끊겼을 때")]
    public AnimationClip hitBigClip;
    public AnimationClip deathClip;
    public float crossFade = 0.2f;

    [Header("기절 (상성 마법 연속 적중)")]
    [Tooltip("Hit_Big 다음 웅크리고 있는 동작 (반복). 예: Crouch_Idle")]
    public AnimationClip stunIdleClip;
    [Tooltip("기절에서 깨어나 일어서는 동작. 예: Crouch To Stand")]
    public AnimationClip stunRecoverClip;
    [Tooltip("(선택) 머리 위 이펙트 프리팹. 비우면 노란 별이 빙빙 도는 기본 이펙트")]
    public GameObject stunEffectPrefab;
    [Tooltip("머리 위로 얼마나 띄울지")]
    public float starHeight = 0.35f;
    [Tooltip("별이 도는 원의 반지름")]
    public float starRadius = 0.35f;
    public int starCount = 4;
    public float starSize = 0.2f;
    [Tooltip("1초에 도는 각도")]
    public float starSpinSpeed = 200f;
    public Color starColor = new Color(1f, 0.88f, 0.3f);

    [Header("코드 연출 (애니메이션이 없어도 동작)")]
    [Tooltip("Idle 클립이 없을 때 위아래로 떠다니는 정도")]
    public float floatAmount = 0.08f;
    [Tooltip("맞았을 때 뒤로 밀리는 거리")]
    public float hitRecoil = 0.25f;

    public EnemyController Enemy { get; private set; }
    public int Phase { get; private set; } = 1;
    public bool IsCharging { get; private set; }
    public bool HasBattlePoint => battlePoint != null;
    public float HPRatio => Enemy != null ? Mathf.Clamp01(Enemy.CurrentHP / Mathf.Max(1f, maxHP)) : 0f;
    public MagicElement Element => Enemy != null ? Enemy.enemyType : MagicElement.Normal;

    private ClipPlayer player;
    private GameObject chargeObject;
    private Vector3 basePos;
    private Coroutine recoilRoutine;
    private bool holdPose;   // 사망 등 마지막 자세 유지
    private bool seated;
    private bool chargeHeld;
    private Animator animatorRef; // Casting_1을 팔 든 자세에서 멈춰 둔 상태

    // =========================================================
    // 준비
    // =========================================================
    private void Awake()
    {
        Enemy = GetComponent<EnemyController>();
        // 공격·사망 처리는 이 스크립트와 BossBattleDirector가 담당
        Enemy.canAttack = false;
        Enemy.punchOnHit = false;
        Enemy.shrinkOnDeath = false;
        Enemy.hitTrigger = "";
        Enemy.showKillText = false;
        Enemy.deathFallbackDuration = 3f;
        Enemy.destroyDelayAfterDeath = 9999f; // 쓰러진 채로 남아 있게 (씬이 바뀔 때까지)
        Enemy.OnDamaged += HandleDamaged;

        Animator animator = Enemy.anim != null ? Enemy.anim : GetComponentInChildren<Animator>();
        if (animator != null)
        {
            animator.applyRootMotion = false;
            animator.runtimeAnimatorController = null; // 클립은 코드로 재생
            animatorRef = animator;
            player = new ClipPlayer(animator, name);
        }
        basePos = transform.position;
    }

    private void Start()
    {
        Enemy.ResetHP(maxHP);
        SetPhase(1, false);
        if (staff != null) staffScale = staff.transform.localScale;
        if (sitClip != null)
        {
            seated = true;
            Play(sitClip, 0f);
            if (staff != null) staff.SetActive(false); // 앉아 있을 땐 지팡이 없음
        }
        else Play(idleClip, 0f);
    }

    private void OnDestroy()
    {
        if (Enemy != null) Enemy.OnDamaged -= HandleDamaged;
        player?.Destroy();
    }

    private void Update()
    {
        player?.Tick(Time.deltaTime);

        // 한 번 재생하는 동작이 끝나면 Idle로
        if (player != null && !holdPose && !seated && !IsStunned && player.Current != null && player.Current != idleClip
            && !player.Current.isLooping && player.Elapsed >= player.Current.length - crossFade)
        {
            Play(idleClip);
        }

        // Idle 클립이 없으면 둥실둥실
        if (idleClip == null && !holdPose && recoilRoutine == null && Enemy != null && !Enemy.IsDying)
        {
            transform.position = basePos + Vector3.up * Mathf.Sin(Time.time * 1.6f) * floatAmount;
        }
    }

    private void Play(AnimationClip clip, float fade = -1f)
    {
        if (player == null || clip == null) return;
        player.Play(clip, fade < 0f ? crossFade : fade);
    }

    private float PlayLength(AnimationClip clip, float fallback)
    {
        if (clip == null) return fallback;
        Play(clip);
        return clip.length;
    }

    // =========================================================
    // 페이즈
    // =========================================================
    public int PhaseForHP()
    {
        float r = HPRatio;
        if (r <= phase3At) return 3;
        if (r <= phase2At) return 2;
        return 1;
    }

    public static Color ElementColorOf(MagicElement e) => e == MagicElement.Normal ? new Color(0.6f, 0.25f, 0.8f) : EnemyController.ElementColor(e);

    public Color CurrentColor => ElementColorOf(Element);

    public void SetPhase(int phase, bool animate)
    {
        Phase = Mathf.Clamp(phase, 1, 3);
        if (phaseElements != null && phaseElements.Length >= Phase) Enemy.enemyType = phaseElements[Phase - 1];
        Color c = CurrentColor;
        if (auraLight != null)
        {
            auraLight.color = c;
            auraLight.intensity = auraIntensity;
        }
        if (auraParticles != null)
        {
            foreach (ParticleSystem ps in auraParticles)
            {
                if (ps == null) continue;
                ParticleSystem.MainModule main = ps.main;
                main.startColor = c;
            }
        }
        if (animate) StartCoroutine(AuraFlash());
    }

    private IEnumerator AuraFlash()
    {
        if (auraLight == null) yield break;
        float t = 0f;
        while (t < 1.2f)
        {
            t += Time.deltaTime;
            auraLight.intensity = auraIntensity * (1f + 3f * Mathf.Sin(Mathf.Clamp01(t / 1.2f) * Mathf.PI));
            yield return null;
        }
        auraLight.intensity = auraIntensity;
    }

    // 페이즈 전환 연출 (기 모으기 동작 + 오라 번쩍 + 속성 변경). 걸린 시간만큼 기다리는 코루틴
    public IEnumerator DoPowerUp(int newPhase)
    {
        CancelCharge();
        float length = PlayLength(powerUpClip, 1.6f);
        yield return new WaitForSeconds(Mathf.Min(length * 0.5f, 1.2f));
        SetPhase(newPhase, true);
        yield return new WaitForSeconds(Mathf.Max(0.6f, length * 0.5f));
    }

    // =========================================================
    // 등장
    // =========================================================
    public IEnumerator DoStandUp()
    {
        seated = false;
        if (standUpClip != null)
        {
            Play(standUpClip);
            float before = standUpClip.length * staffAppearAt;
            yield return new WaitForSeconds(before);
            StartCoroutine(ShowStaff());
            yield return new WaitForSeconds(standUpClip.length - before);
        }
        else
        {
            StartCoroutine(ShowStaff());
        }
        Play(idleClip);
    }

    // 왕좌에서 싸울 자리로 천천히 떠서 내려옴 (플레이어를 바라보는 방향은 유지)
    public IEnumerator DoDescend()
    {
        if (battlePoint == null) yield break;
        Vector3 from = transform.position;
        Vector3 to = battlePoint.position;
        Quaternion fromRot = transform.rotation;
        Quaternion toRot = fromRot;
        Camera cam = Camera.main;
        if (cam != null)
        {
            // 지금 플레이어를 보는 각도를 그대로 → 새 자리에서도 플레이어를 정면으로
            Vector3 a = cam.transform.position - from; a.y = 0f;
            Vector3 b = cam.transform.position - to; b.y = 0f;
            if (a.sqrMagnitude > 0.01f && b.sqrMagnitude > 0.01f) toRot = Quaternion.FromToRotation(a.normalized, b.normalized) * fromRot;
        }

        if (descendClip != null) Play(descendClip);
        if (descendEffect != null) EffectUtil.SpawnAndAutoDestroy(descendEffect, from, Quaternion.identity);

        float time = Mathf.Max(0.1f, descendTime);
        for (float t = 0f; t < time; t += Time.deltaTime)
        {
            float k = Mathf.SmoothStep(0f, 1f, t / time);
            transform.position = Vector3.Lerp(from, to, k) + Vector3.up * Mathf.Sin(k * Mathf.PI) * descendHover;
            transform.rotation = Quaternion.Slerp(fromRot, toRot, k);
            yield return null;
        }
        transform.position = to;
        transform.rotation = toRot;
        basePos = to;

        if (descendEffect != null) EffectUtil.SpawnAndAutoDestroy(descendEffect, to, Quaternion.identity);
        if (descendClip != null) Play(idleClip);
    }

    private Vector3 staffScale = Vector3.one;

    // 지팡이가 손에 맺히듯 커지며 나타남
    private IEnumerator ShowStaff()
    {
        if (staff == null || staff.activeSelf) yield break;
        staff.transform.localScale = Vector3.zero;
        staff.SetActive(true);
        if (staffAppearEffect != null)
        {
            EffectUtil.SpawnAndAutoDestroy(staffAppearEffect, staff.transform.position, Quaternion.identity);
        }
        for (float t = 0f; t < staffAppearTime; t += Time.deltaTime)
        {
            float x = Mathf.Clamp01(t / Mathf.Max(0.01f, staffAppearTime)) - 1f;
            float e = 1f + 2.2f * x * x * x + 1.2f * x * x; // 살짝 커졌다가 자리 잡음
            staff.transform.localScale = staffScale * e;
            yield return null;
        }
        staff.transform.localScale = staffScale;
    }

    // =========================================================
    // 공격 (BossBattleDirector가 호출, 마법이 플레이어에게 닿을 때까지 기다림)
    // =========================================================
    private float BasicDamage => basicDamage != null && basicDamage.Length >= Phase ? basicDamage[Phase - 1] : 8f;
    private string AttackReason => $"{bossName}'s magic";

    public IEnumerator DoBasicAttack()
    {
        AnimationClip clip = attackClips != null && attackClips.Length > 0 ? attackClips[Random.Range(0, attackClips.Length)] : null;
        Play(clip);
        yield return Telegraph(castDelay, 0.04f);
        BossSpell spell = FireSpell(BasicDamage, spellScale);
        yield return WaitSpell(spell);
    }

    public IEnumerator DoDoubleAttack()
    {
        Play(doubleAttackClip != null ? doubleAttackClip : (attackClips != null && attackClips.Length > 0 ? attackClips[0] : null));
        yield return Telegraph(doubleCastDelay, 0.05f);
        BossSpell a = FireSpell(BasicDamage, spellScale);
        yield return new WaitForSeconds(Mathf.Max(0.55f, doubleCastGap)); // 무적 시간(0.5초)보다 길게
        BossSpell b = FireSpell(BasicDamage, spellScale);
        yield return WaitSpell(a);
        yield return WaitSpell(b);
    }

    // 힘 모으기 시작 (이번 보스 턴은 여기서 끝, 다음 보스 턴에 발사)
    public IEnumerator BeginCharge()
    {
        IsCharging = true;
        chargeHeld = false;
        Play(chargeClip);
        Vector3 at = CastPosition;
        if (chargePrefab != null)
        {
            chargeObject = Instantiate(chargePrefab, at, Quaternion.identity, castPoint != null ? castPoint : transform);
        }
        else
        {
            chargeObject = CreateGlow(CurrentColor);
            chargeObject.transform.SetParent(castPoint != null ? castPoint : transform, true);
            chargeObject.transform.position = at;
        }
        // 팔을 들어 올리는 동안 구슬이 커짐 → 들어 올린 자세에서 동작을 멈춤
        float growTime = chargeClip != null ? Mathf.Max(0.3f, chargeClip.length * chargeHoldAt) : 1.2f;
        float t = 0f;
        while (t < growTime)
        {
            t += Time.deltaTime;
            if (chargeObject != null) chargeObject.transform.localScale = Vector3.one * chargeOrbSize * Mathf.SmoothStep(0.1f, 1f, t / growTime);
            yield return null;
        }
        if (chargeClip != null && chargeReleaseClip == null && player != null && player.Current == chargeClip && IsCharging)
        {
            player.SetSpeed(0f);
            chargeHeld = true;
        }
    }

    public IEnumerator ReleaseCharge()
    {
        if (chargeHeld && player != null && player.Current == chargeClip)
        {
            // 멈춰 둔 Casting_1을 이어서 재생 → 지팡이를 땅에 꽂는 순간 발사
            chargeHeld = false;
            player.SetSpeed(1f);
            float wait = Mathf.Max(0.05f, (chargeSlamAt - chargeHoldAt) * chargeClip.length);
            yield return Telegraph(wait, 0.08f);
            if (chargeSlamEffect != null)
            {
                EffectUtil.SpawnAndAutoDestroy(chargeSlamEffect, transform.position, Quaternion.identity);
            }
        }
        else
        {
            chargeHeld = false;
            Play(chargeReleaseClip != null ? chargeReleaseClip : chargeClip);
            yield return Telegraph(castDelay, 0.08f);
        }
        DestroyCharge();
        IsCharging = false;
        BossSpell spell = FireSpell(chargeDamage, chargeSpellScale, 0.4f);
        yield return WaitSpell(spell);
    }

    // 상성 마법에 맞아 차지가 끊김
    public void CancelCharge()
    {
        if (!IsCharging) return;
        IsCharging = false;
        DestroyCharge();
        // 팔을 든 채 멈춰 있었다면 풀어 줌 (상성 피격 동작이 없을 때도 굳어 있지 않게)
        if (chargeHeld)
        {
            chargeHeld = false;
            if (player != null && player.Current == chargeClip) Play(idleClip, 0.25f);
        }
    }

    private void DestroyCharge()
    {
        if (chargeObject == null) return;
        float tail = EffectUtil.StopEmitting(chargeObject);
        foreach (Renderer r in chargeObject.GetComponentsInChildren<Renderer>()) if (!(r is ParticleSystemRenderer)) r.enabled = false;
        foreach (Light l in chargeObject.GetComponentsInChildren<Light>()) l.enabled = false;
        Destroy(chargeObject, tail + 0.2f);
        chargeObject = null;
    }

    public IEnumerator DoSummon()
    {
        float length = PlayLength(summonClip, 1.2f);
        yield return new WaitForSeconds(Mathf.Min(length, 1.6f));
    }

    private Vector3 CastPosition => castPoint != null ? castPoint.position : Enemy.AimPoint + Vector3.up * 0.4f;

    private BossSpell FireSpell(float damage, float scale, float arc = 0.8f)
    {
        MagicElement e = Element;
        GameObject prefab = spellPrefabs != null && spellPrefabs.Length > (int)e ? spellPrefabs[(int)e] : null;
        return BossSpell.Launch(prefab, CastPosition, CurrentColor, scale, spellSpeed, damage, AttackReason, impactPrefab, arc);
    }

    private static IEnumerator WaitSpell(BossSpell spell)
    {
        float safety = 6f;
        while (spell != null && !spell.Done && safety > 0f)
        {
            safety -= Time.deltaTime;
            yield return null;
        }
    }

    // 마법이 나가기 전까지 살짝 떨림 (예고)
    private IEnumerator Telegraph(float duration, float shake)
    {
        Vector3 origin = transform.position;
        float t = 0f;
        while (t < duration)
        {
            t += Time.deltaTime;
            float k = t / Mathf.Max(0.01f, duration);
            transform.position = origin + new Vector3(Random.Range(-1f, 1f), 0f, Random.Range(-1f, 1f)) * shake * k;
            yield return null;
        }
        transform.position = origin;
    }

    // =========================================================
    // 피격 · 사망
    // =========================================================
    // =========================================================
    // 기절: Hit_Big → 웅크린 채 머리 위 별 → (다음 보스 턴) Crouch To Stand → Idle
    // =========================================================
    public bool IsStunned { get; private set; }
    private GameObject stunFx;
    private readonly List<Transform> stars = new List<Transform>();
    private Transform headBone;

    public void BeginStun()
    {
        if (IsStunned || Enemy == null || Enemy.IsDying) return;
        IsStunned = true;
        if (IsCharging) CancelCharge();
        StartCoroutine(StunRoutine());
    }

    private IEnumerator StunRoutine()
    {
        ShowStars(true);
        if (hitBigClip != null)
        {
            // 상성 피격으로 이미 Hit_Big이 나오고 있으면 이어서, 아니면 새로 재생
            if (player == null || player.Current != hitBigClip) Play(hitBigClip, 0.08f);
            float left = hitBigClip.length - (player != null ? player.Elapsed : 0f) - crossFade;
            if (left > 0f) yield return new WaitForSeconds(left);
        }
        if (!IsStunned || Enemy.IsDying) yield break;
        Play(stunIdleClip != null ? stunIdleClip : idleClip, 0.3f);
    }

    // 기절에서 깨어남 (BossBattleDirector가 쉬는 턴 끝에 호출)
    public IEnumerator EndStun()
    {
        if (!IsStunned) yield break;
        IsStunned = false;
        ShowStars(false);
        if (Enemy.IsDying) yield break;
        if (stunRecoverClip != null)
        {
            Play(stunRecoverClip, 0.25f);
            yield return new WaitForSeconds(Mathf.Max(0.1f, stunRecoverClip.length - crossFade));
        }
        Play(idleClip);
    }

    private void ShowStars(bool show)
    {
        if (!show)
        {
            if (stunFx != null)
            {
                float tail = EffectUtil.StopEmitting(stunFx);
                foreach (Renderer r in stunFx.GetComponentsInChildren<Renderer>()) if (!(r is ParticleSystemRenderer)) r.enabled = false;
                Destroy(stunFx, tail + 0.1f);
            }
            stunFx = null;
            stars.Clear();
            return;
        }
        if (stunFx != null) return;

        if (headBone == null && animatorRef != null && animatorRef.isHuman) headBone = animatorRef.GetBoneTransform(HumanBodyBones.Head);
        if (stunEffectPrefab != null)
        {
            stunFx = Instantiate(stunEffectPrefab, StarCenter, Quaternion.identity);
            return;
        }

        stunFx = new GameObject("StunStars");
        stunFx.transform.position = StarCenter;
        Sprite sprite = StarSprite();
        Shader unlit = Shader.Find("Universal Render Pipeline/2D/Sprite-Unlit-Default");
        Material mat = unlit != null ? new Material(unlit) : null;
        for (int i = 0; i < Mathf.Max(1, starCount); i++)
        {
            GameObject star = new GameObject("Star");
            star.transform.SetParent(stunFx.transform, false);
            star.transform.localScale = Vector3.one * starSize;
            SpriteRenderer sr = star.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            sr.color = starColor;
            if (mat != null) sr.sharedMaterial = mat;
            stars.Add(star.transform);
        }
    }

    private Vector3 StarCenter
    {
        get
        {
            if (headBone != null) return headBone.position + Vector3.up * starHeight;
            return Enemy != null ? Enemy.AimPoint + Vector3.up * 1.2f : transform.position + Vector3.up * 2f;
        }
    }

    // 애니메이션이 적용된 뒤 머리 위치를 따라감
    private void LateUpdate()
    {
        if (stunFx == null) return;
        stunFx.transform.position = StarCenter;
        if (stars.Count == 0) return;

        Camera cam = Camera.main;
        float baseAngle = Time.time * starSpinSpeed;
        for (int i = 0; i < stars.Count; i++)
        {
            float a = (baseAngle + 360f / stars.Count * i) * Mathf.Deg2Rad;
            Vector3 offset = new Vector3(Mathf.Cos(a) * starRadius, Mathf.Sin(a * 2f + i) * 0.04f, Mathf.Sin(a) * starRadius);
            stars[i].position = stunFx.transform.position + offset;
            // 카메라를 바라보며 제자리에서도 살짝 회전 / 뒤쪽으로 갈 때 작아져 원근감
            Quaternion face = cam != null ? cam.transform.rotation : Quaternion.identity;
            stars[i].rotation = face * Quaternion.Euler(0f, 0f, Time.time * 160f + i * 40f);
            float depth = cam != null ? Vector3.Dot(offset, -cam.transform.forward) / Mathf.Max(0.01f, starRadius) : 0f;
            stars[i].localScale = Vector3.one * starSize * (0.85f + 0.15f * depth);
        }
    }

    // 별 모양 스프라이트를 코드로 그림 (한 번만)
    private static Sprite starSprite;
    private static Sprite StarSprite()
    {
        if (starSprite != null) return starSprite;
        const int size = 64;
        Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        tex.wrapMode = TextureWrapMode.Clamp;
        Vector2[] poly = new Vector2[10];
        for (int k = 0; k < 10; k++)
        {
            float ang = Mathf.PI / 2f + k * Mathf.PI / 5f;
            float r = (k % 2 == 0) ? 0.46f : 0.2f;
            poly[k] = new Vector2(0.5f + Mathf.Cos(ang) * r, 0.5f + Mathf.Sin(ang) * r);
        }
        Color32[] px = new Color32[size * size];
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                // 4번 나눠 찍어 가장자리를 부드럽게
                int hit = 0;
                for (int sy = 0; sy < 2; sy++)
                    for (int sx = 0; sx < 2; sx++)
                        if (InPolygon(new Vector2((x + 0.25f + sx * 0.5f) / size, (y + 0.25f + sy * 0.5f) / size), poly)) hit++;
                float a = hit / 4f;
                // 가운데는 하얗게, 바깥은 은은한 빛
                float d = Vector2.Distance(new Vector2((x + 0.5f) / size, (y + 0.5f) / size), new Vector2(0.5f, 0.5f));
                float glow = Mathf.Clamp01(1f - d / 0.5f) * 0.35f;
                float alpha = Mathf.Max(a, glow * glow);
                float white = Mathf.Clamp01(1f - d / 0.18f) * a;
                byte c = (byte)(255 * (0.8f + 0.2f * white));
                px[y * size + x] = new Color32(255, c, c, (byte)(255 * alpha));
            }
        }
        tex.SetPixels32(px);
        tex.Apply();
        starSprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size);
        return starSprite;
    }

    private static bool InPolygon(Vector2 p, Vector2[] poly)
    {
        bool inside = false;
        for (int i = 0, j = poly.Length - 1; i < poly.Length; j = i++)
        {
            if ((poly[i].y > p.y) != (poly[j].y > p.y) &&
                p.x < (poly[j].x - poly[i].x) * (p.y - poly[i].y) / (poly[j].y - poly[i].y) + poly[i].x)
                inside = !inside;
        }
        return inside;
    }

    private void HandleDamaged(EnemyController e, SpellData spell, float damage, float multiplier)
    {
        if (Enemy.IsDying || Enemy.CurrentHP <= 0f) return;
        bool big = multiplier > 1f;
        AnimationClip clip = big && hitBigClip != null ? hitBigClip
            : (hitClips != null && hitClips.Length > 0 ? hitClips[Random.Range(0, hitClips.Length)] : null);
        // 공격·차지 동작 중에는 피격 동작으로 끊지 않음 (상성 피격은 예외)
        // 기절해 웅크린 동안은 자세를 유지 (BeginStun이 Hit_Big → 웅크리기를 직접 재생)
        if (clip != null && !IsStunned && (big || player == null || player.Current == idleClip)) Play(clip, 0.08f);

        if (recoilRoutine != null) StopCoroutine(recoilRoutine);
        recoilRoutine = StartCoroutine(Recoil(big ? hitRecoil * 1.8f : hitRecoil));
    }

    private IEnumerator Recoil(float distance)
    {
        Vector3 origin = idleClip == null ? basePos : transform.position;
        Vector3 back = -transform.forward * distance;
        float t = 0f;
        const float time = 0.35f;
        while (t < time)
        {
            t += Time.deltaTime;
            float k = Mathf.Sin(Mathf.Clamp01(t / time) * Mathf.PI);
            transform.position = origin + back * k;
            yield return null;
        }
        transform.position = origin;
        recoilRoutine = null;
    }

    public IEnumerator DoDeath()
    {
        holdPose = true;
        IsStunned = false;
        ShowStars(false);
        DestroyCharge();
        IsCharging = false;
        if (auraLight != null) StartCoroutine(FadeAura());
        if (deathClip != null)
        {
            Play(deathClip, 0.15f);
            yield return new WaitForSeconds(deathClip.length);
        }
        else
        {
            // 클립이 없으면 천천히 가라앉음
            Vector3 from = transform.position;
            float t = 0f;
            while (t < 2f)
            {
                t += Time.deltaTime;
                transform.position = from + Vector3.down * 1.2f * Mathf.SmoothStep(0f, 1f, t / 2f);
                yield return null;
            }
        }
    }

    private IEnumerator FadeAura()
    {
        float start = auraLight.intensity;
        float t = 0f;
        while (t < 2f)
        {
            t += Time.deltaTime;
            auraLight.intensity = Mathf.Lerp(start, 0f, t / 2f);
            yield return null;
        }
    }

    private static GameObject CreateGlow(Color color)
    {
        GameObject go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        go.name = "BossCharge";
        Destroy(go.GetComponent<Collider>());
        Shader shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
        Material m = shader != null ? new Material(shader) : go.GetComponent<Renderer>().material;
        m.color = Color.Lerp(color, Color.white, 0.3f);
        go.GetComponent<Renderer>().sharedMaterial = m;
        Light l = go.AddComponent<Light>();
        l.type = LightType.Point;
        l.color = color;
        l.range = 5f;
        l.intensity = 4f;
        go.transform.localScale = Vector3.one * 0.1f;
        return go;
    }

    // =========================================================
    // 클립 재생기 (Animator Controller 없이 두 클립을 섞어서 전환)
    // =========================================================
    private class ClipPlayer
    {
        private PlayableGraph graph;
        private AnimationMixerPlayable mixer;
        private float fade, fadeTime;
        private Playable currentPlayable;

        public AnimationClip Current { get; private set; }
        // 클립 재생 시간 (멈춰 둔 동안은 늘지 않음)
        public float Elapsed => currentPlayable.IsValid() ? (float)currentPlayable.GetTime() : 0f;

        // 지금 동작의 재생 속도 (0 = 그 자세로 멈춤, 1 = 보통)
        public void SetSpeed(float speed)
        {
            if (currentPlayable.IsValid()) currentPlayable.SetSpeed(speed);
        }

        public ClipPlayer(Animator animator, string name)
        {
            graph = PlayableGraph.Create(name + "_Boss");
            graph.SetTimeUpdateMode(DirectorUpdateMode.GameTime);
            AnimationPlayableOutput output = AnimationPlayableOutput.Create(graph, "Animation", animator);
            mixer = AnimationMixerPlayable.Create(graph, 2);
            output.SetSourcePlayable(mixer);
            graph.Play();
        }

        public void Play(AnimationClip clip, float crossFade)
        {
            if (!graph.IsValid() || clip == null) return;

            // 0번 = 지금 동작, 1번 = 사라지는 이전 동작
            Playable old = mixer.GetInput(1);
            if (old.IsValid())
            {
                graph.Disconnect(mixer, 1);
                old.Destroy();
            }
            Playable current = mixer.GetInput(0);
            if (current.IsValid())
            {
                float w = mixer.GetInputWeight(0);
                graph.Disconnect(mixer, 0);
                graph.Connect(current, 0, mixer, 1);
                mixer.SetInputWeight(1, w);
            }

            AnimationClipPlayable next = AnimationClipPlayable.Create(graph, clip);
            next.SetApplyFootIK(true);
            if (!clip.isLooping) next.SetDuration(clip.length);
            graph.Connect(next, 0, mixer, 0);

            fade = Mathf.Max(0f, crossFade);
            fadeTime = 0f;
            mixer.SetInputWeight(0, fade > 0f ? 0f : 1f);
            if (fade <= 0f) mixer.SetInputWeight(1, 0f);

            Current = clip;
            currentPlayable = next;
        }

        public void Tick(float dt)
        {
            if (!graph.IsValid() || fadeTime >= fade) return;
            fadeTime += dt;
            float w = Mathf.Clamp01(fadeTime / Mathf.Max(0.0001f, fade));
            mixer.SetInputWeight(0, w);
            if (mixer.GetInput(1).IsValid()) mixer.SetInputWeight(1, 1f - w);
        }

        public void Destroy()
        {
            if (graph.IsValid()) graph.Destroy();
        }
    }
}