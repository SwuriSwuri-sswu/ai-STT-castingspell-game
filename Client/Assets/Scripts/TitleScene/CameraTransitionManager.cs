using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

public class CameraTransitionManager : MonoBehaviour
{
    [Header("카메라 설정")]
    public Camera mainCamera;

    [Header("도착지 좌표 오브젝트")]
    public Transform throneZoomTarget;
    public Transform firstPersonTarget;

    [Header("오브젝트 제어 (새로 추가됨)")]
    public GameObject playerCharacter; // 숨길 주인공 캐릭터 전체 (최상위 부모)
    public Renderer wandRenderer;      // 페이드인 할 마법 지팡이의 렌더러

    [Header("연출 시간 설정")]
    public float zoomDuration = 2.0f;
    public float holdDuration = 1.0f;
    public float firstPersonDuration = 1.5f;

    public void StartGameTransition()
    {
        StartCoroutine(TransitionSequence());
    }

    private IEnumerator TransitionSequence()
    {
        // [초기 설정] 지팡이를 투명하게 만들고 시작
        if (wandRenderer != null)
        {
            Color initColor = wandRenderer.material.GetColor("_BaseColor");
            initColor.a = 0f;
            wandRenderer.material.SetColor("_BaseColor", initColor);
            wandRenderer.gameObject.SetActive(false);
        }

        Vector3 startPos = mainCamera.transform.position;
        Quaternion startRot = mainCamera.transform.rotation;
        float elapsedTime = 0f;

        // 1단계: 타이틀 뷰 -> 왕좌 줌인 연출
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

        // 2단계: 왕좌 뷰에서 잠시 대기
        yield return new WaitForSeconds(holdDuration);

        // 3단계: 1인칭 전환 시작 (캐릭터 끄기, 지팡이 켜기)
        if (playerCharacter != null) playerCharacter.SetActive(false);
        if (wandRenderer != null) wandRenderer.gameObject.SetActive(true);

        elapsedTime = 0f;
        Vector3 zoomPos = mainCamera.transform.position;
        Quaternion zoomRot = mainCamera.transform.rotation;

        // 4단계: 1인칭 시점 진입 및 지팡이 페이드인
        while (elapsedTime < firstPersonDuration)
        {
            float t = Mathf.SmoothStep(0f, 1f, elapsedTime / firstPersonDuration);

            // 카메라 이동
            mainCamera.transform.position = Vector3.Lerp(zoomPos, firstPersonTarget.position, t);
            mainCamera.transform.rotation = Quaternion.Slerp(zoomRot, firstPersonTarget.rotation, t);

            // URP 머티리얼 알파값 서서히 증가 (페이드인)
            if (wandRenderer != null)
            {
                Color currentWandColor = wandRenderer.material.GetColor("_BaseColor");
                currentWandColor.a = Mathf.Lerp(0f, 1f, t);
                wandRenderer.material.SetColor("_BaseColor", currentWandColor);
            }

            elapsedTime += Time.deltaTime;
            yield return null;
        }

        mainCamera.transform.position = firstPersonTarget.position;
        mainCamera.transform.rotation = firstPersonTarget.rotation;

        // 5단계: 연출 완료 후 GameScene 로드
        SceneManager.LoadScene("GameScene");
    }
}