using UnityEngine;

// 항상 카메라를 바라보게 (가짜 해, 빛 번짐 같은 평면 오브젝트용)
// 카메라가 움직여도(입장 연출 등) 납작해 보이지 않음
public class FaceCamera : MonoBehaviour
{
    [Tooltip("비우면 Main Camera")]
    public Camera target;

    private void LateUpdate()
    {
        Camera cam = target != null ? target : Camera.main;
        if (cam == null) return;
        transform.rotation = Quaternion.LookRotation(transform.position - cam.transform.position, cam.transform.up);
    }
}