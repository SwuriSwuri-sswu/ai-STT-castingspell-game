using System.Collections;
using UnityEngine;

// 몹이 드랍하는 반짝이 (3D)
// 톡 튀어오름 → 잠시 후 플레이어에게 날아감 → 닿으면 스펠 해금 + 획득 UI
public class DropItem : MonoBehaviour
{
    [Header("튀어오르기")]
    public float popForce = 4f;
    [Tooltip("좌우/앞뒤로 퍼지는 정도")]
    public float popSpread = 0.5f;
    public float waitBeforeMagnet = 0.5f;

    [Header("자석 효과")]
    public float flySpeed = 6f;
    [Tooltip("날아가면서 점점 빨라지는 정도 (초당 속도 증가량)")]
    public float flyAcceleration = 20f;
    public float pickupDistance = 0.5f;
    [Tooltip("혹시 플레이어에게 닿지 못해도 이 시간이 지나면 자동 획득")]
    public float maxLifetime = 4f;

    [Header("날아갈 대상")]
    [Tooltip("비우면 'Player' 태그 오브젝트 → 없으면 메인 카메라")]
    public Transform target;

    private SpellData spell;
    private Rigidbody rb;
    private bool isFlying;
    private bool collected;
    private float currentSpeed;

    // EnemyController가 생성 직후 호출
    public void Initialize(SpellData spellData)
    {
        spell = spellData;
    }

    private void Start()
    {
        if (target == null)
        {
            GameObject player = GameObject.FindGameObjectWithTag("Player");
            if (player != null) target = player.transform;
            else if (Camera.main != null) target = Camera.main.transform;
        }

        rb = GetComponent<Rigidbody>();
        if (rb == null) rb = gameObject.AddComponent<Rigidbody>();
        rb.isKinematic = false;
        rb.useGravity = true;

        // 1. 위쪽 + 약간 랜덤한 방향으로 튀어오르기
        Vector3 dir = new Vector3(Random.Range(-popSpread, popSpread), 1f, Random.Range(-popSpread, popSpread)).normalized;
        rb.AddForce(dir * popForce, ForceMode.Impulse);

        StartCoroutine(StartMagnet());
    }

    private IEnumerator StartMagnet()
    {
        yield return new WaitForSeconds(waitBeforeMagnet);

        // 물리 끄고 직접 이동
        rb.isKinematic = true;
        rb.useGravity = false;

        currentSpeed = flySpeed;
        isFlying = true;
    }

    private void Update()
    {
        if (collected) return;

        maxLifetime -= Time.deltaTime;
        if (maxLifetime <= 0f)
        {
            Collect();
            return;
        }

        if (!isFlying || target == null) return;

        currentSpeed += flyAcceleration * Time.deltaTime;
        transform.position = Vector3.MoveTowards(transform.position, target.position, currentSpeed * Time.deltaTime);

        if ((transform.position - target.position).sqrMagnitude < pickupDistance * pickupDistance)
        {
            Collect();
        }
    }

    private void Collect()
    {
        collected = true;

        if (spell != null)
        {
            bool isNew = SkillManager.Instance != null && SkillManager.Instance.UnlockSpell(spell);
            if (isNew && BattleUIManager.Instance != null)
            {
                BattleUIManager.Instance.ShowNotice($"{spell.spellName} 스펠을 획득했습니다!");
            }
        }

        Destroy(gameObject);
    }
}
