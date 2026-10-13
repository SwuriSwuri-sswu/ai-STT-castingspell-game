using System.Collections;
using UnityEngine;

public class EnemyController : MonoBehaviour
{
    [Header("몹 설정")]
    public MagicElement enemyType = MagicElement.Normal; // 이 몹의 고유 속성
    public string enemyRank = "하급"; // 하급, 중급, 상급 (인스펙터에서 설정)
    public float currentHP = 20f; // 초기 체력 (하급 20, 중급 50, 상급 100)

    [Header("컴포넌트 및 에셋")]
    public Animator anim;
    public GameObject dropItemPrefab; // 반짝이 프리팹

    private bool isDead = false;

    private void Start()
    {
        if (anim == null) anim = GetComponent<Animator>();
    }

    public void TakeDamage(SpellData spell)
    {
        if (isDead) return;

        // 1. 상성 배율 계산
        float multiplier = GetTypeMultiplier(spell.elementType, this.enemyType);

        // 2. 최종 데미지 적용
        float finalDamage = spell.baseDamage * multiplier;
        currentHP -= finalDamage;

        Debug.Log($"[{gameObject.name}] {spell.spellName} 적중! 데미지: {finalDamage} (배율: {multiplier}x)");

        // 3. UI 텍스트 출력 호출
        if (BattleUIManager.Instance != null)
        {
            BattleUIManager.Instance.ShowEffectiveness(multiplier);
        }

        // 4. 스펠 폭발 이펙트 생성 (SpellData의 폭발 이펙트 참조)
        if (spell.explosionPrefab != null)
        {
            Instantiate(spell.explosionPrefab, transform.position, Quaternion.identity);
        }

        // 5. 사망 판정
        if (currentHP <= 0)
        {
            Die();
        }
    }

    // 공격 속성과 방어(몹) 속성을 비교하여 배율 반환
    private float GetTypeMultiplier(MagicElement attackType, MagicElement defendType)
    {
        if (attackType == MagicElement.Normal || defendType == MagicElement.Normal || attackType == defendType)
            return 1.0f;

        // 강점 (물->불, 불->풀, 풀->물)
        if (attackType == MagicElement.Water && defendType == MagicElement.Fire) return 1.5f;
        if (attackType == MagicElement.Fire && defendType == MagicElement.Grass) return 1.5f;
        if (attackType == MagicElement.Grass && defendType == MagicElement.Water) return 1.5f;

        // 약점 (물->풀, 불->물, 풀->불)
        if (attackType == MagicElement.Water && defendType == MagicElement.Grass) return 0.5f;
        if (attackType == MagicElement.Fire && defendType == MagicElement.Water) return 0.5f;
        if (attackType == MagicElement.Grass && defendType == MagicElement.Fire) return 0.5f;

        return 1.0f;
    }

    private void Die()
    {
        isDead = true;
        Debug.Log("몹 처치됨!");

        // 추가 피격을 막기 위해 콜라이더 비활성화
        Collider2D coll = GetComponent<Collider2D>();
        if (coll != null) coll.enabled = false;

        // Death 애니메이션 재생
        if (anim != null)
        {
            anim.SetTrigger("Die"); // 애니메이터의 Trigger 이름과 일치시켜야 함
        }

        // TODO: GameSessionManager 등에 처치 카운트 증가 신호 보내기
        // 예: GameSessionManager.Instance.AddMonsterKillCount(enemyType, enemyRank);

        // 애니메이션 대기 후 아이템 드랍 및 삭제
        StartCoroutine(HandleDeathAnimationAndDrop());
    }

    private IEnumerator HandleDeathAnimationAndDrop()
    {
        // Death 애니메이션의 길이를 가져오거나 임의의 대기 시간(예: 1초) 설정
        float animLength = 1.0f;
        if (anim != null)
        {
            animLength = anim.GetCurrentAnimatorStateInfo(0).length;
        }

        yield return new WaitForSeconds(animLength);

        // 아이템(반짝이) 드랍
        if (dropItemPrefab != null)
        {
            Instantiate(dropItemPrefab, transform.position, Quaternion.identity);
        }

        // 몹 프리팹 맵에서 삭제
        Destroy(gameObject);
    }
}