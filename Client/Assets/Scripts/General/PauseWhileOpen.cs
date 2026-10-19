using System.Collections.Generic;
using UnityEngine;

// 이 오브젝트(패널)가 켜져 있는 동안 게임 일시정지
// BookPanel(도감), SettingPanel(설정) 등에 붙이기
//   - 몹 이동·공격, 마법 투사체, 드랍 연출 등 게임 시간이 멈춤 (Time.timeScale = 0)
//   - 음성 영창도 받지 않음 (SkillManager.CanCast가 false)
//   - UI 연출·BGM은 그대로
// 패널이 여러 개 동시에 열려도, 전부 닫혀야 다시 진행됩니다.
public class PauseWhileOpen : MonoBehaviour
{
    private void OnEnable() => GamePause.Request(this);
    private void OnDisable() => GamePause.Release(this);
}

public static class GamePause
{
    private static readonly HashSet<Object> holders = new HashSet<Object>();
    private static float resumeScale = 1f;

    public static bool IsPaused => holders.Count > 0;

    public static void Request(Object holder)
    {
        if (holder == null) return;
        if (holders.Count == 0)
        {
            resumeScale = Time.timeScale > 0f ? Time.timeScale : 1f;
            Time.timeScale = 0f;
        }
        holders.Add(holder);
    }

    public static void Release(Object holder)
    {
        if (!holders.Remove(holder)) return;
        if (holders.Count == 0) Time.timeScale = resumeScale;
    }
}