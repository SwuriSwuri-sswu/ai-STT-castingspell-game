using UnityEngine;

// 스테이지 사이에 넘겨 줄 정보 + 영구 기록 (씬이 바뀌어도 유지)
//   - BossCleared : 보스를 한 번이라도 이긴 적이 있는지 (PlayerPrefs, 2회차 '스테이지 1 건너뛰기'용)
//   - 이번 판 스테이지 1 결과 (처치 수, 넘어갈 때 체력 비율) → BossScene에서 필요하면 사용
public static class StageProgress
{
    private const string BossClearedKey = "BossCleared";

    // 보스 클리어 이력 (BossScene에서 승리 시 MarkBossCleared() 호출)
    public static bool BossCleared => PlayerPrefs.GetInt(BossClearedKey, 0) == 1;

    public static void MarkBossCleared()
    {
        PlayerPrefs.SetInt(BossClearedKey, 1);
        PlayerPrefs.Save();
    }

    public static void ClearBossRecord()
    {
        PlayerPrefs.DeleteKey(BossClearedKey);
        PlayerPrefs.Save();
    }

    // 이번 판 스테이지 1 결과
    public static int Stage1Kills { get; set; }
    // 스테이지 1을 끝낼 때의 체력 비율 (0~1). 보스전을 이어진 체력으로 시작하고 싶을 때만 사용. -1 = 기록 없음
    public static float Stage1HpRatio { get; set; } = -1f;
    // 스테이지 1을 건너뛰고 바로 보스로 갔는지
    public static bool SkippedStage1 { get; set; }

    public static void ResetRun()
    {
        Stage1Kills = 0;
        Stage1HpRatio = -1f;
        SkippedStage1 = false;
    }
}