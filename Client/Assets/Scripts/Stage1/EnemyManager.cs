using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public class EnemySpawnEntry
{
    public EnemyController prefab;
    [Tooltip("등장 확률 가중치 (클수록 자주 등장, 0이면 등장 안 함)")]
    [Min(0f)] public float weight = 1f;
    [Tooltip("총 처치 수가 이 값 이상이 되어야 등장 (예: 중급 5, 상급 15)")]
    [Min(0)] public int minKillsToAppear = 0;
}

// 스테이지 1 몹 스폰 담당 (다수 동시 등장 구조)
// 스폰포인트(Spawn Points) 개수만큼 몹이 동시에 등장 → 한 마리가 처치되어 사라지면 그 자리에 다음 몹 등장
// 스테이지 1 최대 처치 수(50)에 도달하면 더 이상 스폰하지 않음
public class EnemyManager : MonoBehaviour
{
    public static EnemyManager Instance { get; private set; }

    [Header("첫 번째 몹 (튜토리얼)")]
    [Tooltip("게임 시작 시 무조건 처음 등장할 몹 (노말 하급 추천). 비우면 랜덤")]
    public EnemyController firstEnemy;

    [Header("몹 프리팹 한 번에 등록 (추천)")]
    [Tooltip("Project 창에서 몹 프리팹을 여러 개 선택해 이 목록 이름 위로 드래그하면 한 번에 등록됩니다.\n" +
             "등장 확률·등장 시점은 각 몹의 등급(Rank)에 따라 아래 값으로 자동 적용")]
    public List<EnemyController> quickPrefabs = new List<EnemyController>();

    [Header("등급별 자동 값 (Quick Prefabs에 적용)")]
    [Tooltip("등장 확률 가중치 (하급 : 중급 : 상급 = 5 : 3 : 1)")]
    [Min(0f)] public float lowWeight = 5f;
    [Min(0f)] public float midWeight = 3f;
    [Min(0f)] public float highWeight = 1f;
    [Tooltip("총 처치 수가 이 값 이상이 되어야 등장")]
    [Min(0)] public int lowMinKills = 0;
    [Min(0)] public int midMinKills = 5;
    [Min(0)] public int highMinKills = 15;

    [Header("개별 설정 (선택: 특정 몹만 값을 다르게 하고 싶을 때)")]
    [Tooltip("여기에 넣은 몹은 위 자동 값 대신 여기 적은 값을 사용합니다.")]
    public List<EnemySpawnEntry> enemyPrefabs = new List<EnemySpawnEntry>();

    // 실제 스폰에 쓰는 목록 (개별 설정 + 자동 등록)
    private readonly List<EnemySpawnEntry> spawnPool = new List<EnemySpawnEntry>();

    [Header("스폰 위치")]
    // [변경] 스폰포인트를 여러 개 받음. 개수 = 동시에 나오는 몹 수 (5개 넣으면 5마리)
    [Tooltip("몹이 최종적으로 멈춰 서서 싸우는 위치들 (빈 오브젝트).\n" +
             "넣은 개수만큼 몹이 동시에 등장합니다 (5개 = 5마리)")]
    public Transform[] spawnPoints;
    [Tooltip("(예전 설정 호환) Spawn Points가 비어 있으면 이 한 곳만 사용 = 1마리씩")]
    public Transform spawnPoint;
    [Tooltip("등장 경로. 첫 번째 = 몹이 생성될 곳 (성 밖, 문에 가려진 곳), 이후 = 거쳐갈 지점 (예: 성문).\n" +
             "마지막엔 자동으로 각자의 Spawn Point까지 이동합니다. 비워두면 Spawn Point에 바로 생성")]
    public Transform[] approachPath;
    [Tooltip("몹이 바라볼 대상 (왕좌/플레이어). 비우면 메인 카메라")]
    public Transform lookTarget;
    [Tooltip("등장 이동 속도 배율. 몹 프리팹의 Move Speed에 곱해집니다 (2 = 두 배 빠르게)")]
    [Min(0.1f)] public float approachSpeedMultiplier = 2f;

    [Header("등장 자막")]
    [Tooltip("몹이 나타날 때 처치 자막 자리에 'Demon이 나타났습니다!' 표시")]
    public bool showAppearText = true;
    [Tooltip("처음 만난 몹이면 아래에 작게 'New!'")]
    public string newEncounterText = "New!";
    public Color newEncounterColor = new Color(1f, 0.84f, 0.3f);

    [Header("스폰 타이밍")]
    public bool spawnOnStart = true;
    [Tooltip("게임 시작 후 첫 몹이 등장하기까지")]
    public float firstSpawnDelay = 2f;
    [Tooltip("몹이 사라진 뒤 그 자리에 다음 몹이 등장하기까지 (최소)")]
    public float nextSpawnDelay = 1.5f;
    // [추가] 자리마다 등장 시간을 랜덤으로 → 일렬로 한꺼번에 들어오지 않게
    [Tooltip("게임 시작 때 자리마다 First Spawn Delay에 0 ~ 이 값(초) 사이 랜덤 시간을 더함")]
    [Min(0f)] public float initialSpawnSpread = 8f;
    [Tooltip("몹이 사라진 뒤 다음 몹이 나올 때 Next Spawn Delay에 0 ~ 이 값(초) 사이 랜덤 시간을 더함")]
    [Min(0f)] public float respawnRandomExtra = 4f;

    [Header("등장 규칙")]
    [Tooltip("같은 프리팹이 연속으로 나오지 않게 (후보가 2개 이상일 때)")]
    public bool avoidSameTwiceInARow = true;

    // [추가] 초반 난이도: 처음 N마리는 공격력을 조금 낮춤
    [Header("초반 난이도")]
    [Tooltip("게임 시작부터 이 마리 수까지 등장한 몹은 공격력이 낮아짐 (0이면 끔)")]
    [Min(0)] public int easyMonsterCount = 10;
    [Tooltip("초반 하급 몹 공격력 감소량")]
    [Min(0f)] public float easyLowDamageCut = 1f;
    [Tooltip("초반 중급·상급 몹 공격력 감소량")]
    [Min(0f)] public float easyMidHighDamageCut = 2f;

    // 지금까지 등장한 몹 수 (재시작 시 0)
    public int SpawnedCount { get; private set; }

    // 새 몹이 생성됐을 때 (이동 시작 전)
    public event Action<EnemyController> OnEnemySpawned;
    // 몹이 스폰포인트에 도착해 마법 시전이 가능해졌을 때 (UI 안내 등에 사용)
    public event Action<EnemyController> OnEnemyReady;

    // [변경] 자리(스폰포인트)마다 한 마리씩
    private EnemyController[] slots;
    private Coroutine[] slotRoutines;

    // 지금 맵에 있는 몹들 (빈 자리는 null)
    public IReadOnlyList<EnemyController> CurrentEnemies => slots;

    // [호환] 예전 코드가 CurrentEnemy를 쓰고 있어도 깨지지 않게: 살아 있는 몹 중 하나를 돌려줌
    public EnemyController CurrentEnemy
    {
        get
        {
            if (slots == null) return null;
            foreach (EnemyController e in slots)
                if (e != null && !e.IsDying) return e;
            foreach (EnemyController e in slots)
                if (e != null) return e;
            return null;
        }
    }

    public int AliveCount
    {
        get
        {
            int n = 0;
            if (slots != null) foreach (EnemyController e in slots) if (e != null) n++;
            return n;
        }
    }

    public bool IsRunning { get; private set; }

    private EnemyController lastPrefab;
    private bool firstSpawned;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        InitSlots();
    }

    // 스폰포인트 개수만큼 자리 만들기
    private void InitSlots()
    {
        if (spawnPoints == null || spawnPoints.Length == 0)
        {
            spawnPoints = new[] { spawnPoint != null ? spawnPoint : transform };
        }
        slots = new EnemyController[spawnPoints.Length];
        slotRoutines = new Coroutine[spawnPoints.Length];
    }

    private void Start()
    {
        if (lookTarget == null && Camera.main != null) lookTarget = Camera.main.transform;
        BuildSpawnPool();

        if (spawnOnStart) StartSpawning();
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    // 스폰 시작 (빈 자리마다 차례로 등장)
    public void StartSpawning()
    {
        if (IsRunning) return;
        IsRunning = true;

        // 자리마다 랜덤한 시간에 등장 (첫 자리 하나는 바로 나오게 해서 시작이 심심하지 않게)
        int firstSlot = UnityEngine.Random.Range(0, slots.Length);
        for (int i = 0; i < slots.Length; i++)
        {
            if (slots[i] != null) continue;
            float extra = (i == firstSlot) ? 0f : UnityEngine.Random.Range(0f, initialSpawnSpread);
            ScheduleSpawn(i, firstSpawnDelay + extra);
        }
    }

    // 다음 몹이 더 이상 나오지 않게 (현재 몹들은 그대로)
    public void StopSpawning()
    {
        IsRunning = false;
        if (slotRoutines == null) return;
        for (int i = 0; i < slotRoutines.Length; i++)
        {
            if (slotRoutines[i] != null)
            {
                StopCoroutine(slotRoutines[i]);
                slotRoutines[i] = null;
            }
        }
    }

    // 스테이지 전환/재시작 시 현재 몹 전부 제거
    public void ClearCurrentEnemy()
    {
        if (slots == null) return;
        for (int i = 0; i < slots.Length; i++)
        {
            EnemyController e = slots[i];
            if (e == null) continue;
            e.OnDespawned -= HandleEnemyDespawned;
            e.OnReady -= HandleEnemyReady;
            Destroy(e.gameObject);
            slots[i] = null;
        }
    }

    // 재시작 시 첫 몹부터 다시
    public void ResetSpawner()
    {
        StopSpawning();
        ClearCurrentEnemy();
        firstSpawned = false;
        lastPrefab = null;
        SpawnedCount = 0;
    }

    private void ScheduleSpawn(int slot, float delay)
    {
        if (slotRoutines[slot] != null) StopCoroutine(slotRoutines[slot]);
        slotRoutines[slot] = StartCoroutine(SpawnAfter(slot, delay));
    }

    private IEnumerator SpawnAfter(int slot, float delay)
    {
        yield return new WaitForSeconds(delay);
        slotRoutines[slot] = null;

        if (!IsRunning || slots[slot] != null) yield break;

        GameSessionManager session = GameSessionManager.Instance;
        if (session != null && session.IsStage1LimitReached)
        {
            // 50마리 달성 → 스테이지 2 전환은 GameSessionManager.OnStage1KillLimitReached에서 처리
            IsRunning = false;
            yield break;
        }

        SpawnOne(slot);
    }

    private void SpawnOne(int slot)
    {
        Transform point = spawnPoints[slot] != null ? spawnPoints[slot] : transform;

        EnemyController prefab;
        if (DemoMode.On)
        {
            // [시연 모드] 하급 → 중급 → 상급 순서대로
            int kills = GameSessionManager.Instance != null ? GameSessionManager.Instance.TotalKills : 0;
            List<EnemyController> pool = new List<EnemyController>();
            foreach (EnemySpawnEntry e in spawnPool) if (e != null) pool.Add(e.prefab);
            prefab = DemoMode.PickMob(kills, pool, lastPrefab);
            if (prefab == null) prefab = PickPrefab();
        }
        else if (!firstSpawned && firstEnemy != null)
        {
            prefab = firstEnemy;
        }
        else
        {
            prefab = PickPrefab();
        }
        firstSpawned = true;

        if (prefab == null)
        {
            Debug.LogWarning("[EnemyManager] 스폰할 몹 프리팹이 등록되지 않았습니다.");
            return;
        }

        // 이동 경로 구성: [생성 위치] → (중간 지점들) → 이 자리의 스폰포인트
        List<Vector3> waypoints = new List<Vector3>();
        if (approachPath != null)
        {
            foreach (Transform p in approachPath)
            {
                if (p != null) waypoints.Add(p.position);
            }
        }
        bool hasApproach = waypoints.Count > 0;

        Vector3 position = hasApproach ? waypoints[0] : point.position;
        if (hasApproach)
        {
            waypoints.RemoveAt(0);         // 첫 지점은 생성 위치
            waypoints.Add(point.position); // 마지막은 이 자리의 스폰포인트
        }

        // 처음 바라볼 방향: 이동이 있으면 다음 지점, 없으면 플레이어
        Vector3 faceTarget = hasApproach ? waypoints[0]
                           : (lookTarget != null ? lookTarget.position : position + point.forward);
        Quaternion rotation = point.rotation;
        Vector3 dir = faceTarget - position;
        dir.y = 0f; // 위아래로 기울지 않게
        if (dir.sqrMagnitude > 0.0001f) rotation = Quaternion.LookRotation(dir);

        EnemyController enemy = Instantiate(prefab, position, rotation);
        slots[slot] = enemy;

        // [추가] 초반 N마리는 공격력 감소 (최소 1)
        SpawnedCount++;
        if (SpawnedCount <= easyMonsterCount)
        {
            float cut = enemy.rank == MonsterRank.Low ? easyLowDamageCut : easyMidHighDamageCut;
            enemy.attackDamage = Mathf.Max(1f, enemy.attackDamage - cut);
        }
        enemy.OnDespawned += HandleEnemyDespawned;
        enemy.OnReady += HandleEnemyReady;
        lastPrefab = prefab;

        Debug.Log($"[EnemyManager] {slot}번 자리: {EnemyController.RankName(enemy.rank)} " +
                  $"{EnemyController.ElementName(enemy.enemyType)} 속성 몬스터 등장!");
        // 몹 도감 기록 + 등장 자막
        bool isNew = MonsterCollection.Encounter(prefab.gameObject.name);
        if (showAppearText && BattleUIManager.Instance != null)
        {
            BattleUIManager.Instance.ShowKill(BuildAppearText(enemy, isNew));
        }
        OnEnemySpawned?.Invoke(enemy);
        SoundManager.Play(L => L.monsterAppear);

        Vector3 lookAt = lookTarget != null ? lookTarget.position : point.position + point.forward;
        if (hasApproach) enemy.BeginApproach(waypoints, lookAt, approachSpeedMultiplier);
        else HandleEnemyReady(enemy); // 이동 없이 바로 전투 가능
    }

    // "Demon이 나타났습니다!" (이름은 속성 색) + 처음이면 작은 New!
    private string BuildAppearText(EnemyController enemy, bool isNew)
    {
        string name = enemy.DisplayName;
        string hex = ColorUtility.ToHtmlStringRGB(EnemyController.ElementColor(enemy.enemyType));
        string text = $"<color=#{hex}>{name}</color> appeared!";
        if (isNew && !string.IsNullOrEmpty(newEncounterText))
        {
            string newHex = ColorUtility.ToHtmlStringRGB(newEncounterColor);
            text += $"\n<size=60%><color=#{newHex}>{newEncounterText}</color></size>";
        }
        return text;
    }

    // 등록된 모든 몹 프리팹 (첫 몹 + 개별 설정 + Quick Prefabs, 중복 없이). 몹 도감·사진 촬영용
    public List<EnemyController> AllPrefabs
    {
        get
        {
            List<EnemyController> list = new List<EnemyController>();
            if (firstEnemy != null) list.Add(firstEnemy);
            foreach (EnemySpawnEntry e in enemyPrefabs)
                if (e != null && e.prefab != null && !list.Contains(e.prefab)) list.Add(e.prefab);
            foreach (EnemyController p in quickPrefabs)
                if (p != null && !list.Contains(p)) list.Add(p);
            return list;
        }
    }

    private void HandleEnemyReady(EnemyController enemy)
    {
        enemy.OnReady -= HandleEnemyReady;
        Debug.Log("[EnemyManager] 몬스터가 자리에 도착했습니다. 마법 시전 가능!");
        OnEnemyReady?.Invoke(enemy);
    }

    // 몹이 Death 애니메이션·드랍까지 끝내고 사라지면 그 자리에 다음 몹 예약
    private void HandleEnemyDespawned(EnemyController enemy)
    {
        enemy.OnDespawned -= HandleEnemyDespawned;

        int slot = Array.IndexOf(slots, enemy);
        if (slot < 0) return;
        slots[slot] = null;

        if (IsRunning) ScheduleSpawn(slot, nextSpawnDelay + UnityEngine.Random.Range(0f, respawnRandomExtra));
    }

    // 개별 설정을 먼저 넣고, Quick Prefabs는 등급별 자동 값으로 추가 (중복은 개별 설정 우선)
    public void BuildSpawnPool()
    {
        spawnPool.Clear();
        HashSet<EnemyController> added = new HashSet<EnemyController>();

        foreach (EnemySpawnEntry entry in enemyPrefabs)
        {
            if (entry == null || entry.prefab == null || !added.Add(entry.prefab)) continue;
            spawnPool.Add(entry);
        }

        foreach (EnemyController prefab in quickPrefabs)
        {
            if (prefab == null || !added.Add(prefab)) continue;
            spawnPool.Add(new EnemySpawnEntry
            {
                prefab = prefab,
                weight = RankWeight(prefab.rank),
                minKillsToAppear = RankMinKills(prefab.rank),
            });
        }

        Debug.Log($"[EnemyManager] 스폰 후보 {spawnPool.Count}종 등록");
    }

    private float RankWeight(MonsterRank rank)
    {
        switch (rank)
        {
            case MonsterRank.Mid: return midWeight;
            case MonsterRank.High: return highWeight;
            default: return lowWeight;
        }
    }

    private int RankMinKills(MonsterRank rank)
    {
        switch (rank)
        {
            case MonsterRank.Mid: return midMinKills;
            case MonsterRank.High: return highMinKills;
            default: return lowMinKills;
        }
    }

    private EnemyController PickPrefab()
    {
        int kills = GameSessionManager.Instance != null ? GameSessionManager.Instance.TotalKills : 0;

        // 1차: 등장 조건 충족 + 직전 몹 제외
        EnemyController picked = PickWeighted(kills, avoidSameTwiceInARow ? lastPrefab : null);
        // 2차: 후보가 직전 몹 하나뿐이면 연속 등장 허용
        if (picked == null) picked = PickWeighted(kills, null);
        // 3차: 아직 아무도 등장 조건을 못 채웠으면 가장 빨리 등장하는 몹
        if (picked == null) picked = EarliestEntry();

        return picked;
    }

    private EnemyController PickWeighted(int kills, EnemyController exclude)
    {
        float total = 0f;
        foreach (EnemySpawnEntry entry in spawnPool)
        {
            if (IsCandidate(entry, kills, exclude)) total += entry.weight;
        }
        if (total <= 0f) return null;

        float roll = UnityEngine.Random.Range(0f, total);
        EnemyController picked = null;
        foreach (EnemySpawnEntry entry in spawnPool)
        {
            if (!IsCandidate(entry, kills, exclude)) continue;
            picked = entry.prefab;
            roll -= entry.weight;
            if (roll <= 0f) break;
        }
        return picked;
    }

    private static bool IsCandidate(EnemySpawnEntry entry, int kills, EnemyController exclude)
    {
        if (entry == null || entry.prefab == null || entry.weight <= 0f) return false;
        if (kills < entry.minKillsToAppear) return false;
        return entry.prefab != exclude;
    }

    private EnemyController EarliestEntry()
    {
        EnemySpawnEntry best = null;
        foreach (EnemySpawnEntry entry in spawnPool)
        {
            if (entry == null || entry.prefab == null || entry.weight <= 0f) continue;
            if (best == null || entry.minKillsToAppear < best.minKillsToAppear) best = entry;
        }
        return best != null ? best.prefab : null;
    }
}