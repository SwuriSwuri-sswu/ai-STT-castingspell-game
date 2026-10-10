using UnityEngine;

public class ProjectileController : MonoBehaviour
{
    public float speed = 15f; // 날아가는 속도
    private SpellData mySpellData;
    private Transform target;

    public void Initialize(SpellData spellData)
    {
        mySpellData = spellData;

        // 1:1 방식이므로 씬에 있는 'Enemy' 태그를 가진 오브젝트를 즉시 탐색
        GameObject enemyObj = GameObject.FindGameObjectWithTag("Enemy");
        if (enemyObj != null)
        {
            target = enemyObj.transform;
        }
    }

    void Update()
    {
        if (target == null)
        {
            // 적이 씬에 없으면 지팡이가 향하던 방향으로 직진
            transform.Translate(Vector3.forward * speed * Time.deltaTime);
            return;
        }

        // 적을 향해 이동 (추후 적의 중심점 좌표로 수정 예정)
        transform.position = Vector3.MoveTowards(transform.position, target.position, speed * Time.deltaTime);
        transform.LookAt(target.position);
    }

    void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("Enemy"))
        {
            // 부딪힌 대상의 EnemyController를 가져와서 데미지 전달
            EnemyController enemy = other.GetComponent<EnemyController>();
            if (enemy != null)
            {
                enemy.TakeDamage(mySpellData);
            }

            // 적중 시 폭발 이펙트 생성
            if (mySpellData.explosionPrefab != null)
            {
                Instantiate(mySpellData.explosionPrefab, transform.position, Quaternion.identity);
            }

            Destroy(gameObject); // 투사체 파괴
        }
    }
}