using System;
using UnityEngine;

// 한 판(세션) 동안의 처치 기록과 스펠 드랍 판정을 담당
// - 속성 × 등급별 처치 수 (Monster 패널/도감용)
// - 같은 속성 몹을 N마리 잡을 때마다 그 속성 스펠 드랍 판정
// - 스테이지 1 최대 처치 수(50) 도달 알림
public class GameSessionManager : MonoBehaviour
{
    public static GameSessionManager Instance { get; private set; }

    [Header("스테이지 1 규칙")]
    [Tooltip("스테이지 1에서 잡을 수 있는 최대 몹 수. 도달하면 스테이지 2로 넘어갑니다.")]
    public int maxStage1Kills = 50;

    [Header("스펠 드랍 규칙")]
    [Tooltip("같은 속성 몹을 몇 마리 잡을 때마다 드랍 판정을 할지")]
    [Min(1)] public int killsPerDropRoll = 3;
    [Tooltip("드랍 판정 시 실제로 드랍될 확률 (1 = 100%)")]
    [Range(0f, 1f)] public float dropChance = 1f;

    [Header("점수")]
    public int lowRankScore = 10;
    public int midRankScore = 30;
    public int highRankScore = 60;

    // 처치 수가 바뀔 때 (현재 총 처치 수, 최대 처치 수) - UI 카운터용
    public event Action<int, int> OnKillCountChanged;
    // 스테이지 1 최대 처치 수 도달
    public event Action OnStage1KillLimitReached;

    public int TotalKills { get; private set; }
    public bool IsStage1LimitReached => TotalKills >= maxStage1Kills;

    private const int ElementCount = 4; // Normal, Water, Fire, Grass
    private const int RankCount = 3;    // Low, Mid, High

    private int[,] killTable = new int[ElementCount, RankCount];
    private int[] killsSinceLastRoll = new int[ElementCount];

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        if (DemoMode.On) maxStage1Kills = DemoMode.Cfg.stage1Kills;   // [시연 모드] 몇 마리만 잡고 보스로
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    /// <summary>
    /// 몹이 죽을 때 EnemyController가 호출. 드랍할 스펠을 반환 (없으면 null)
    /// </summary>
    public SpellData RegisterKill(EnemyController enemy)
    {
        int e = (int)enemy.enemyType;
        int r = (int)enemy.rank;

        TotalKills++;
        killTable[e, r]++;

        // 서버 저장용 점수/킬 수 (GameManager가 씬에 있을 때만)
        if (GameManager.Instance != null)
        {
            GameManager.Instance.AddMonsterKill(GetScore(enemy.rank));
        }

        OnKillCountChanged?.Invoke(TotalKills, maxStage1Kills);
        if (TotalKills == maxStage1Kills)
        {
            Debug.Log("[GameSession] 스테이지 1 최대 처치 수 도달!");
            OnStage1KillLimitReached?.Invoke();
        }

        return RollDrop(enemy.enemyType);
    }

    private SpellData RollDrop(MagicElement element)
    {
        // [시연 모드] 잠가 둔 스펠을 아직 안 배웠으면 반드시 드랍 (속성 상관없이)
        if (DemoMode.On)
        {
            SkillManager sm = SkillManager.Instance;
            SpellData locked = sm != null ? DemoMode.LockedSpell(sm.spellDatabase) : null;
            return locked != null && !sm.IsUnlocked(locked) ? locked : null;
        }

        if (element == MagicElement.Normal) return null;

        int e = (int)element;
        killsSinceLastRoll[e]++;
        if (killsSinceLastRoll[e] < killsPerDropRoll) return null;

        killsSinceLastRoll[e] = 0;
        if (UnityEngine.Random.value > dropChance) return null;

        return SkillManager.Instance != null ? SkillManager.Instance.PickDropSpell(element) : null;
    }

    private int GetScore(MonsterRank rank)
    {
        switch (rank)
        {
            case MonsterRank.Mid: return midRankScore;
            case MonsterRank.High: return highRankScore;
            default: return lowRankScore;
        }
    }

    // Monster 패널(도감)에서 사용
    public int GetKillCount(MagicElement element, MonsterRank rank) => killTable[(int)element, (int)rank];

    public int GetKillCount(MagicElement element)
    {
        int sum = 0;
        for (int r = 0; r < RankCount; r++) sum += killTable[(int)element, r];
        return sum;
    }

    // 재시작 시 초기화
    public void ResetSession()
    {
        TotalKills = 0;
        killTable = new int[ElementCount, RankCount];
        killsSinceLastRoll = new int[ElementCount];
        OnKillCountChanged?.Invoke(TotalKills, maxStage1Kills);
    }
}