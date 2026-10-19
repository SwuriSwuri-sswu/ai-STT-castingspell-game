using System.Collections.Generic;
using UnityEngine;

// 몹 도감 기록 (게임을 껐다 켜도 유지)
//   - 만난 적 있는 몹 (최초 조우 시 "New!" 표시, 도감에서 ??? → 공개)
//   - 몹별 처치 수
// 저장 위치: PlayerPrefs (이 컴퓨터에만 저장). 키 = 프리팹 이름 (예: High_Demon)
public static class MonsterCollection
{
    private const string EncounteredKey = "MonsterCollection.Encountered";
    private const string SeenKey = "MonsterCollection.Seen";
    private const string KillsKey = "MonsterCollection.Kills";
    private const char Sep = '|';

    private static List<string> encountered;
    private static HashSet<string> seen;
    private static Dictionary<string, int> kills;

    public static IReadOnlyList<string> Encountered { get { Load(); return encountered; } }

    public static bool IsEncountered(string key) { Load(); return !string.IsNullOrEmpty(key) && encountered.Contains(key); }

    // 몹이 등장할 때 호출 (EnemyManager). 처음 만난 몹이면 true
    public static bool Encounter(string key)
    {
        if (string.IsNullOrEmpty(key)) return false;
        Load();
        if (encountered.Contains(key)) return false;
        encountered.Add(key);
        Save(EncounteredKey, encountered);
        return true;
    }

    public static void AddKill(string key)
    {
        if (string.IsNullOrEmpty(key)) return;
        Load();
        kills.TryGetValue(key, out int n);
        kills[key] = n + 1;
        List<string> list = new List<string>();
        foreach (KeyValuePair<string, int> p in kills) list.Add($"{p.Key}={p.Value}");
        Save(KillsKey, list);
    }

    public static int GetKills(string key)
    {
        Load();
        return !string.IsNullOrEmpty(key) && kills.TryGetValue(key, out int n) ? n : 0;
    }

    // 도감에서 확인했는지 (NEW 표시용)
    public static bool IsSeen(string key) { Load(); return seen.Contains(key); }

    public static void MarkSeen(IEnumerable<string> keys)
    {
        Load();
        bool changed = false;
        foreach (string k in keys) if (!string.IsNullOrEmpty(k) && seen.Add(k)) changed = true;
        if (changed) Save(SeenKey, seen);
    }

    public static void Clear()
    {
        Load();
        encountered.Clear(); seen.Clear(); kills.Clear();
        PlayerPrefs.DeleteKey(EncounteredKey);
        PlayerPrefs.DeleteKey(SeenKey);
        PlayerPrefs.DeleteKey(KillsKey);
        PlayerPrefs.Save();
        Debug.Log("[MonsterCollection] 몹 도감 기록을 초기화했습니다.");
    }

    private static void Load()
    {
        if (encountered != null) return;
        encountered = new List<string>(Split(PlayerPrefs.GetString(EncounteredKey, "")));
        seen = new HashSet<string>(Split(PlayerPrefs.GetString(SeenKey, "")));
        kills = new Dictionary<string, int>();
        foreach (string pair in Split(PlayerPrefs.GetString(KillsKey, "")))
        {
            int i = pair.LastIndexOf('=');
            if (i > 0 && int.TryParse(pair.Substring(i + 1), out int n)) kills[pair.Substring(0, i)] = n;
        }
    }

    private static IEnumerable<string> Split(string raw)
    {
        if (string.IsNullOrEmpty(raw)) yield break;
        foreach (string s in raw.Split(Sep)) if (!string.IsNullOrEmpty(s)) yield return s;
    }

    private static void Save(string key, IEnumerable<string> values)
    {
        PlayerPrefs.SetString(key, string.Join(Sep.ToString(), values));
        PlayerPrefs.Save();
    }
}