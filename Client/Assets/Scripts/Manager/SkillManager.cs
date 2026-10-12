using System.Collections.Generic;
using UnityEngine;

public class SkillManager : MonoBehaviour
{
    // 보이스 매니저에서 쉽게 부를 수 있도록 싱글톤 처리
    public static SkillManager Instance { get; private set; }

    [Header("Magic Casting Setup")]
    public Transform wandTip; // 지팡이 끝 빈 오브젝트 연결
    public List<SpellData> spellDatabase; // 생성한 SpellData(ScriptableObject) 파일들을 여기에 넣음

    private void Awake()
    {
        if (Instance == null) { Instance = this; ResetSpellUnlocks(); }
        else Destroy(gameObject);
    }


    // [1] 보이스 매니저(STT)에서 텍스트를 넘겨받으면 실행되는 함수
    public void CastSpell(string recognizedText)
    {
        if (string.IsNullOrEmpty(recognizedText)) return;

        // 1. STT 삑사리 완벽 방지: 중간 띄어쓰기까지 전부 제거 (예: "얼음 가시" -> "얼음가시")
        string cleanedInput = recognizedText.Replace(" ", "");

        // 2. 데이터베이스에서 스펠 찾기 
        // (SpellData의 이름도 공백을 뺀 상태로 비교해서 완벽하게 매칭되게 함)
        SpellData foundSpell = spellDatabase.Find(s =>
            s.spellName.Replace(" ", "") == cleanedInput && s.isUnlocked
        );

        if (foundSpell != null)
        {
            // 3. 지팡이 끝 좌표와 회전값을 기준으로 파티클/발사체 프리팹 생성
            if (foundSpell.projectilePrefab != null)
            {
                GameObject projectile = Instantiate(foundSpell.projectilePrefab, wandTip.position, wandTip.rotation);

                // 발사체 스크립트에 마법 정보 전달 (데미지, 속성 등)
                projectile.GetComponent<ProjectileController>().Initialize(foundSpell);
            }

            // 4. GameManager에 점수 및 UI 갱신 요청 (예: 티어 * 50점)
            GameManager.Instance.OnSpellRecognized(foundSpell.spellName, foundSpell.tier * 50);

            Debug.Log($"[마법 시전 성공] {foundSpell.spellName} 발사!");
        }
        else
        {
            // 스펠이 아니거나 해금 안 된 경우
            Debug.Log($"[시전 실패] 알 수 없거나 해금되지 않은 주문입니다: {recognizedText}");
        }
    }

    // [2] 해금 시스템
    // GameManager에서 몬스터 5마리를 잡을 때마다 호출하는 함수
    public void UnlockRandomSpell()
    {
        // 1단계(Tier 1)부터 3단계(Tier 3)까지 순차적으로 확인
        for (int currentTier = 1; currentTier <= 3; currentTier++)
        {
            // 현재 확인 중인 단계(Tier)에서 아직 해금 안 된(!isUnlocked) 스펠들만 싹 모음
            // (노말 속성은 이미 다 해금되어 있으므로 제외)
            List<SpellData> lockedSpells = spellDatabase.FindAll(s => s.tier == currentTier && !s.isUnlocked);

            if (lockedSpells.Count > 0)
            {
                // 아직 해금할 스펠이 남아있다면 그 중 랜덤으로 하나 뽑기
                int randomIndex = Random.Range(0, lockedSpells.Count);
                SpellData spellToUnlock = lockedSpells[randomIndex];

                // 해금 처리
                spellToUnlock.isUnlocked = true;

                Debug.Log($"[스펠 해금] 새로운 마법을 배웠습니다!: {spellToUnlock.spellName} (Tier {spellToUnlock.tier})");

                // TODO: 여기서 화면에 "OOO 마법이 해금되었습니다!" 띄우는 UI 함수 호출하면 좋습니다.

                return; // 하나 해금했으니 반복문 멈추고 함수 종료 (다음 5마리 잡을 때까지 대기)
            }
            // lockedSpells.Count == 0이면 전부 해금 > 다음 해금 단계로 넘어감
        }

        Debug.Log("모든 스펠을 마스터했습니다!");
    }

    // 게임 다시 시작할 때 기존에 해금된 스펠들을 다시 잠그는 함수
    // Awake()에서 호출
    public void ResetSpellUnlocks()
    {
        foreach (var spell in spellDatabase)
        {
            // 노말 속성은 기본 스펠이니까 해금 상태(true) 유지, 나머지는 전부 잠금(false)
            if (spell.elementType == MagicElement.Normal)
                spell.isUnlocked = true;
            else
                spell.isUnlocked = false;
        }
        Debug.Log("[SkillManager] 스펠 해금 상태 초기화 완료");
    }
}