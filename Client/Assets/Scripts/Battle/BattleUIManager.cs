using UnityEngine;
using TMPro;
using System.Collections;
using System.Collections.Generic;
using System.Text.RegularExpressions;

public class BattleUIManager : MonoBehaviour
{
    public static BattleUIManager Instance; // 싱글톤 접근용

    [Header("UI 연결")]
    public TextMeshProUGUI effectivenessText; // "효과가 굉장했다!"를 띄울 Text 오브젝트
    [Tooltip("\"하급 물 속성 몬스터 처치!\" 자막 (없으면 비워두기)")]
    public TextMeshProUGUI killText;
    [Tooltip("\"OOO 스펠을 획득했습니다!\" 알림 (없으면 비워두기)")]
    public TextMeshProUGUI noticeText;

    [Header("표시 시간")]
    public float effectivenessDuration = 2.0f;
    public float killDuration = 1.5f;
    [Tooltip("알림 기본 표시 시간 (초). 긴 문장은 아래 글자당 시간만큼 더 보여 줌")]
    public float noticeDuration = 2.0f;
    [Tooltip("글자 하나당 더 보여 줄 시간 (초). 긴 알림일수록 오래 남음")]
    public float noticeTimePerChar = 0.05f;
    [Tooltip("알림 최대 표시 시간 (초)")]
    public float noticeMaxDuration = 5f;
    [Tooltip("다음 알림이 기다리고 있어도 최소 이 시간은 보여 준 뒤 넘김 (초)")]
    public float noticeMinTime = 1.6f;
    [Tooltip("알림이 사라질 때 페이드아웃 시간")]
    public float noticeFadeDuration = 0.5f;

    // 텍스트마다 코루틴을 따로 관리 (StopAllCoroutines를 쓰면 서로 끊기기 때문)
    private Coroutine effectivenessRoutine;
    private Coroutine killRoutine;
    private Coroutine noticeRoutine;
    // 알림은 줄을 세워 차례로 보여 줌 (새 알림이 바로 앞 알림을 덮어 읽을 틈이 없던 문제)
    private readonly Queue<string> noticeQueue = new Queue<string>();
    private string currentNotice;
    private static readonly Regex RichTag = new Regex("<[^>]+>");

    void Awake()
    {
        Instance = this;
        if (effectivenessText != null) effectivenessText.gameObject.SetActive(false);
        if (killText != null) killText.gameObject.SetActive(false);
        if (noticeText != null) noticeText.gameObject.SetActive(false);
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    // 외부에서 상성 배율을 전달받아 텍스트를 출력하는 함수
    public void ShowEffectiveness(float multiplier)
    {
        if (effectivenessText == null) return;

        string message;
        if (multiplier >= 1.5f)
            message = "<color=#FF5555>It's super effective!</color>";   // 빨간색 계열
        else if (multiplier <= 0.5f)
            message = "<color=#C8C8C8>It's not very effective...</color>"; // 밝은 회색 (흰색보다 옅게, 잘 보이게)
        else
            message = "<color=#FFFFFF>It's effective.</color>";      // 흰색

        Restart(ref effectivenessRoutine, ShowFor(effectivenessText, message, effectivenessDuration, 0f));
    }

    // 몹 처치 자막
    public void ShowKill(string message)
    {
        if (killText == null) return;
        Restart(ref killRoutine, ShowFor(killText, message, killDuration, 0f));
    }

    // 스펠 획득 등 화면 중앙 알림 (페이드아웃)
    public void ShowNotice(string message)
    {
        if (noticeText == null)
        {
            Debug.Log($"[알림] {message}");
            return;
        }
        if (string.IsNullOrEmpty(message)) return;
        // 같은 알림이 지금 떠 있거나 이미 줄에 있으면 또 넣지 않음
        if (message == currentNotice || noticeQueue.Contains(message)) return;
        noticeQueue.Enqueue(message);
        while (noticeQueue.Count > 3) noticeQueue.Dequeue();   // 너무 밀리면 오래된 것부터 버림
        if (noticeRoutine == null) noticeRoutine = StartCoroutine(NoticeLoop());
    }

    // 지금 떠 있는 알림과 줄 선 알림을 모두 지움 (씬 전환 등)
    public void ClearNotices()
    {
        noticeQueue.Clear();
        if (noticeRoutine != null) StopCoroutine(noticeRoutine);
        noticeRoutine = null;
        currentNotice = null;
        if (noticeText != null) { noticeText.gameObject.SetActive(false); noticeText.alpha = 1f; }
    }

    // 문장 길이에 맞춘 표시 시간 (색 태그 등은 글자 수에서 뺌)
    private float NoticeTime(string message)
    {
        int chars = RichTag.Replace(message, "").Length;
        return Mathf.Clamp(noticeDuration + chars * noticeTimePerChar, noticeDuration, Mathf.Max(noticeDuration, noticeMaxDuration));
    }

    // 슬로 모션(페이즈 연출) 중에도 실제 시간 기준으로 보여 줌
    private IEnumerator NoticeLoop()
    {
        while (noticeQueue.Count > 0)
        {
            string message = noticeQueue.Dequeue();
            currentNotice = message;
            noticeText.text = message;
            noticeText.alpha = 1f;
            noticeText.gameObject.SetActive(true);

            float duration = NoticeTime(message);
            for (float t = 0f; t < duration; t += Time.unscaledDeltaTime)
            {
                if (noticeQueue.Count > 0 && t >= noticeMinTime) break;   // 다음 알림이 기다리면 최소 시간 뒤 넘김
                yield return null;
            }

            if (noticeQueue.Count == 0 && noticeFadeDuration > 0f)
            {
                for (float t = 0f; t < noticeFadeDuration; t += Time.unscaledDeltaTime)
                {
                    if (noticeQueue.Count > 0) break;   // 사라지는 중에 새 알림이 오면 바로 다음으로
                    noticeText.alpha = Mathf.Lerp(1f, 0f, t / noticeFadeDuration);
                    yield return null;
                }
            }
        }
        noticeText.gameObject.SetActive(false);
        noticeText.alpha = 1f;
        currentNotice = null;
        noticeRoutine = null;
    }

    private void Restart(ref Coroutine handle, IEnumerator routine)
    {
        if (handle != null) StopCoroutine(handle);
        handle = StartCoroutine(routine);
    }

    private IEnumerator ShowFor(TextMeshProUGUI text, string message, float duration, float fadeDuration)
    {
        text.text = message;
        text.alpha = 1f;
        text.gameObject.SetActive(true);

        yield return new WaitForSeconds(duration);

        if (fadeDuration > 0f)
        {
            float t = 0f;
            while (t < fadeDuration)
            {
                t += Time.deltaTime;
                text.alpha = Mathf.Lerp(1f, 0f, t / fadeDuration);
                yield return null;
            }
        }

        text.gameObject.SetActive(false);
        text.alpha = 1f;
    }
}