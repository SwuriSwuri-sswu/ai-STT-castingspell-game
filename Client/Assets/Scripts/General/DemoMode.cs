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
[CreateAssetMenu(fileName = "DemoConfig", menuName = "Demo/Demo Config")]
public class DemoConfig : ScriptableObject
{
    [Tooltip("켜면 시연 모드. main 브랜치에서는 반드시 꺼 두기")]
    public bool enabled = false;

    [Header("게임씬")]
    [Tooltip("인트로 이야기 건너뛰고 바로 시작")]
    public bool skipIntro = true;
    [Tooltip("몹 등장 순서 (등급). 처치 수만큼 다음 칸으로. 칸이 모자라면 마지막 등급 반복")]
    public MonsterRank[] rankSequence = { MonsterRank.Low, MonsterRank.Mid, MonsterRank.High };
    [Tooltip("(선택) 특정 몹을 순서대로 내보내고 싶으면 여기에. 비운 칸은 위 등급에서 무작위")]
    public EnemyController[] mobSequence = new EnemyController[0];
    [Tooltip("이만큼 잡으면 보스씬으로 이동")]
    [Min(1)] public int stage1Kills = 3;
    [Tooltip("시작할 때 잠가 둘 스펠 (첫 몹을 잡으면 이 스펠이 드랍). 비우면 기본 해금이 아닌 스펠 중 마지막 것")]
    public SpellData lockedSpell;
    [Tooltip("플레이어 체력이 1 밑으로 내려가지 않음 (맞는 연출·체력 감소는 그대로)")]
    public bool playerInvincible = true;

    [Header("보스씬")]
    [Tooltip("입장 때 걷는 속도 배율")]
    [Min(0.1f)] public float bossWalkSpeed = 1.6f;
    [Tooltip("보스전 안내창 생략")]
    public bool skipBossTutorial = true;
    [Tooltip("내 턴 + 마왕 턴을 몇 번 한 뒤 마왕이 쓰러질지")]
    [Min(1)] public int bossRounds = 2;
    [Tooltip("마지막 라운드의 마왕 턴 직전에 페이즈 전환 연출 보여 주기")]
    public bool showPhaseChange = true;
    [Tooltip("내 턴 제한 시간 (초). 음성 판정이 느릴 수 있으니 넉넉하게")]
    public float playerTurnTime = 20f;

    [Header("비상 키 (마이크·서버가 안 될 때)")]
    public bool emergencyKeys = true;
    [Tooltip("지금 적에게 상성이 좋은 스펠 시전")]
    public KeyCode bestSpellKey = KeyCode.F1;
    [Tooltip("아무 스펠이나 시전")]
    public KeyCode anySpellKey = KeyCode.F2;

    [Header("기록")]
    [Tooltip("게임을 켤 때마다 도감·보스 클리어·인트로 본 기록을 지움 (리허설 후에도 매번 같은 시연)")]
    public bool resetRecordsOnLaunch = true;
    public string gameSceneName = "GameScene";
}

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