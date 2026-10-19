using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class CameraTransitionManager : MonoBehaviour
{
    [Header("카메라 설정")]
    public Camera mainCamera;

    [Header("도착지 좌표 오브젝트")]
    public Transform throneZoomTarget;
    public Transform firstPersonTarget;

    [Header("UI 제어")]
    public CanvasGroup titleUIGroup;
    public Image blackFadeScreen;

    [Header("카메라 연출 시간 설정")]
    public float uiFadeDuration = 0.5f;
    public float zoomDuration = 2.0f;
    public float holdDuration = 1.0f;
    public float firstPersonDuration = 1.5f;

    [Header("화면 페이드 연출 시간 (요청하신 타이밍)")]
    public float delayBeforeFadeOut = 0.5f;      // 1. 1인칭 전환 후 대기 시간 (0.5초)
    public float screenFadeOutDuration = 1.5f;   // 2. 화면 페이드 아웃 시간 (1.5초)
    public float blackScreenHoldDuration = 1.0f; // 3. 검은 화면 유지 시간 (1.0초)

    private void Start()
    {
        // 타이틀 씬 시작 시 검은 화면을 완벽하게 숨김 (시작 시 페이드인 방지)
        if (blackFadeScreen != null)
        {
            Color c = blackFadeScreen.color;
            c.a = 0f;
            blackFadeScreen.color = c;
            blackFadeScreen.gameObject.SetActive(false);
        }
    }

    public void StartGameTransition()
    {
        StartCoroutine(TransitionSequence());
    }

    private IEnumerator TransitionSequence()
    {
        if (titleUIGroup != null) StartCoroutine(FadeOutUI());

        Vector3 startPos = mainCamera.transform.position;
        Quaternion startRot = mainCamera.transform.rotation;
        float elapsedTime = 0f;

        // 줌인 연출
        while (elapsedTime < zoomDuration)
        {
            float t = Mathf.SmoothStep(0f, 1f, elapsedTime / zoomDuration);
            mainCamera.transform.position = Vector3.Lerp(startPos, throneZoomTarget.position, t);
            mainCamera.transform.rotation = Quaternion.Slerp(startRot, throneZoomTarget.rotation, t);
            elapsedTime += Time.deltaTime;
            yield return null;
        }
        mainCamera.transform.position = throneZoomTarget.position;
        mainCamera.transform.rotation = throneZoomTarget.rotation;

        // 왕좌 앞 대기
        yield return new WaitForSeconds(holdDuration);

        elapsedTime = 0f;
        Vector3 zoomPos = mainCamera.transform.position;
        Quaternion zoomRot = mainCamera.transform.rotation;

        // 1인칭 회전 연출
        while (elapsedTime < firstPersonDuration)
        {
            float t = Mathf.SmoothStep(0f, 1f, elapsedTime / firstPersonDuration);
            mainCamera.transform.position = Vector3.Lerp(zoomPos, firstPersonTarget.position, t);
            mainCamera.transform.rotation = Quaternion.Slerp(zoomRot, firstPersonTarget.rotation, t);
            elapsedTime += Time.deltaTime;
            yield return null;
        }
        mainCamera.transform.position = firstPersonTarget.position;
        mainCamera.transform.rotation = firstPersonTarget.rotation;

        // 1. 1인칭 완료 후 잠깐 대기 (0.5초)
        yield return new WaitForSeconds(delayBeforeFadeOut);

        // 2. 검은 화면으로 페이드 아웃 (1.5초)
        if (blackFadeScreen != null)
        {
            blackFadeScreen.gameObject.SetActive(true);
            elapsedTime = 0f;
            Color fadeColor = blackFadeScreen.color;

            while (elapsedTime < screenFadeOutDuration)
            {
                fadeColor.a = Mathf.Lerp(0f, 1f, elapsedTime / screenFadeOutDuration);
                blackFadeScreen.color = fadeColor;
                elapsedTime += Time.deltaTime;
                yield return null;
            }
            fadeColor.a = 1f;
            blackFadeScreen.color = fadeColor;
        }

        // 3. 완전히 까매진 상태로 대기 (1.0초)
        yield return new WaitForSeconds(blackScreenHoldDuration);

        // 4. 게임 씬 로드
        SceneManager.LoadScene("GameScene");
    }

    private IEnumerator FadeOutUI()
    {
        float elapsedTime = 0f;
        while (elapsedTime < uiFadeDuration)
        {
            titleUIGroup.alpha = Mathf.Lerp(1f, 0f, elapsedTime / uiFadeDuration);
            elapsedTime += Time.deltaTime;
            yield return null;
        }
        titleUIGroup.alpha = 0f;
        titleUIGroup.gameObject.SetActive(false);
    }
}