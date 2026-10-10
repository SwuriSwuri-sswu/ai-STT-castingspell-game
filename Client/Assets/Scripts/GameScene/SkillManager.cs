using System.Collections.Generic;
using UnityEngine;

public class SkillManager : MonoBehaviour
{
    public Transform wandTip; // 지팡이 끝 빈 오브젝트 연결
    public List<SpellData> spellDatabase; // 생성한 SpellData 파일들을 여기에 넣음

    public void CastSpell(string recognizedText)
    {
        string cleanedInput = recognizedText.Trim();
        SpellData foundSpell = spellDatabase.Find(s => s.spellName == cleanedInput && s.isUnlocked);

        if (foundSpell != null)
        {
            // 1:1 상황이므로 씬에 있는 적을 찾습니다.
            GameObject targetEnemy = GameObject.FindGameObjectWithTag("Enemy");

            // --- [1] 투사체형 (Projectile) 마법 처리 ---
            if (foundSpell.castType == CastType.Projectile)
            {
                // 지팡이 끝에서 생성
                GameObject projectile = Instantiate(foundSpell.projectilePrefab, wandTip.position, wandTip.rotation);
                projectile.GetComponent<ProjectileController>().Initialize(foundSpell);
                Debug.Log($"[마법 시전] {foundSpell.spellName} 발사!");
            }
            // --- [2] 즉발/장판형 (TargetInstant) 마법 처리 ---
            else if (foundSpell.castType == CastType.TargetInstant)
            {
                if (targetEnemy != null)
                {
                    // 적의 바닥(또는 중심) 좌표 계산
                    Vector3 spawnPosition = targetEnemy.transform.position;

                    // (선택) 몹의 머리 위에서 떨어지는 형태(메테오)라면 Y축을 조금 높여줍니다.
                    // spawnPosition.y += 5f; 

                    // 몹의 위치에 이펙트를 즉시 생성
                    GameObject instantEffect = Instantiate(foundSpell.explosionPrefab, spawnPosition, Quaternion.identity);

                    // 즉발형 마법은 이동 로직이 필요 없으므로 바로 데미지 처리 (구현 예정)
                    // targetEnemy.GetComponent<EnemyController>().TakeDamage(foundSpell);

                    // 이펙트 프리팹 자체에 수명(Lifetime)이 다하면 파괴되는 파티클 세팅을 해두거나,
                    // 스크립트로 n초 후 파괴시킵니다.
                    Destroy(instantEffect, 3f); // 3초 후 삭제

                    Debug.Log($"[마법 시전] {foundSpell.spellName} 타겟 위치에 즉시 발동!");
                }
                else
                {
                    Debug.Log("타겟이 없어 즉발형 마법을 시전할 수 없습니다.");
                }
            }
        }
        else
        {
            Debug.Log($"[시전 실패] 알 수 없거나 해금되지 않은 주문입니다: {cleanedInput}");
        }
    }
}