using System.Collections;
using UnityEngine;

public class CursorManager : MonoBehaviour
{
    [Header("커서 이미지 설정")]
    public Texture2D defaultCursor;      // 기본 마우스 모양 텍스처
    public Texture2D[] clickAnimFrames;  // 클릭 시 재생될 텍스처 배열 (순서대로)

    [Header("커서 설정")]
    // 클릭하는 정확한 지점 (보통 텍스처의 좌상단(0,0)이지만 과녁 모양이면 중앙으로 설정해야 함)
    public Vector2 hotSpot = Vector2.zero;
    public float frameDelay = 0.05f; // 애니메이션 프레임 간격 (빠르기 조절)

    private bool isClickAnimating = false;

    private void Start()
    {
        // 시작할 때 기본 커서로 설정
        SetDefaultCursor();
    }

    private void Update()
    {
        // 마우스 왼쪽 버튼을 클릭했을 때 애니메이션 시작
        if (Input.GetMouseButtonDown(0) && !isClickAnimating)
        {
            StartCoroutine(PlayClickAnimation());
        }
    }

    private void SetDefaultCursor()
    {
        // CursorMode.Auto : 하드웨어 커서(빠름), ForceSoftware : 소프트웨어 커서
        Cursor.SetCursor(defaultCursor, hotSpot, CursorMode.Auto);
    }

    private IEnumerator PlayClickAnimation()
    {
        isClickAnimating = true;

        // 배열에 등록된 텍스처를 순서대로 빠르게 변경하여 애니메이션 효과 생성
        for (int i = 0; i < clickAnimFrames.Length; i++)
        {
            Cursor.SetCursor(clickAnimFrames[i], hotSpot, CursorMode.Auto);
            yield return new WaitForSeconds(frameDelay);
        }

        // 애니메이션이 끝나면 다시 기본 커서로 복구
        SetDefaultCursor();
        isClickAnimating = false;
    }
}