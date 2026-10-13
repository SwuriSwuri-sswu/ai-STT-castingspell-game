using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 음성 기능이 사용하는 "가상의 SkillManager". 팀원 코드를 참조하지 않는다.
///
/// 지금: 팀원 SkillManager 의 규칙을 따라 하고, 결과는 로그로 남긴다.
///   - 시작 시 기본 스펠(tier 0)만 해금
///   - 해금: Tier 1 → 3 순서로, 그 단계의 잠긴 스펠 중 하나를 랜덤 (게임에서는 5마리 처치마다)
///   - 시전: 공백을 뺀 이름이 같고 해금된 스펠만 성공
/// 연동: GetCastableSpells / TryCast 안쪽만 진짜 SkillManager 를 쓰도록 바꾸면 된다.
///       이 클래스를 쓰는 VoiceRecorder, VoiceWsTest 는 수정하지 않는다.
/// </summary>
public class VoiceSpellBook : MonoBehaviour
{
    [Serializable]
    public class Spell
    {
        public string id;          // SpellData 에셋 이름에 해당 (서버 결과의 skill.id)
        public string spellName;   // SpellData.spellName (판정 기준 이름)
        public int tier;           // 0 = 기본(Normal), 1~3 = 단계
        public bool isUnlocked;
        public List<string> aliases = new List<string>();   // 자주 틀리게 인식되는 표기
    }

    /// <summary>해금·초기화로 목록이 바뀌면 호출된다. VoiceWsTest 가 서버로 다시 보낸다.</summary>
    public event Action SpellsChanged;

    [SerializeField] List<Spell> spells = DefaultSpells();
    [Tooltip("켜면 해금된 스펠만 서버로 보낸다 (오인식 감소)")]
    [SerializeField] bool unlockedOnly = true;
    [SerializeField] KeyCode unlockKey = KeyCode.U;   // 테스트: 해금 (몬스터 5마리 처치에 해당)
    [SerializeField] KeyCode resetKey = KeyCode.R;    // 테스트: 초기화 (게임 재시작에 해당)

    void Awake() => ResetSpellUnlocks();   // SkillManager.Awake 와 같은 시점

    void Update()
    {
        if (Input.GetKeyDown(unlockKey)) UnlockRandomSpell();
        if (Input.GetKeyDown(resetKey)) ResetSpellUnlocks();
    }

    /// <summary>서버 판정 후보로 보낼 스펠 목록</summary>
    public List<Spell> GetCastableSpells() =>
        spells.FindAll(s => s != null && !string.IsNullOrWhiteSpace(s.spellName) && (!unlockedOnly || s.isUnlocked));

    /// <summary>발동 요청. SkillManager.CastSpell 과 같은 규칙으로 찾고, 성공하면 true</summary>
    public bool TryCast(string spellName)
    {
        if (string.IsNullOrEmpty(spellName)) return false;

        string key = spellName.Replace(" ", "");
        Spell found = spells.Find(s => s != null && s.isUnlocked && s.spellName != null && s.spellName.Replace(" ", "") == key);

        Debug.Log(found != null
            ? $"[SPELL] 시전 성공: {found.spellName} (Tier {found.tier})"
            : $"[SPELL] 시전 실패 (없거나 해금되지 않음): {spellName}");
        return found != null;
    }

    public void UnlockRandomSpell()
    {
        for (int tier = 1; tier <= 3; tier++)
        {
            List<Spell> locked = spells.FindAll(s => s != null && s.tier == tier && !s.isUnlocked);
            if (locked.Count == 0) continue;

            Spell pick = locked[UnityEngine.Random.Range(0, locked.Count)];
            pick.isUnlocked = true;
            Debug.Log($"[SPELL] 해금: {pick.spellName} (Tier {tier})");
            SpellsChanged?.Invoke();
            return;
        }
        Debug.Log("[SPELL] 모든 스펠을 해금했습니다.");
    }

    public void ResetSpellUnlocks()
    {
        foreach (Spell s in spells)
            if (s != null) s.isUnlocked = s.tier == 0;

        Debug.Log("[SPELL] 초기화: 기본 스펠만 해금");
        SpellsChanged?.Invoke();
    }

    // 팀에서 정한 30개. tier 0 = 기본(Normal). Inspector 에서 수정 가능
    static Spell S(string id, string name, int tier) => new Spell { id = id, spellName = name, tier = tier };

    static List<Spell> DefaultSpells() => new List<Spell>
    {
        // 노말 (시작 시 해금)
        S("light", "빛", 0), S("wind", "바람", 0), S("stone", "돌", 0),
        // 물
        S("aqua", "물", 1), S("ice", "얼음", 1), S("wave", "파도", 1),
        S("ice_thorn", "얼음 가시", 2), S("rain_shower", "소나기", 2), S("water_spray", "물보라", 2),
        S("cold_rain_shower", "차가운 소나기", 3), S("sharp_icicle", "날카로운 고드름", 3), S("raging_wave", "몰아치는 파도", 3),
        // 불
        S("sun", "해", 1), S("fire", "불", 1), S("flame", "화염", 1),
        S("fire_ball", "화염구", 2), S("spark", "불꽃", 2), S("volcano", "화산", 2),
        S("flame_burst", "화염 폭발", 3), S("hot_flame", "뜨거운 불꽃", 3), S("red_volcano_explosion", "붉은 화산 폭발", 3),
        // 풀
        S("grass", "풀", 1), S("leaf", "잎", 1), S("seed", "씨앗", 1),
        S("log", "통나무", 2), S("flower_petal", "꽃잎", 2), S("leafage", "잎사귀", 2),
        S("giant_log", "거대한 통나무", 3), S("poisonous_thorn", "독을 품은 가시", 3), S("dancing_green_flower_petal", "춤추는 초록 꽃잎", 3),
    };
}
