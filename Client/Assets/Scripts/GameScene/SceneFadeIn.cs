using System.Collections;
using UnityEngine;
using UnityEngine.UI;

public class SceneFadeIn : MonoBehaviour
{
    [Header("페이드 인 설정")]
    public Image fadeScreen;
    public float fadeDuration = 1.5f; // 화면이 밝아지는 시간 (1.5초)

    void Start()
    {
        if (fadeScreen != null)
        {
            // 씬이 열리자마자 화면을 완벽한 검은색(Alpha 1)으로 강제 고정
            Color c = fadeScreen.color;
            c.a = 1f;
            fadeScreen.color = c;
            fadeScreen.gameObject.SetActive(true);

            // 즉시 페이드인 시작
            StartCoroutine(FadeInRoutine());
        }
    }

    private IEnumerator FadeInRoutine()
    {
        float elapsedTime = 0f;
        Color fadeColor = fadeScreen.color;

        // 1.5초 동안 서서히 투명해짐
        while (elapsedTime < fadeDuration)
        {
            fadeColor.a = Mathf.Lerp(1f, 0f, elapsedTime / fadeDuration);
            fadeScreen.color = fadeColor;
            elapsedTime += Time.deltaTime;
            yield return null;
        }

        // 연출 완료 후 UI 비활성화 (클릭 방해 방지)
        fadeColor.a = 0f;
        fadeScreen.color = fadeColor;
        fadeScreen.gameObject.SetActive(false);
    }
}