using UnityEngine;

// 카메라 움찔 (주문을 시전하는 순간 등)
// 따로 붙일 필요 없음: CameraKick.Kick(...)을 부르면 Main Camera에 자동으로 붙음
// 다른 스크립트(보스전 입장 연출 등)가 카메라를 움직이고 있어도 그 위치를 기준으로 살짝 흔들고 원래대로 둠
public class CameraKick : MonoBehaviour
{
    private float timer, duration;
    private float strength;
    private Vector3 dir;          // 이번 움찔의 방향 (랜덤)
    private bool shakeMode;       // true = 맞았을 때처럼 덜덜 흔들림 / false = 시전할 때처럼 한 번 움찔
    private bool applied;
    private Vector3 basePos, appliedPos;
    private Quaternion baseRot, appliedRot;

    /// <param name="strength">세기 (0.5 = 약하게, 1 = 보통, 2 = 강하게)</param>
    /// <param name="time">지속 시간 (초)</param>
    public static void Kick(float strength = 1f, float time = 0.18f) => Get()?.Begin(strength, time, false);

    /// <summary>맞았을 때처럼 짧게 덜덜 흔들림</summary>
    public static void Shake(float strength = 1f, float time = 0.3f) => Get()?.Begin(strength, time, true);

    private static CameraKick Get()
    {
        Camera cam = Camera.main;
        if (cam == null) return null;
        CameraKick kick = cam.GetComponent<CameraKick>();
        if (kick == null) kick = cam.gameObject.AddComponent<CameraKick>();
        return kick;
    }

    private void Begin(float s, float time, bool shake)
    {
        if (s <= 0f) return;
        if (shake || timer <= 0f) shakeMode = shake;   // 흔들림이 움찔보다 우선
        // 이미 흔들리는 중이면 더 센 쪽으로
        strength = timer > 0f ? Mathf.Max(strength * (timer / Mathf.Max(0.01f, duration)), s) : s;
        duration = Mathf.Max(0.05f, time);
        timer = duration;
        dir = new Vector3(Random.Range(-1f, 1f), Random.Range(0.4f, 1f), Random.Range(-1f, 1f));
    }

    private void LateUpdate()
    {
        Transform t = transform;

        // 지난 프레임에 준 흔들림을 걷어냄 (그사이 다른 스크립트가 카메라를 옮겼으면 그 값을 그대로 둠)
        if (applied)
        {
            if (t.position == appliedPos) t.position = basePos;
            if (t.rotation == appliedRot) t.rotation = baseRot;
            applied = false;
        }
        if (timer <= 0f) return;

        timer -= Time.unscaledDeltaTime;
        float k = Mathf.Clamp01(timer / duration);

        basePos = t.position;
        baseRot = t.rotation;
        if (shakeMode)
        {
            // 맞음: 무작위로 덜덜 (끝으로 갈수록 약해짐)
            float a = k * strength;
            Vector3 euler = new Vector3(Random.Range(-1f, 1f) * 1.2f, Random.Range(-1f, 1f) * 1.2f, Random.Range(-1f, 1f) * 1.8f) * a;
            t.rotation = baseRot * Quaternion.Euler(euler);
            t.position = basePos + (t.right * Random.Range(-1f, 1f) + t.up * Random.Range(-1f, 1f)) * (0.025f * a);
        }
        else
        {
            float e = k * k * Mathf.Sin((1f - k) * Mathf.PI * 6f);   // 빠르게 튀었다가 잦아듦
            // 위로 살짝 들리며 뒤로 밀리는 움찔 + 약간의 좌우 기울기 (각도 단위)
            Vector3 euler = new Vector3(-dir.y * 1.2f, dir.x * 0.6f, dir.z * 0.8f) * (e * strength);
            t.rotation = baseRot * Quaternion.Euler(euler);
            t.position = basePos - t.forward * (0.03f * e * strength);
        }

        appliedPos = t.position;
        appliedRot = t.rotation;
        applied = true;
    }

    private void OnDisable()
    {
        if (!applied) return;
        transform.position = basePos;
        transform.rotation = baseRot;
        applied = false;
    }
}
