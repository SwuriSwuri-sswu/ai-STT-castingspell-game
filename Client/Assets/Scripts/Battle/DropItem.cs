using System.Collections;
using UnityEngine;

// 몹이 드랍하는 스펠 두루마리 (3D)
// 연출: 몹 위치에서 포물선으로 '몹과 플레이어 사이 공중'에 날아가 멈춤
//      → 등급(tier)별 색깔 빛기둥 + 두루마리 둥실 애니메이션을 잠깐 보여줌
//      → 플레이어에게 빨려 들어옴 → 스펠 해금 + "OOO 스펠을 획득했습니다!"
//
// 프리팹 구성: [DropItem 프리팹 루트 (이 스크립트)]
//                └ StatScroll (두루마리 모델 + 둥실 애니메이션, 자식으로 넣기)
//              빛기둥(vfx_LootDrop)은 코드가 등급에 맞게 자동으로 붙임
public class DropItem : MonoBehaviour
{
    [Header("등급별 빛 (1단계 / 2단계 / 3단계)")]
    [Tooltip("0 = 1단계(파랑), 1 = 2단계(보라), 2 = 3단계(노랑).\n" +
             "비어 있는 칸은 0번 칸 프리팹을 사용합니다.")]
    public GameObject[] tierGlowPrefabs = new GameObject[3];
    [Tooltip("켜면 빛 프리팹의 파티클 색을 아래 색으로 덮어씁니다.\n" +
             "색을 바꾼 빛 프리팹을 따로 만들었다면 끄세요.")]
    public bool tintGlow = false;
    public Color[] tierColors =
    {
        new Color(0.30f, 0.65f, 1.00f), // 1단계 파랑
        new Color(0.70f, 0.35f, 1.00f), // 2단계 보라
        new Color(1.00f, 0.85f, 0.25f), // 3단계 노랑
    };
    [Tooltip("빛이 두루마리를 보는 동안 계속 나오도록 루프를 켭니다 (원본 vfx_LootDrop은 1초짜리 일회성)")]
    public bool loopGlow = true;
    [Tooltip("빛을 두루마리 기준 어디에 둘지. 기본: 두루마리 바로 아래 바닥")]
    public bool glowOnGround = true;
    [Tooltip("glowOnGround가 꺼져 있을 때 두루마리 기준 위치 보정")]
    public Vector3 glowOffset = Vector3.zero;

    [Header("떨어지는 위치")]
    [Tooltip("몹(0) ~ 플레이어(1) 사이 어디쯤에 떨어질지. 0.5 = 정중앙")]
    [Range(0f, 1f)] public float dropRatio = 0.5f;
    [Tooltip("바닥에서 얼마나 떠 있을지 (둥실 애니메이션이 바닥에 파묻히지 않을 만큼)")]
    public float hoverHeight = 1.2f;
    [Tooltip("바닥 높이를 찾을 때 검사할 레이어 (바닥 콜라이더가 있으면 그 높이 사용)")]
    public LayerMask groundMask = ~0;

    [Header("날아가서 멈추기")]
    [Tooltip("몹 위치 → 떨어지는 위치까지 걸리는 시간")]
    public float travelTime = 0.6f;
    [Tooltip("포물선 높이 (톡 튀어 오르는 느낌)")]
    public float arcHeight = 1.0f;

    [Header("감상 시간")]
    [Tooltip("공중에 멈춰서 둥실 애니메이션을 보여주는 시간")]
    public float admireTime = 1.2f;
    [Tooltip("두루마리가 플레이어(카메라) 쪽을 바라보게")]
    public bool faceTarget = true;

    [Header("빨려 들어오기")]
    public float flySpeed = 2f;
    [Tooltip("날아오면서 점점 빨라지는 정도 (초당 속도 증가량)")]
    public float flyAcceleration = 25f;
    [Tooltip("이 거리 안에 들어오면 획득")]
    public float pickupDistance = 0.6f;
    [Tooltip("날아오면서 작아지기 (카메라를 가리지 않게)")]
    public bool shrinkWhileFlying = true;

    [Header("날아올 대상")]
    [Tooltip("비우면 'Player' 태그 오브젝트 → 없으면 메인 카메라")]
    public Transform target;
    [Tooltip("대상 위치 보정 (카메라 정중앙보다 살짝 아래로 빨려 들어오게)")]
    public Vector3 targetOffset = new Vector3(0f, -0.3f, 0f);

    [Header("획득 연출 (선택)")]
    [Tooltip("획득 순간 생성할 이펙트 (반짝 터지는 이펙트 등)")]
    public GameObject collectEffectPrefab;
    public AudioClip collectSound;
    [Range(0f, 1f)] public float collectVolume = 1f;
    [Tooltip("획득 문구의 스펠 이름을 등급 색으로 칠하기")]
    public bool colorSpellNameInNotice = true;

    private SpellData spell;
    private Vector3 enemyFeet;
    private bool hasEnemyFeet;
    private GameObject glow;
    private bool collected;

    // EnemyController가 생성 직후 호출 (enemyFeet = 몹 발밑 위치 → 바닥 높이 기준)
    public void Initialize(SpellData spellData, Vector3 enemyFeetPosition)
    {
        spell = spellData;
        enemyFeet = enemyFeetPosition;
        hasEnemyFeet = true;
    }

    public void Initialize(SpellData spellData)
    {
        spell = spellData;
    }

    // 1단계 = 0번, 2단계 = 1번, 3단계 = 2번 (노말 0단계는 0번)
    private int TierIndex => spell != null ? Mathf.Clamp(spell.tier - 1, 0, 2) : 0;

    private void Start()
    {
        if (target == null)
        {
            GameObject player = GameObject.FindGameObjectWithTag("Player");
            if (player != null) target = player.transform;
            else if (Camera.main != null) target = Camera.main.transform;
        }

        // 예전 프리팹에 Rigidbody가 붙어 있어도 물리로 떨어지지 않게
        Rigidbody rb = GetComponent<Rigidbody>();
        if (rb != null)
        {
            rb.isKinematic = true;
            rb.useGravity = false;
        }

        StartCoroutine(Sequence());
    }

    private Vector3 TargetPosition => target != null ? target.position + targetOffset : transform.position;

    private IEnumerator Sequence()
    {
        // 1. 떨어질 위치 계산: 몹 ~ 플레이어 사이, 바닥에서 hoverHeight 만큼 위
        Vector3 start = transform.position;
        Vector3 landing = Vector3.Lerp(start, TargetPosition, dropRatio);
        float groundY = FindGroundY(landing);
        landing.y = groundY + hoverHeight;

        FaceTargetNow();

        // 2. 포물선으로 날아가기
        float t = 0f;
        while (t < travelTime)
        {
            t += Time.deltaTime;
            float k = Mathf.Clamp01(t / travelTime);
            float eased = 1f - (1f - k) * (1f - k); // 처음 빠르고 끝에 부드럽게 멈춤
            Vector3 p = Vector3.Lerp(start, landing, eased);
            p.y += Mathf.Sin(k * Mathf.PI) * arcHeight;
            transform.position = p;
            yield return null;
        }
        transform.position = landing;

        // 3. 멈춘 자리에 등급별 빛 + 감상 시간
        SpawnGlow(groundY);
        float wait = 0f;
        while (wait < admireTime)
        {
            wait += Time.deltaTime;
            FaceTargetSmooth();
            yield return null;
        }

        // 4. 빨려 들어오기 (빛은 바닥에 남아서 서서히 꺼짐)
        ReleaseGlow();
        Vector3 originalScale = transform.localScale;
        float startDistance = Mathf.Max(0.01f, Vector3.Distance(transform.position, TargetPosition));
        float speed = flySpeed;
        float safety = 5f; // 혹시 대상에 닿지 못해도 5초 뒤 자동 획득

        while (!collected)
        {
            safety -= Time.deltaTime;
            speed += flyAcceleration * Time.deltaTime;

            Vector3 goal = TargetPosition;
            transform.position = Vector3.MoveTowards(transform.position, goal, speed * Time.deltaTime);

            float remain = Vector3.Distance(transform.position, goal);
            if (shrinkWhileFlying)
            {
                float k = Mathf.Clamp01(remain / startDistance);
                transform.localScale = originalScale * Mathf.Lerp(0.3f, 1f, k);
            }

            if (remain <= pickupDistance || safety <= 0f)
            {
                Collect();
                yield break;
            }
            yield return null;
        }
    }

    // 바닥 콜라이더가 있으면 그 높이, 없으면 몹 발밑 높이
    private float FindGroundY(Vector3 point)
    {
        Vector3 from = point + Vector3.up * 5f;
        if (Physics.Raycast(from, Vector3.down, out RaycastHit hit, 20f, groundMask, QueryTriggerInteraction.Ignore))
        {
            return hit.point.y;
        }
        return hasEnemyFeet ? enemyFeet.y : point.y - hoverHeight;
    }

    private void SpawnGlow(float groundY)
    {
        GameObject prefab = null;
        if (tierGlowPrefabs != null && tierGlowPrefabs.Length > 0)
        {
            int index = TierIndex;
            if (index < tierGlowPrefabs.Length) prefab = tierGlowPrefabs[index];
            if (prefab == null) prefab = tierGlowPrefabs[0];
        }
        if (prefab == null) return;

        Vector3 pos = glowOnGround
            ? new Vector3(transform.position.x, groundY, transform.position.z)
            : transform.position + glowOffset;

        glow = Instantiate(prefab, pos, Quaternion.identity, transform);

        Color color = GetTierColor();
        foreach (ParticleSystem ps in glow.GetComponentsInChildren<ParticleSystem>())
        {
            ParticleSystem.MainModule main = ps.main;
            if (loopGlow) main.loop = true;
            if (tintGlow) main.startColor = color;
        }
    }

    // 두루마리가 날아갈 때 빛은 제자리에서 방출을 멈추고 남은 입자가 사라지면 삭제
    private void ReleaseGlow()
    {
        if (glow == null) return;
        glow.transform.SetParent(null, true);

        float longest = 0f;
        foreach (ParticleSystem ps in glow.GetComponentsInChildren<ParticleSystem>())
        {
            ps.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            longest = Mathf.Max(longest, ps.main.startLifetime.constantMax);
        }
        Destroy(glow, longest + 0.5f);
        glow = null;
    }

    private Color GetTierColor()
    {
        if (tierColors == null || tierColors.Length == 0) return new Color(1f, 1f, 1f);
        return tierColors[Mathf.Min(TierIndex, tierColors.Length - 1)];
    }

    private Quaternion LookRotationToTarget()
    {
        Vector3 dir = TargetPosition - transform.position;
        dir.y = 0f; // 위아래로 기울지 않게
        return dir.sqrMagnitude > 0.0001f ? Quaternion.LookRotation(dir) : transform.rotation;
    }

    private void FaceTargetNow()
    {
        if (faceTarget && target != null) transform.rotation = LookRotationToTarget();
    }

    private void FaceTargetSmooth()
    {
        if (faceTarget && target != null)
        {
            transform.rotation = Quaternion.Slerp(transform.rotation, LookRotationToTarget(), 5f * Time.deltaTime);
        }
    }

    private void Collect()
    {
        if (collected) return;
        collected = true;

        if (collectEffectPrefab != null)
        {
            EffectUtil.SpawnAndAutoDestroy(collectEffectPrefab, transform.position, Quaternion.identity);
        }
        if (collectSound != null)
        {
            AudioSource.PlayClipAtPoint(collectSound, TargetPosition, collectVolume);
        }
        else
        {
            SoundManager.Play(L => L.spellGet, 1f, 1f, false);   // Collect Sound가 비어 있으면 SoundLibrary의 Spell Get
        }

        if (spell != null)
        {
            bool isNew = SkillManager.Instance != null && SkillManager.Instance.UnlockSpell(spell);
            if (isNew && BattleUIManager.Instance != null)
            {
                // 스펠 이름 + 느낌표까지 같은 등급 색으로
                string name = spell.spellName + "!";
                if (colorSpellNameInNotice)
                {
                    name = $"<color=#{ColorUtility.ToHtmlStringRGB(GetTierColor())}>{name}</color>";
                }
                BattleUIManager.Instance.ShowNotice($"New spell learned: {name}");
            }
        }

        if (glow != null) ReleaseGlow();
        Destroy(gameObject);
    }

    private void OnDestroy()
    {
        // 씬 전환 등으로 중간에 사라질 때 빛이 남지 않게
        if (glow != null) Destroy(glow);
    }
}