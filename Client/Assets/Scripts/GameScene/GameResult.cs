using UnityEngine;

// 한 판의 결과 (GameScene → EndingScene으로 전달)
// 씬이 바뀌어도 유지되도록 static으로 보관합니다.
public static class GameResult
{
    public static bool HasResult { get; private set; }
    public static bool Cleared { get; private set; }      // true = 클리어, false = 게임 오버
    public static int Kills { get; private set; }
    public static int Score { get; private set; }
    public static float PlaySeconds { get; private set; }
    public static int LearnedSpells { get; private set; }

    public static void Record(bool cleared, float playSeconds)
    {
        HasResult = true;
        Cleared = cleared;
        PlaySeconds = playSeconds;
        Kills = GameSessionManager.Instance != null ? GameSessionManager.Instance.TotalKills : 0;
        Score = GameManager.Instance != null ? GameManager.Instance.playScore + GameManager.Instance.voiceScore : 0;
        LearnedSpells = SkillManager.Instance != null ? SkillManager.Instance.UnlockedSpells.Count : 0;
        Debug.Log($"[GameResult] {(cleared ? "클리어" : "게임 오버")} / 처치 {Kills} / 점수 {Score} / {playSeconds:F0}초");
    }

    public static string PlayTimeText
    {
        get
        {
            int s = Mathf.Max(0, Mathf.FloorToInt(PlaySeconds));
            return $"{s / 60:00}:{s % 60:00}";
        }
    }
}