using UnityEngine;
using TMPro;
using System.Collections;

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
    public float noticeDuration = 2.0f;
    [Tooltip("알림이 사라질 때 페이드아웃 시간")]
    public float noticeFadeDuration = 0.5f;

    // 텍스트마다 코루틴을 따로 관리 (StopAllCoroutines를 쓰면 서로 끊기기 때문)
    private Coroutine effectivenessRoutine;
    private Coroutine killRoutine;
    private Coroutine noticeRoutine;

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
            message = "<color=#AAAAAA>It's not very effective...</color>"; // 회색 계열
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
        Restart(ref noticeRoutine, ShowFor(noticeText, message, noticeDuration, noticeFadeDuration));
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
