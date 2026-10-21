using System.Collections.Generic;
using UnityEngine;

// =====================================================================
// 시연 모드 (발표용)
//   설정: Assets/Resources/DemoConfig.asset  (메뉴 Tools → Demo → 시연 모드 켜기 / 끄기)
//   - main 브랜치  : Enabled 꺼 둠 (또는 에셋이 없음) → 본게임과 완전히 똑같이 동작
//   - simulation   : Enabled 켬 → 아래 규칙으로 짧게 진행
//
//   [게임씬] 인트로 건너뜀 → 하급 → 중급 → 상급 몹이 차례로 등장
//            스펠은 하나만 빼고 전부 해금 → 첫 몹을 잡으면 그 스펠이 반드시 드랍
//            3마리를 잡으면 보스씬으로 이동 / 플레이어는 체력이 1 밑으로 안 내려감
//   [보스씬] 입장 연출(빠르게) → 안내창 생략 → 내 턴·마왕 턴 2번씩
//            (마지막 라운드에 페이즈 전환 연출) → 마왕 쓰러짐 연출 → 엔딩
//            하수인·기절·힘 모으기 없음, 내 턴 시간 넉넉하게
//   [비상 키] F1 = 지금 적에게 상성이 좋은 스펠 시전 / F2 = 아무 스펠 시전 (마이크가 안 될 때)
//   [기록]   게임을 켤 때마다 도감·보스 클리어·인트로 기록을 지워 매번 같은 시연
// =====================================================================
public static class DemoMode
{
    private static DemoConfig cfg;
    private static bool loaded;

    public static DemoConfig Cfg
    {
        get
        {
            if (!loaded) { loaded = true; cfg = Resources.Load<DemoConfig>("DemoConfig"); }
            return cfg;
        }
    }

    /// <summary>시연 모드가 켜져 있는지 (에셋이 없으면 꺼짐)</summary>
    public static bool On => Cfg != null && Cfg.enabled;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() { loaded = false; cfg = null; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Boot()
    {
        if (!On) return;
        Debug.Log("<color=#FFCC00>[Demo] 시연 모드가 켜져 있습니다.</color>");
        if (Cfg.resetRecordsOnLaunch)
        {
            SpellCollection.Clear();
            MonsterCollection.Clear();
            StageProgress.ClearBossRecord();
            PlayerPrefs.DeleteKey("IntroSeen");
            PlayerPrefs.Save();
        }
        GameObject go = new GameObject("[DemoMode]");
        Object.DontDestroyOnLoad(go);
        go.AddComponent<DemoRuntime>();
    }

    public static bool IsGameScene(string sceneName) => On && sceneName == Cfg.gameSceneName;

    /// <summary>시작할 때 잠가 둘 스펠</summary>
    public static SpellData LockedSpell(List<SpellData> database)
    {
        if (!On) return null;
        if (Cfg.lockedSpell != null) return Cfg.lockedSpell;
        if (database == null) return null;
        for (int i = database.Count - 1; i >= 0; i--)
            if (database[i] != null && !database[i].unlockedByDefault) return database[i];
        return null;
    }

    /// <summary>처치 수에 맞는 몹 (pool에서 고름). 고를 수 없으면 null</summary>
    public static EnemyController PickMob(int kills, IEnumerable<EnemyController> pool, EnemyController last)
    {
        if (!On) return null;
        if (Cfg.mobSequence != null && kills < Cfg.mobSequence.Length && Cfg.mobSequence[kills] != null)
            return Cfg.mobSequence[kills];
        if (Cfg.rankSequence == null || Cfg.rankSequence.Length == 0) return null;
        MonsterRank rank = Cfg.rankSequence[Mathf.Min(kills, Cfg.rankSequence.Length - 1)];

        List<EnemyController> match = new List<EnemyController>();
        foreach (EnemyController e in pool)
            if (e != null && e.rank == rank && e != last && !match.Contains(e)) match.Add(e);
        if (match.Count == 0)
            foreach (EnemyController e in pool)
                if (e != null && e.rank == rank && !match.Contains(e)) match.Add(e);
        return match.Count > 0 ? match[Random.Range(0, match.Count)] : null;
    }
}