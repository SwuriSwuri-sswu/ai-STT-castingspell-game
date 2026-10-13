using UnityEngine;

public class EnemyController : MonoBehaviour
{
    [Header("몹 설정")]
    public MagicElement enemyType = MagicElement.Normal; // 이 몹의 고유 속성
    public float currentHP = 100f; // 현재 체력

    public void TakeDamage(SpellData spell)
    {
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

        // 4. 사망 판정
        if (currentHP <= 0)
        {
            Die();
        }
    }

    // 공격 속성과 방어(몹) 속성을 비교하여 배율 반환
    private float GetTypeMultiplier(MagicElement attackType, MagicElement defendType)
    {
        // 노말 속성이 개입되거나, 같은 속성끼리의 공격은 무조건 1배
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
        Debug.Log("몹 처치됨!");
        // TODO: 스펠 드랍 로직 및 처치 카운트 증가 로직 추가 예정
        Destroy(gameObject);
    }
}
