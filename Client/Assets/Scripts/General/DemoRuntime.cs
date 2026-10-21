using System.Collections.Generic;
using UnityEngine;

// 비상 시전 키 (시연 모드에서만 생김, 화면에 표시 없음)
public class DemoRuntime : MonoBehaviour
{
    private void Update()
    {
        DemoConfig c = DemoMode.Cfg;
        if (c == null || !c.emergencyKeys) return;
        if (Input.GetKeyDown(c.bestSpellKey)) Cast(true);
        else if (Input.GetKeyDown(c.anySpellKey)) Cast(false);
    }

    private static void Cast(bool best)
    {
        SkillManager sm = SkillManager.Instance;
        if (sm == null || !SkillManager.CanCast) return;
        Vector3 from = sm.wandTip != null ? sm.wandTip.position : sm.transform.position;
        EnemyController target = EnemyController.GetNearest(from);
        if (target == null) return;

        List<SpellData> pool = new List<SpellData>();
        foreach (SpellData s in sm.UnlockedSpells) if (s != null) pool.Add(s);
        if (pool.Count == 0) return;

        SpellData pick = null;
        if (best)
        {
            // 상성이 좋은 것 중 단계가 높은 것 (같으면 무작위)
            List<SpellData> strong = pool.FindAll(s => EnemyController.GetTypeMultiplier(s.elementType, target.enemyType) > 1f);
            if (strong.Count == 0) strong = pool.FindAll(s => EnemyController.GetTypeMultiplier(s.elementType, target.enemyType) >= 1f);
            if (strong.Count > 0)
            {
                int top = 0;
                foreach (SpellData s in strong) top = Mathf.Max(top, s.tier);
                List<SpellData> best3 = strong.FindAll(s => s.tier == top);
                pick = best3[Random.Range(0, best3.Count)];
            }
        }
        if (pick == null) pick = pool[Random.Range(0, pool.Count)];

        string name = string.IsNullOrEmpty(pick.spellName) ? pick.name : pick.spellName;
        SoundManager.Play(L => L.castSuccess, 1f, 1f, false);
        string fail = sm.TryCastByVoice(name, 1.2f);
        Debug.Log(fail == null ? $"[Demo] 비상 시전: {name}" : $"[Demo] 비상 시전 실패: {name} ({fail})");
    }
}