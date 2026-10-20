using UnityEngine;

// 투사체형 마법: 지팡이 끝 → 가장 가까운 몹의 몸 중앙으로 유도 → 적중 시 폭발 이펙트 + 데미지
// 거리로 적중을 판정하므로 투사체 프리팹에 Collider/Rigidbody가 없어도 동작합니다.
public class ProjectileController : MonoBehaviour
{
    [Header("이동")]
    public float speed = 15f;
    [Tooltip("타겟 중심과 이 거리 이내로 들어오면 적중")]
    public float hitDistance = 0.4f;
    [Tooltip("타겟이 없을 때 이 시간이 지나면 자동 삭제")]
    public float maxLifetime = 5f;

    private SpellData spell;
    private EnemyController target;
    private float power = 1f;   // 영창 보너스 (시전 순간 값을 들고 날아감)
    private bool hasHit;
    private float lifetime;

    // 이전 호출 방식 호환용 (타겟은 자동으로 가장 가까운 몹)
    public void Initialize(SpellData spellData) => Initialize(spellData, null);

    public void Initialize(SpellData spellData, EnemyController targetEnemy, float spellPower = 1f)
    {
        spell = spellData;
        power = spellPower;
        target = targetEnemy != null ? targetEnemy : EnemyController.GetNearest(transform.position);
    }

    private void Update()
    {
        if (hasHit) return;

        lifetime += Time.deltaTime;
        if (lifetime >= maxLifetime)
        {
            Destroy(gameObject);
            return;
        }

        // 날아가는 도중 타겟이 죽었으면 다음으로 가까운 몹으로 변경
        if (target == null || target.IsDying)
        {
            target = EnemyController.GetNearest(transform.position);
        }

        if (target == null)
        {
            // 몹이 없으면 바라보는 방향으로 직진
            transform.position += transform.forward * speed * Time.deltaTime;
            return;
        }

        Vector3 aim = target.AimPoint;
        transform.position = Vector3.MoveTowards(transform.position, aim, speed * Time.deltaTime);

        Vector3 dir = aim - transform.position;
        if (dir.sqrMagnitude > 0.0001f) transform.rotation = Quaternion.LookRotation(dir);

        if ((aim - transform.position).sqrMagnitude <= hitDistance * hitDistance)
        {
            Hit(target);
        }
    }

    // 프리팹에 Collider(Is Trigger) + Rigidbody가 있으면 몸에 닿는 순간에도 적중 처리
    private void OnTriggerEnter(Collider other)
    {
        if (hasHit) return;

        EnemyController enemy = other.GetComponentInParent<EnemyController>();
        if (enemy != null && !enemy.IsDying) Hit(enemy);
    }

    private void Hit(EnemyController enemy)
    {
        hasHit = true;

        if (spell != null)
        {
            // 1. 투사체가 사라진 자리에 폭발 이펙트 (여기 한 곳에서만 생성)
            float effectLength = EffectUtil.SpawnScaled(spell.explosionPrefab, transform.position + spell.explosionOffset,
                                                        Quaternion.identity, SkillManager.EffectScale * spell.explosionScale,
                                                        spell.effectDuration, SkillManager.Instance);

            // 2. 데미지 → (HP 0이면) 폭발과 함께 death 애니메이션
            enemy.ReceiveSpell(spell, spell.ResolveDamageDelay(effectLength), power);
        }

        // 3. 투사체 삭제
        Destroy(gameObject);
    }
}