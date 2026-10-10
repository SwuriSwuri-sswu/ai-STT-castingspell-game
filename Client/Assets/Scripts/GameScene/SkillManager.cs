using System.Collections.Generic;
using UnityEngine;

public class SkillManager : MonoBehaviour
{
    public Transform wandTip; // 지팡이 끝 빈 오브젝트 연결
    public List<SpellData> spellDatabase; // 생성한 SpellData 파일들을 여기에 넣음

    public void CastSpell(string recognizedText)
    {
        // 공백이나 불필요한 기호 제거 후 정확히 일치(==)하는지 확인
        string cleanedInput = recognizedText.Trim();

        // 데이터베이스에서 이름이 같고, 해금된 스펠 찾기
        SpellData foundSpell = spellDatabase.Find(s => s.spellName == cleanedInput && s.isUnlocked);

        if (foundSpell != null)
        {
            // 지팡이 끝 좌표와 회전값을 기준으로 프리팹 생성
            GameObject projectile = Instantiate(foundSpell.projectilePrefab, wandTip.position, wandTip.rotation);

            // 발사체 스크립트에 마법 정보 전달
            projectile.GetComponent<ProjectileController>().Initialize(foundSpell);

            Debug.Log($"[마법 시전 성공] {foundSpell.spellName} 발사!");
        }
        else
        {
            Debug.Log($"[시전 실패] 알 수 없거나 해금되지 않은 주문입니다: {cleanedInput}");
        }
    }
}