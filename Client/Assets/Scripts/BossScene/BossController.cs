using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

// 보스 마법 유형: 투사체(날아와서 폭발) / 장판(지팡이 빛이 번쩍 → 플레이어 발밑에 장판)
public enum BossAttackType { Projectile, Ground }

// 공격 동작 하나 = 클립 + 마법 유형 (이펙트는 유형별 목록에서 매번 랜덤)
[System.Serializable]
public class BossAttackMove
{
    public AnimationClip clip;
    public BossAttackType type = BossAttackType.Projectile;
    [Tooltip("동작의 몇 % 지점에서 마법이 나갈지 (지팡이를 내지르거나 휘두르는 순간)")]
    [Range(0f, 1f)] public float castAt = 0.4f;
    [Tooltip("폭발·장판 이펙트 크기 배율 (Big·Floor처럼 큰 마법은 1.6~2)")]
    public float effectScale = 1f;
    [Tooltip("기본 공격력에 곱할 값 (큰 마법은 1.3~1.5)")]
    public float damageMultiplier = 1f;
}

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
    [Tooltip("(Attack Moves가 비어 있을 때만) 공격 동작 시작 후 마법이 나가는 시점 (초)")]
    public float castDelay = 0.6f;
    public float spellSpeed = 22f;
    [Tooltip("투사체 크기 배율")]
    public float spellScale = 1f;
    [Tooltip("차지 공격을 투사체로 쏠 때 투사체 크기 배율")]
    public float chargeSpellScale = 2.2f;

    [Header("공격 동작 → 마법 유형 (매번 이 중에서 랜덤)")]
    [Tooltip("예: Attack_1H_1·3·4 = Projectile / Attack_1H_2 = Ground / Attack_1H_Big = Projectile(폭발 크게) / Attack_1H_Floor = Ground(장판 크게)")]
    public BossAttackMove[] attackMoves;

    [Header("마법 이펙트 (유형에 맞는 목록에서 매번 랜덤)")]
    [Tooltip("투사체 (날아오는 마법). 플레이어 마법의 투사체 프리팹을 넣어도 됨. 비우면 속성 색 빛 구슬")]
    public GameObject[] projectilePrefabs;
    [Tooltip("투사체가 닿을 때 터지는 폭발")]
    public GameObject[] explosionPrefabs;
    [Tooltip("장판 (플레이어 발밑에 깔리는 마법)")]
    public GameObject[] groundPrefabs;
    [Tooltip("투사체가 카메라 앞 몇 m에서 터질지 (가까우면 폭발이 화면을 가림)")]
    public float impactDistance = 1.6f;

    [Header("장판 마법")]
    [Tooltip("지팡이 끝의 Point Light (번쩍임). 비우면 Cast Point·지팡이 아래에서 자동으로 찾음")]
    public Light castLight;
    [Tooltip("번쩍일 때 밝기")]
    public float flashIntensity = 60f;
    [Tooltip("번쩍임이 사그라드는 시간 (초)")]
    public float flashTime = 0.45f;
    [Tooltip("번쩍일 때 현재 속성 색으로")]
    public bool flashElementColor = true;
    [Tooltip("장판이 깔리는 위치 (플레이어 앞 몇 m). 0이면 발밑 바로 아래라 화면에 안 보일 수 있음")]
    public float groundForward = 1.5f;
    [Tooltip("장판 크기 배율 (모든 장판에 공통)")]
    public float groundScale = 1f;
    [Tooltip("장판이 나타난 뒤 데미지가 들어가기까지 (초)")]
    public float groundHitDelay = 0.35f;
    [Tooltip("데미지가 들어가는 순간 장판 위에서 폭발 (Explosion Prefabs에서 랜덤)")]
    public bool groundExplosion = true;
    [Tooltip("장판 폭발이 바닥에서 떠 있는 높이")]
    public float groundExplosionHeight = 0.6f;
    [Tooltip("투사체가 플레이어 앞에서 터지는 폭발을 보여 주는 최대 시간 (초). 오래 남는 에셋도 여기서 끊어 UI를 가리지 않게")]
    public float explosionMaxTime = 0.9f;
    [Tooltip("폭발이 끝난 뒤 남은 입자가 사라지는 최대 시간 (초)")]
    public float explosionTail = 0.25f;
    [Tooltip("장판을 보여 주는 전체 시간 (초). 끝나면 장판을 지우고 다음 턴으로 (에셋이 길어도 여기서 끊음)")]
    public float groundEffectTime = 1.4f;
    [Tooltip("장판이 끝난 뒤 남은 입자가 사라지는 최대 시간 (초)")]
    public float groundEffectTail = 0.3f;

    [Header("차지 공격 (Casting_1 땅에 꽂기)")]
    public BossAttackType chargeAttackType = BossAttackType.Ground;
    [Tooltip("차지 공격의 폭발·장판 크기 배율")]
    public float chargeEffectScale = 2.2f;

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

    [Header("발밑 마법진 (페이즈 속성마다 바뀜)")]
    [Tooltip("물 속성 페이즈 마법진 (파랑)")]
    public GameObject waterCircle;
    [Tooltip("불 속성 페이즈 마법진 (빨강)")]
    public GameObject fireCircle;
    [Tooltip("풀 속성 페이즈 마법진 (초록)")]
    public GameObject grassCircle;
    [Tooltip("(선택) 노말 속성 페이즈 마법진")]
    public GameObject normalCircle;
    [Tooltip("보스 발밑 기준 위치 보정 (바닥에 묻히면 Y를 0.02~0.05 정도)")]
    public Vector3 circleOffset = new Vector3(0f, 0.03f, 0f);
    [Tooltip("마법진 회전 보정. 마법진이 세워져 있으면 X 90 또는 -90")]
    public Vector3 circleRotation = Vector3.zero;
    public float circleScale = 1f;
    [Tooltip("한 번 재생하고 사라지는 에셋이어도 계속 반복되게")]
    public bool circleForceLoop = true;
    [Tooltip("왕좌에 앉아 있을 때는 숨기고, 내려와서 전투 위치에 선 뒤부터 보이기")]
    public bool circleAfterDescend = true;
    [Tooltip("맞아서 몸이 뒤로 밀리는 동작 등에서 마법진이 골반(Hips) 위치를 따라감 (Humanoid만)")]
    public bool circleFollowHips = true;

    [Header("페이즈 전환 아우라 (속성이 바뀌는 순간 발밑에서 터짐)")]
    [Tooltip("물 속성으로 바뀔 때")]
    public GameObject waterPowerUpEffect;
    [Tooltip("불 속성으로 바뀔 때")]
    public GameObject firePowerUpEffect;
    [Tooltip("풀 속성으로 바뀔 때")]
    public GameObject grassPowerUpEffect;
    [Tooltip("(선택) 위 칸이 비어 있을 때 쓸 공용 아우라")]
    public GameObject powerUpEffect;
    public float powerUpEffectScale = 1f;
    [Tooltip("파워업 동안 발밑 마법진을 끄고 아우라만 보여 줌 → 끝나면 새 속성 마법진")]
    public bool hideCircleDuringPowerUp = true;
    [Tooltip("새 속성 마법진이 나타날 때 잠깐 커졌다 원래대로 (1 = 연출 없음)")]
    public float circlePopScale = 1.6f;
    [Tooltip("커졌다 돌아오는 시간 (초)")]
    public float circlePopTime = 0.7f;
    [Tooltip("공용 아우라의 파티클 색을 속성 색으로 바꾸기")]
    public bool tintPowerUpEffect = true;

    [Header("차지 자세로 버티는 동안 (숨 쉬듯 커졌다 작아짐)")]
    [Tooltip("커지는 정도 (0.04 = 4%)")]
    [Range(0f, 0.2f)] public float chargeBreath = 0.04f;
    [Tooltip("1초에 커졌다 작아지는 횟수")]
    public float chargeBreathSpeed = 1.3f;

    [Header("애니메이션 클립 (Mixamo)")]
    public AnimationClip idleClip;
    [Tooltip("(선택) 등장 전 왕좌에 앉아 있는 자세 (반복)")]
    public AnimationClip sitClip;
    [Tooltip("(선택) 앉아 있다가 일어나는 동작")]
    public AnimationClip standUpClip;
    [Tooltip("(예전 방식) Attack Moves가 비어 있을 때만 사용. 전부 투사체로 쏨")]
    public AnimationClip[] attackClips;
    [Tooltip("(선택) 2연속 공격 전용 동작. 비우면 Attack Moves의 투사체 동작 중 랜덤")]
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
    [Tooltip("마왕 동작(공격·차지·소환·피격 등) 재생 속도. 1 = 원래 속도. 마법이 나가는 시점도 같이 빨라짐 (Idle·앉기는 그대로)")]
    [Range(0.5f, 3f)] public float actionSpeed = 1.4f;
    [Tooltip("공격 마법이 플레이어에게 닿은 뒤 남은 동작을 끊고 Idle로 돌아가는 시간 (0 = 동작을 끝까지 재생)")]
    public float returnToIdleFade = 0.35f;

    [Header("피격·기절 동작의 발걸음 이동 (Root Motion)")]
    [Tooltip("피격·기절 동작에서 실제로 발을 옮긴 만큼 마왕 위치도 같이 움직임 → 마법진이 따라가고 일어날 때 순간이동 없음\n" +
             "※ 해당 클립들의 Root Transform Position (XZ) → Bake Into Pose 는 '체크 해제'")]
    public bool hitRootMotion = true;
    [Tooltip("전투 자리에서 최대 이만큼(m)까지만 밀려남")]
    public float maxDrift = 1.5f;
    [Tooltip("Idle로 돌아온 뒤 전투 자리로 천천히 되돌아가는 속도 (m/초). 0 = 밀린 자리에 그대로")]
    public float driftReturnSpeed = 0.35f;

    [Header("기절 (상성 마법 연속 적중)  Hit_Big → Standing To Crouched → Stunned (반복) → Crouch To Stand")]
    [Tooltip("Hit_Big 다음 서 있다가 쭈그리는 동작. 예: Standing To Crouched (비우면 바로 웅크림)")]
    public AnimationClip stunEnterClip;
    [Tooltip("Hit_Big의 몇 % 지점에서 쭈그리기로 넘어갈지 (1 = 끝까지 본 뒤). 맞고 휘청이는 끝부분과 겹쳐야 자연스러움")]
    [Range(0.3f, 1f)] public float hitBigBlendAt = 0.8f;
    [Tooltip("Hit_Big → 쭈그리기로 섞이는 시간 (초)")]
    public float stunEnterFade = 0.3f;
    [Tooltip("쭈그리기 → 웅크린 반복 동작으로 섞이는 시간 (초)")]
    public float stunIdleFade = 0.3f;
    [Tooltip("(선택) 웅크리고 있는 동작 (반복). 비우면 Standing To Crouched 마지막 자세에서 멈춰 있음")]
    public AnimationClip stunIdleClip;
    [Tooltip("쭈그린 자세(Standing To Crouched 끝)에서 멈춰 별을 보여 주는 시간 (초)")]
    public float stunHoldTime = 3.5f;
    [Tooltip("켜면 위 시간이 지나면 스스로 일어남 (Crouch To Stand → Idle). 끄면 마왕의 쉬는 턴이 끝날 때 일어남")]
    public bool stunAutoRecover = true;
    [Tooltip("기절에서 깨어나 일어서는 동작. 예: Crouch To Stand")]
    public AnimationClip stunRecoverClip;
    [Tooltip("(선택) 머리 위 이펙트 프리팹. 비우면 노란 별이 빙빙 도는 기본 이펙트")]
    public GameObject stunEffectPrefab;
    [Tooltip("머리 위 이펙트 프리팹 크기")]
    public float stunEffectScale = 1f;
    [Tooltip("머리 위 이펙트 회전 보정 (원이 세워져 있으면 X 90 등으로 눕히기)")]
    public Vector3 stunEffectRotation = Vector3.zero;
    [Tooltip("한 번 재생하고 끝나는 에셋이어도 기절 동안 계속 반복")]
    public bool stunEffectLoop = true;
    [Tooltip("머리 위 이펙트 재생 속도 (0.5 = 절반 속도로 천천히 돎)")]
    [Range(0.05f, 2f)] public float stunEffectSpeed = 0.5f;
    [Tooltip("이펙트 프리팹이 있어도 코드로 만든 별도 같이 보여 주기")]
    public bool starsWithEffect = true;
    [Tooltip("별 크기·회전 반경을 마왕 키에 맞춰 키움 (마왕 키 2m 기준). 큰 마왕에서 별이 머리 속에 묻혀 안 보이던 문제")]
    public bool starsScaleWithBoss = true;
    [Tooltip("머리 위로 얼마나 띄울지")]
    public float starHeight = 0.35f;
    [Tooltip("별이 도는 원의 반지름")]
    public float starRadius = 0.35f;
    public int starCount = 4;
    public float starSize = 0.2f;
    [Tooltip("1초에 도는 각도")]
    public float starSpinSpeed = 90f;
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
            // 피격 동작의 발걸음 이동을 받아 오기 (Animator가 붙은 오브젝트에서만 받을 수 있음)
            BossRootMotionRelay relay = animator.GetComponent<BossRootMotionRelay>();
            if (relay == null) relay = animator.gameObject.AddComponent<BossRootMotionRelay>();
            relay.Init(this, animator);
        }
        basePos = transform.position;
    }

    private void Start()
    {
        BossSpell.ImpactMaxTime = explosionMaxTime;
        BossSpell.ImpactTail = explosionTail;
        Enemy.ResetHP(maxHP);
        bodyScale = transform.localScale;
        circleAllowed = !circleAfterDescend;
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
        ReturnDrift();

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

    // 피격·기절 동작일 때만 발걸음 이동을 마왕 위치에 반영 (공격·Idle 등은 제자리)
    internal void ApplyRootMotion(Vector3 delta)
    {
        if (!hitRootMotion || seated || holdPose || player == null || Enemy == null || Enemy.IsDying) return;
        AnimationClip c = player.Current;
        bool moving = IsHitClip(c) || c == stunEnterClip || c == stunIdleClip || c == stunRecoverClip;
        if (!moving) return;
        delta.y = 0f;
        Vector3 next = transform.position + delta;
        Vector3 off = next - basePos; off.y = 0f;
        if (off.magnitude > maxDrift) next = new Vector3(basePos.x, next.y, basePos.z) + off.normalized * maxDrift;
        transform.position = next;
    }

    // Idle로 돌아온 뒤 전투 자리로 천천히 복귀
    private void ReturnDrift()
    {
        if (!hitRootMotion || driftReturnSpeed <= 0f || seated || holdPose || IsStunned || recoilRoutine != null) return;
        if (player == null || player.Current != idleClip || Enemy == null || Enemy.IsDying) return;
        Vector3 p = transform.position;
        Vector3 target = new Vector3(basePos.x, p.y, basePos.z);
        if ((p - target).sqrMagnitude < 0.0001f) return;
        transform.position = Vector3.MoveTowards(p, target, driftReturnSpeed * Time.deltaTime);
    }

    // 동작 재생 속도 (반복 클립은 원래 속도)
    private float Speed => Mathf.Max(0.1f, actionSpeed);

    private void Play(AnimationClip clip, float fade = -1f)
    {
        if (player == null || clip == null) return;
        player.Play(clip, fade < 0f ? crossFade : fade);
        player.SetSpeed(clip.isLooping ? 1f : Speed);
    }

    // 재생하고 실제로 걸리는 시간(초)을 돌려줌
    private float PlayLength(AnimationClip clip, float fallback)
    {
        if (clip == null) return fallback;
        Play(clip);
        return clip.isLooping ? clip.length : clip.length / Speed;
    }

    // 공격이 끝나면 남은 동작(뒷마무리)은 짧게 끊고 Idle로 → 플레이어 턴에 바로 피격 반응
    private void EaseToIdle()
    {
        if (returnToIdleFade <= 0f || player == null || idleClip == null) return;
        if (holdPose || IsStunned || chargeHeld || Enemy == null || Enemy.IsDying) return;
        if (player.Current == idleClip) return;
        Play(idleClip, returnToIdleFade);
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

    // ---------- 발밑 마법진 ----------
    private GameObject currentCircle;
    private bool circleAllowed;

    private GameObject CirclePrefab(MagicElement e)
    {
        switch (e)
        {
            case MagicElement.Water: return waterCircle;
            case MagicElement.Fire: return fireCircle;
            case MagicElement.Grass: return grassCircle;
            default: return normalCircle;
        }
    }

    // 현재 속성의 마법진으로 바꿈 (예전 마법진은 서서히 사라짐)
    private void RefreshCircle()
    {
        RemoveCircle();
        if (!circleAllowed) return;
        GameObject prefab = CirclePrefab(Element);
        if (prefab == null) return;

        currentCircle = Instantiate(prefab, transform);
        currentCircle.transform.localPosition = circleOffset;
        CaptureHipsRest();
        currentCircle.transform.localRotation = Quaternion.Euler(circleRotation);
        if (!Mathf.Approximately(circleScale, 1f)) EffectUtil.Scale(currentCircle, circleScale);
        foreach (ParticleSystem ps in currentCircle.GetComponentsInChildren<ParticleSystem>(true))
        {
            ParticleSystem.MainModule main = ps.main;
            if (circleForceLoop) main.loop = true;
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;   // 커졌다 작아지는 연출에 파티클도 같이
            main.stopAction = ParticleSystemStopAction.None;   // 저절로 지워지지 않게
            ps.Play(true);
        }
    }

    private Transform hipsBone;
    private Vector3 hipsRestLocal;
    private bool hasHipsRest;

    // 지금(서 있는 자세) 골반 위치를 기준으로 기억 → 피격 동작에서 골반이 밀린 만큼만 마법진을 옮김
    private void CaptureHipsRest()
    {
        if (!circleFollowHips) return;
        if (hipsBone == null && animatorRef != null && animatorRef.isHuman) hipsBone = animatorRef.GetBoneTransform(HumanBodyBones.Hips);
        if (hipsBone == null) return;
        hipsRestLocal = transform.InverseTransformPoint(hipsBone.position);
        hasHipsRest = true;
    }

    private void UpdateCircleFollow()
    {
        if (currentCircle == null || !circleFollowHips || !hasHipsRest || hipsBone == null) return;
        Vector3 d = transform.InverseTransformPoint(hipsBone.position) - hipsRestLocal;
        currentCircle.transform.localPosition = circleOffset + new Vector3(d.x, 0f, d.z);
    }

    // 마법진이 확 커졌다가 원래 크기로 자리 잡음
    private IEnumerator PopCircle(Transform circle)
    {
        Vector3 full = circle.localScale;
        for (float t = 0f; t < circlePopTime && circle != null; t += Time.deltaTime)
        {
            float k = t / circlePopTime;
            // 처음 25%에 최대로 커진 뒤 부드럽게 줄어듦
            float s = k < 0.25f ? Mathf.Lerp(0.6f, circlePopScale, Mathf.SmoothStep(0f, 1f, k / 0.25f))
                                : Mathf.Lerp(circlePopScale, 1f, Mathf.SmoothStep(0f, 1f, (k - 0.25f) / 0.75f));
            circle.localScale = full * s;
            yield return null;
        }
        if (circle != null) circle.localScale = full;
    }

    private void RemoveCircle()
    {
        if (currentCircle == null) return;
        GameObject old = currentCircle;
        currentCircle = null;
        float tail = EffectUtil.StopEmitting(old);
        Destroy(old, tail + 0.1f);
    }

    // 마법진 보이기/숨기기 (BossBattleDirector에서 내려온 뒤 호출해도 됨)
    public void ShowCircle(bool show)
    {
        if (show && circleAllowed && currentCircle != null) return;   // 이미 켜져 있음
        circleAllowed = show;
        if (show) RefreshCircle(); else RemoveCircle();
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
        RefreshCircle();
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

    // 지금 몸(골반)이 있는 바닥 위치. 피격 동작으로 몸이 밀려 있으면 그만큼 옮긴 위치
    public Vector3 BodyFloorPoint
    {
        get
        {
            if (hipsBone == null && animatorRef != null && animatorRef.isHuman) hipsBone = animatorRef.GetBoneTransform(HumanBodyBones.Hips);
            if (hipsBone == null || !hasHipsRest) return transform.position;
            Vector3 d = transform.InverseTransformPoint(hipsBone.position) - hipsRestLocal;
            return transform.TransformPoint(new Vector3(d.x, 0f, d.z));
        }
    }

    // 피격 동작(Hit / Hit_Big)이 재생 중이면 끝나고 Idle로 돌아올 때까지 기다림 (최대 maxWait초)
    public IEnumerator WaitHitSettled(float maxWait = 2f)
    {
        float waited = 0f;
        while (waited < maxWait && player != null && IsHitClip(player.Current))
        {
            waited += Time.deltaTime;
            yield return null;
        }
        if (waited > 0f) yield return new WaitForSeconds(crossFade);   // Idle로 섞이는 시간
    }

    private bool IsHitClip(AnimationClip clip)
    {
        if (clip == null) return false;
        if (clip == hitBigClip) return true;
        if (hitClips != null) foreach (AnimationClip c in hitClips) if (c == clip) return true;
        return false;
    }

    // 마왕 키 (m, 메시 크기 기준). 카메라 연출 거리 계산용
    public float BodyHeight
    {
        get
        {
            bool has = false;
            Bounds b = new Bounds(transform.position, Vector3.zero);
            foreach (Renderer r in GetComponentsInChildren<Renderer>())
            {
                if (r is ParticleSystemRenderer || !r.enabled) continue;
                if (currentCircle != null && r.transform.IsChildOf(currentCircle.transform)) continue;
                if (!has) { b = r.bounds; has = true; } else b.Encapsulate(r.bounds);
            }
            return has ? b.size.y : 2f;
        }
    }

    // 속성이 바뀌는 순간 발밑 아우라
    private void SpawnPowerUpAura()
    {
        GameObject prefab = null;
        switch (Element)
        {
            case MagicElement.Water: prefab = waterPowerUpEffect; break;
            case MagicElement.Fire: prefab = firePowerUpEffect; break;
            case MagicElement.Grass: prefab = grassPowerUpEffect; break;
        }
        bool generic = prefab == null;
        if (generic) prefab = powerUpEffect;
        if (prefab == null) return;

        GameObject fx = Instantiate(prefab, BodyFloorPoint + Vector3.up * circleOffset.y, Quaternion.identity);
        EffectUtil.Scale(fx, powerUpEffectScale);
        if (generic && tintPowerUpEffect)
        {
            Color c = CurrentColor;
            foreach (ParticleSystem ps in fx.GetComponentsInChildren<ParticleSystem>(true))
            {
                ParticleSystem.MainModule main = ps.main;
                main.startColor = c;
            }
        }
        EffectUtil.PlayOnce(fx);
    }

    // 페이즈 전환 연출 (기 모으기 동작 + 오라 번쩍 + 속성 변경). 걸린 시간만큼 기다리는 코루틴
    public IEnumerator DoPowerUp(int newPhase)
    {
        CancelCharge();
        yield return WaitHitSettled();   // 맞고 휘청이는 중이면 자세가 돌아온 뒤 시작 (마법진·아우라가 어긋나지 않게)
        bool hadCircle = circleAllowed;
        if (hideCircleDuringPowerUp) ShowCircle(false);   // 예전 속성 마법진은 서서히 꺼짐
        float length = PlayLength(powerUpClip, 1.6f);
        yield return new WaitForSeconds(Mathf.Min(length * 0.5f, 1.2f));
        SetPhase(newPhase, true);   // (마법진이 꺼져 있으면 새로 만들지 않음)
        SpawnPowerUpAura();
        yield return new WaitForSeconds(Mathf.Max(0.6f, length * 0.5f));
        if (hideCircleDuringPowerUp && hadCircle && !Enemy.IsDying)
        {
            ShowCircle(true);   // 새 속성 마법진
            if (currentCircle != null && circlePopScale > 1f) StartCoroutine(PopCircle(currentCircle.transform));
        }
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
            float length = standUpClip.length / Speed;
            float before = length * staffAppearAt;
            yield return new WaitForSeconds(before);
            StartCoroutine(ShowStaff());
            yield return new WaitForSeconds(length - before);
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
        ShowCircle(true);   // 전투 자리에 내려선 순간 발밑 마법진

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

    // 기본 공격: 동작을 랜덤으로 골라 그 동작의 유형(투사체/장판)에 맞는 마법
    public IEnumerator DoBasicAttack()
    {
        BossAttackMove move = PickMove(null);
        Play(move.clip);
        yield return Telegraph(CastTime(move), 0.04f);
        float damage = BasicDamage * Mathf.Max(0f, move.damageMultiplier);
        if (move.type == BossAttackType.Ground)
        {
            yield return GroundStrike(damage, move.effectScale);
        }
        else
        {
            BossSpell spell = FireProjectile(damage, spellScale, move.effectScale);
            yield return WaitSpell(spell);
        }
        EaseToIdle();
    }

    // 2연속: 투사체 두 발
    public IEnumerator DoDoubleAttack()
    {
        BossAttackMove move = PickMove(BossAttackType.Projectile);
        float first;
        if (doubleAttackClip != null) { Play(doubleAttackClip); first = doubleCastDelay / Speed; }
        else { Play(move.clip); first = CastTime(move); }
        yield return Telegraph(first, 0.05f);
        float damage = BasicDamage * Mathf.Max(0f, move.damageMultiplier);
        BossSpell a = FireProjectile(damage, spellScale, move.effectScale);
        yield return new WaitForSeconds(Mathf.Max(0.55f, doubleCastGap / Speed)); // 무적 시간(0.5초)보다 길게
        BossSpell b = FireProjectile(damage, spellScale, move.effectScale);
        yield return WaitSpell(a);
        yield return WaitSpell(b);
        EaseToIdle();
    }

    // 동작 고르기 (type을 주면 그 유형 중에서). Attack Moves가 비어 있으면 예전 Attack Clips를 투사체로
    private BossAttackMove PickMove(BossAttackType? only)
    {
        List<BossAttackMove> pool = new List<BossAttackMove>();
        if (attackMoves != null)
        {
            foreach (BossAttackMove m in attackMoves)
            {
                if (m == null || m.clip == null) continue;
                if (only.HasValue && m.type != only.Value) continue;
                pool.Add(m);
            }
        }
        if (pool.Count == 0 && attackClips != null)
        {
            foreach (AnimationClip c in attackClips)
                if (c != null) pool.Add(new BossAttackMove { clip = c, type = BossAttackType.Projectile, castAt = -1f });
        }
        if (pool.Count == 0) return new BossAttackMove { clip = null, type = only ?? BossAttackType.Projectile, castAt = -1f };
        return pool[Random.Range(0, pool.Count)];
    }

    // 동작 시작부터 마법이 나가기까지 (실제 초)
    private float CastTime(BossAttackMove move)
    {
        if (move == null || move.clip == null || move.castAt < 0f) return castDelay / Speed;
        return move.clip.length * move.castAt / Speed;
    }

    private static GameObject PickRandom(GameObject[] list)
    {
        if (list == null || list.Length == 0) return null;
        List<GameObject> valid = new List<GameObject>();
        foreach (GameObject g in list) if (g != null) valid.Add(g);
        return valid.Count > 0 ? valid[Random.Range(0, valid.Count)] : null;
    }

    // 투사체: 지팡이 끝 → 카메라 앞에서 폭발 (투사체·폭발 모두 목록에서 랜덤)
    private BossSpell FireProjectile(float damage, float visualScale, float impactScale, float arc = 0.8f)
    {
        return BossSpell.Launch(PickRandom(projectilePrefabs), CastPosition, CurrentColor, visualScale, spellSpeed, damage,
                                AttackReason, PickRandom(explosionPrefabs), impactScale, arc, impactDistance);
    }

    // 장판: 지팡이 빛이 번쩍 → 플레이어 앞 바닥에 장판 → 잠시 뒤 데미지
    private IEnumerator GroundStrike(float damage, float scale)
    {
        StartCoroutine(FlashCastLight());
        GameObject prefab = PickRandom(groundPrefabs);
        GameObject area = null;
        Vector3 ground = PlayerGroundPoint();
        if (prefab != null)
        {
            Camera cam = Camera.main;
            Quaternion rot = Quaternion.identity;
            if (cam != null)
            {
                Vector3 f = cam.transform.forward; f.y = 0f;
                if (f.sqrMagnitude > 0.001f) rot = Quaternion.LookRotation(-f.normalized);   // 플레이어 쪽을 향하게
            }
            area = Instantiate(prefab, ground, rot);
            EffectUtil.Scale(area, groundScale * scale);
        }
        float shown = 0f;
        yield return new WaitForSeconds(groundHitDelay);
        shown += groundHitDelay;
        if (Enemy.IsDying) { EndGround(area); yield break; }
        if (groundExplosion)
        {
            GameObject boom = PickRandom(explosionPrefabs);
            if (boom != null) EffectUtil.SpawnScaled(boom, ground + Vector3.up * groundExplosionHeight, Quaternion.identity, groundScale * scale);
        }
        if (PlayerHealth.Instance != null) PlayerHealth.Instance.TakeDamage(damage, AttackReason);

        // 장판을 Ground Effect Time까지 보여 준 뒤 지우고 → 그다음에 턴 넘김 (에셋이 길어도 "Your turn!"과 겹치지 않게)
        float rest = Mathf.Max(0.3f, groundEffectTime - shown);
        yield return new WaitForSeconds(rest);
        EndGround(area);
        yield return new WaitForSeconds(Mathf.Min(groundEffectTail, 0.3f));
    }

    private void EndGround(GameObject area)
    {
        if (area == null) return;
        float tail = EffectUtil.StopEmitting(area);
        Destroy(area, Mathf.Min(tail, groundEffectTail) + 0.05f);
    }

    // 플레이어(카메라) 앞 바닥 위치. 바닥 콜라이더가 있으면 그 높이, 없으면 마왕 발 높이
    private Vector3 PlayerGroundPoint()
    {
        Camera cam = Camera.main;
        if (cam == null) return transform.position;
        Vector3 f = cam.transform.forward; f.y = 0f;
        f = f.sqrMagnitude > 0.001f ? f.normalized : Vector3.forward;
        Vector3 p = cam.transform.position + f * groundForward;
        float y = transform.position.y;
        if (Physics.Raycast(p + Vector3.up * 0.5f, Vector3.down, out RaycastHit hit, 6f, ~0, QueryTriggerInteraction.Ignore)) y = hit.point.y;
        return new Vector3(p.x, y + 0.02f, p.z);
    }

    // 지팡이 끝 Point Light 번쩍
    private float castLightBase = -1f;
    private Color castLightColor;
    private IEnumerator FlashCastLight()
    {
        if (castLight == null)
        {
            if (castPoint != null) castLight = castPoint.GetComponentInChildren<Light>(true);
            if (castLight == null && staff != null) castLight = staff.GetComponentInChildren<Light>(true);
        }
        if (castLight == null) yield break;
        if (castLightBase < 0f) { castLightBase = castLight.intensity; castLightColor = castLight.color; }
        bool wasEnabled = castLight.enabled;
        castLight.enabled = true;
        if (flashElementColor) castLight.color = CurrentColor;
        for (float t = 0f; t < 0.08f; t += Time.deltaTime)
        {
            castLight.intensity = Mathf.Lerp(castLightBase, flashIntensity, t / 0.08f);
            yield return null;
        }
        for (float t = 0f; t < flashTime; t += Time.deltaTime)
        {
            float k = t / Mathf.Max(0.01f, flashTime);
            castLight.intensity = Mathf.Lerp(flashIntensity, castLightBase, k * k);
            yield return null;
        }
        castLight.intensity = castLightBase;
        castLight.color = castLightColor;
        castLight.enabled = wasEnabled;
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
        float growTime = chargeClip != null ? Mathf.Max(0.3f, chargeClip.length * chargeHoldAt / Speed) : 1.2f;
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
            player.SetSpeed(Speed);
            float wait = Mathf.Max(0.05f, (chargeSlamAt - chargeHoldAt) * chargeClip.length / Speed);
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
            yield return Telegraph(castDelay / Speed, 0.08f);
        }
        DestroyCharge();
        IsCharging = false;
        if (chargeAttackType == BossAttackType.Ground)
        {
            yield return GroundStrike(chargeDamage, chargeEffectScale);
        }
        else
        {
            BossSpell spell = FireProjectile(chargeDamage, chargeSpellScale, chargeEffectScale, 0.4f);
            yield return WaitSpell(spell);
        }
        EaseToIdle();
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
        EaseToIdle();
    }

    private Vector3 CastPosition => castPoint != null ? castPoint.position : Enemy.AimPoint + Vector3.up * 0.4f;

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
    public bool IsRecovering { get; private set; }   // 기절에서 일어나는 중 (Crouch To Stand)
    private GameObject stunFx;     // 머리 위 이펙트 프리팹 (Stun Effect Prefab)
    private GameObject starFx;     // 코드로 만든 별 (부모)
    private float starScaleMul = 1f;
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
            // 끝까지 기다리지 않고 휘청이는 끝부분에서 바로 쭈그리기로 섞음
            float left = (hitBigClip.length * hitBigBlendAt - (player != null ? player.Elapsed : 0f)) / Speed;
            if (left > 0f) yield return new WaitForSeconds(left);
        }
        if (!IsStunned || Enemy.IsDying) yield break;

        // 서 있다가 쭈그림 (Standing To Crouched)
        if (stunEnterClip != null)
        {
            Play(stunEnterClip, stunEnterFade);
            if (stunIdleClip != null)
            {
                float enter = stunEnterClip.length / Speed - stunIdleFade;
                if (enter > 0f) yield return new WaitForSeconds(enter);
                if (!IsStunned || Enemy.IsDying) yield break;
                Play(stunIdleClip, stunIdleFade);
            }
            else
            {
                // 마지막 자세(쭈그려 기절한 모습)에서 그대로 멈춤
                yield return new WaitForSeconds(stunEnterClip.length * 0.97f / Speed);
                if (!IsStunned || Enemy.IsDying) yield break;
                if (player != null && player.Current == stunEnterClip) player.SetSpeed(0f);
            }
        }
        else
        {
            Play(stunIdleClip != null ? stunIdleClip : idleClip, stunIdleFade);
        }

        // 멈춘 채 별이 도는 모습을 잠시 보여 준 뒤 스스로 일어남 (Crouch To Stand → Idle)
        if (stunAutoRecover)
        {
            yield return new WaitForSeconds(stunHoldTime);
            if (IsStunned && !Enemy.IsDying) yield return EndStun();
        }
    }

    // 기절에서 깨어남 (BossBattleDirector가 쉬는 턴 끝에 호출)
    public IEnumerator EndStun()
    {
        if (!IsStunned) yield break;
        IsStunned = false;
        IsRecovering = true;
        ShowStars(false);
        if (Enemy.IsDying) { IsRecovering = false; yield break; }
        if (stunRecoverClip != null)
        {
            Play(stunRecoverClip, 0.25f);
            yield return new WaitForSeconds(Mathf.Max(0.1f, stunRecoverClip.length / Speed - crossFade));
        }
        Play(idleClip);
        IsRecovering = false;
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
            if (starFx != null) Destroy(starFx);
            starFx = null;
            stars.Clear();
            return;
        }
        if (stunFx != null || starFx != null) return;

        if (headBone == null && animatorRef != null && animatorRef.isHuman) headBone = animatorRef.GetBoneTransform(HumanBodyBones.Head);
        starScaleMul = starsScaleWithBoss ? Mathf.Max(1f, BodyHeight / 2f) : 1f;
        if (stunEffectPrefab != null)
        {
            stunFx = Instantiate(stunEffectPrefab, StarCenter, Quaternion.Euler(stunEffectRotation));
            EffectUtil.Scale(stunFx, stunEffectScale);
            foreach (ParticleSystem ps in stunFx.GetComponentsInChildren<ParticleSystem>(true))
            {
                ParticleSystem.MainModule main = ps.main;
                if (stunEffectLoop) main.loop = true;
                main.stopAction = ParticleSystemStopAction.None;   // 기절이 끝날 때까지 저절로 지워지지 않게
                main.simulationSpeed = stunEffectSpeed;             // 천천히 돌게
                ps.Play(true);
            }
            foreach (Animator a in stunFx.GetComponentsInChildren<Animator>(true)) a.speed = stunEffectSpeed;
            if (!starsWithEffect) return;
        }

        starFx = new GameObject("StunStars");
        starFx.transform.position = StarCenter;
        Sprite sprite = StarSprite();
        Shader unlit = Shader.Find("Universal Render Pipeline/2D/Sprite-Unlit-Default");
        Material mat = unlit != null ? new Material(unlit) : null;
        for (int i = 0; i < Mathf.Max(1, starCount); i++)
        {
            GameObject star = new GameObject("Star");
            star.transform.SetParent(starFx.transform, false);
            star.transform.localScale = Vector3.one * starSize * starScaleMul;
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
            if (headBone != null) return headBone.position + Vector3.up * starHeight * starScaleMul;
            return Enemy != null ? Enemy.AimPoint + Vector3.up * 1.2f : transform.position + Vector3.up * 2f;
        }
    }

    // 애니메이션이 적용된 뒤 머리 위치를 따라감
    private Vector3 bodyScale = Vector3.one;
    private float breathWeight;
    private bool breathApplied;

    private void LateUpdate()
    {
        UpdateCircleFollow();

        // 차지 자세로 멈춰 있는 동안 숨 쉬듯 커졌다 작아짐
        breathWeight = Mathf.MoveTowards(breathWeight, chargeHeld && !Enemy.IsDying ? 1f : 0f, Time.deltaTime * 3f);
        if (breathWeight > 0f)
        {
            float b = (Mathf.Sin(Time.time * chargeBreathSpeed * Mathf.PI * 2f) + 1f) * 0.5f;
            transform.localScale = bodyScale * (1f + chargeBreath * b * breathWeight);
            if (chargeObject != null) chargeObject.transform.localScale = Vector3.one * chargeOrbSize * (1f + chargeBreath * 2.5f * b * breathWeight);
            breathApplied = true;
        }
        else if (breathApplied)
        {
            transform.localScale = bodyScale;   // 버티기가 끝나면 원래 크기로 (한 번만)
            breathApplied = false;
        }

        if (stunFx != null) stunFx.transform.position = StarCenter;
        if (starFx == null) return;
        starFx.transform.position = StarCenter;
        if (stars.Count == 0) return;

        Camera cam = Camera.main;
        float baseAngle = Time.time * starSpinSpeed;
        for (int i = 0; i < stars.Count; i++)
        {
            float a = (baseAngle + 360f / stars.Count * i) * Mathf.Deg2Rad;
            Vector3 offset = new Vector3(Mathf.Cos(a) * starRadius * starScaleMul, Mathf.Sin(a * 2f + i) * 0.04f, Mathf.Sin(a) * starRadius * starScaleMul);
            stars[i].position = starFx.transform.position + offset;
            // 카메라를 바라보며 제자리에서도 살짝 회전 / 뒤쪽으로 갈 때 작아져 원근감
            Quaternion face = cam != null ? cam.transform.rotation : Quaternion.identity;
            stars[i].rotation = face * Quaternion.Euler(0f, 0f, Time.time * 160f + i * 40f);
            float depth = cam != null ? Vector3.Dot(offset, -cam.transform.forward) / Mathf.Max(0.01f, starRadius * starScaleMul) : 0f;
            stars[i].localScale = Vector3.one * starSize * starScaleMul * (0.85f + 0.15f * depth);
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
        // 맞으면 지금 동작을 바로 끊고 피격 동작 (공격 뒷마무리 중이어도 즉시 반응)
        // 예외: 기절해 웅크린 동안 / 차지 자세로 버티는 중 보통 피격 (상성 피격은 차지가 끊기므로 재생)
        bool keepPose = IsStunned || holdPose || (chargeHeld && !big);
        if (clip != null && !keepPose) Play(clip, 0.08f);

        // 피격 동작이 직접 발을 옮기면 코드로 밀어내는 반동은 생략
        if (hitRootMotion && clip != null && !keepPose) return;
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
        ShowCircle(false);
        if (deathClip != null)
        {
            Play(deathClip, 0.15f);
            yield return new WaitForSeconds(deathClip.length / Speed);
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

// 마왕 Animator가 붙은 오브젝트에 자동으로 붙음: 동작의 발걸음 이동(Root Motion)을 BossController에 넘김
public class BossRootMotionRelay : MonoBehaviour
{
    private BossController boss;
    private Animator animator;

    public void Init(BossController owner, Animator anim)
    {
        boss = owner;
        animator = anim;
    }

    // 이 함수가 있으면 Unity가 Root Motion을 직접 적용하지 않고 여기로 넘김
    private void OnAnimatorMove()
    {
        if (boss != null && animator != null) boss.ApplyRootMotion(animator.deltaPosition);
    }
}