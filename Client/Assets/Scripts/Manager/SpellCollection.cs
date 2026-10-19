using System.Collections.Generic;
using UnityEngine;

// 스펠 도감 수집 기록 (게임을 껐다 켜도 유지)
// 한 판이 끝나면 배운 스펠은 초기화되지만, '한 번이라도 배운 적 있는 스펠'은 여기 계속 남습니다.
// 타이틀 화면의 스펠 도감이 이 기록을 보여 줍니다.
// 저장 위치: PlayerPrefs (이 컴퓨터에만 저장)
public static class SpellCollection
{
    private const string CollectedKey = "SpellCollection.Collected";
    private const string SeenKey = "SpellCollection.Seen";
    private const char Separator = '|';

    private static List<string> collected;   // 처음 배운 순서대로
    private static HashSet<string> seen;     // 도감에서 이미 확인한 스펠

    public static IReadOnlyList<string> CollectedNames
    {
        get { Load(); return collected; }
    }

    public static bool IsCollected(SpellData spell)
    {
        Load();
        return spell != null && collected.Contains(Key(spell));
    }

    // 스펠을 배울 때마다 호출 (SkillManager). 처음 배운 스펠이면 true
    public static bool Add(SpellData spell)
    {
        if (spell == null) return false;
        Load();
        string key = Key(spell);
        if (collected.Contains(key)) return false;

        collected.Add(key);
        Save(CollectedKey, collected);
        return true;
    }

    public static bool IsSeen(SpellData spell)
    {
        Load();
        return spell != null && seen.Contains(Key(spell));
    }

    public static void MarkSeen(IEnumerable<SpellData> spells)
    {
        Load();
        bool changed = false;
        foreach (SpellData s in spells)
        {
            if (s != null && seen.Add(Key(s))) changed = true;
        }
        if (changed) Save(SeenKey, seen);
    }

    // 테스트용: 도감 기록 전부 지우기
    public static void Clear()
    {
        Load();
        collected.Clear();
        seen.Clear();
        PlayerPrefs.DeleteKey(CollectedKey);
        PlayerPrefs.DeleteKey(SeenKey);
        PlayerPrefs.Save();
        Debug.Log("[SpellCollection] 스펠 도감 기록을 초기화했습니다.");
    }

    // 스펠 이름 기준으로 저장 (에셋 이름이 바뀌어도 spellName이 같으면 유지)
    private static string Key(SpellData spell) =>
        string.IsNullOrEmpty(spell.spellName) ? spell.name : spell.spellName;

    private static void Load()
    {
        if (collected != null) return;
        collected = new List<string>(Split(PlayerPrefs.GetString(CollectedKey, "")));
        seen = new HashSet<string>(Split(PlayerPrefs.GetString(SeenKey, "")));
    }

    private static IEnumerable<string> Split(string raw)
    {
        if (string.IsNullOrEmpty(raw)) yield break;
        foreach (string s in raw.Split(Separator))
        {
            if (!string.IsNullOrEmpty(s)) yield return s;
        }
    }

    private static void Save(string key, IEnumerable<string> values)
    {
        PlayerPrefs.SetString(key, string.Join(Separator.ToString(), values));
        PlayerPrefs.Save();
    }
}