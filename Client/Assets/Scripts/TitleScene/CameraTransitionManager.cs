using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class CameraTransitionManager : MonoBehaviour
{
    [Header("ī�޶� ����")]
    public Camera mainCamera;

    [Header("������ ��ǥ ������Ʈ")]
    public Transform throneZoomTarget;
    public Transform firstPersonTarget;

    [Header("UI ����")]
    public CanvasGroup titleUIGroup;
    public Image blackFadeScreen;

    [Header("ī�޶� ���� �ð� ����")]
    public float uiFadeDuration = 0.5f;
    public float zoomDuration = 2.0f;
    public float holdDuration = 1.0f;
    public float firstPersonDuration = 1.5f;

    [Header("ȭ�� ���̵� ���� �ð� (��û�Ͻ� Ÿ�̹�)")]
    public float delayBeforeFadeOut = 0.5f;      // 1. 1��Ī ��ȯ �� ��� �ð� (0.5��)
    public float screenFadeOutDuration = 1.5f;   // 2. ȭ�� ���̵� �ƿ� �ð� (1.5��)
    public float blackScreenHoldDuration = 1.0f; // 3. ���� ȭ�� ���� �ð� (1.0��)

    private void Start()
    {
        // Ÿ��Ʋ �� ���� �� ���� ȭ���� �Ϻ��ϰ� ���� (���� �� ���̵��� ����)
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

        // ���� ����
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

        // ���� �� ���
        yield return new WaitForSeconds(holdDuration);

        elapsedTime = 0f;
        Vector3 zoomPos = mainCamera.transform.position;
        Quaternion zoomRot = mainCamera.transform.rotation;

        // 1��Ī ȸ�� ����
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

        // 1. 1��Ī �Ϸ� �� ��� ��� (0.5��)
        yield return new WaitForSeconds(delayBeforeFadeOut);

        // 2. ���� ȭ������ ���̵� �ƿ� (1.5��)
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

        // 3. ������ ����� ���·� ��� (1.0��)
        yield return new WaitForSeconds(blackScreenHoldDuration);

        // 4. ���� �� �ε�
        string nextScene = Application.CanStreamedLevelBeLoaded("simul") ? "simul" : "GameScene";
        SceneManager.LoadScene(nextScene);
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