using UnityEngine;

// 횃불·모닥불 불빛이 일렁이게 (Point Light에 붙이기)
// 밝기와 위치를 살짝씩 흔들어서 불꽃이 살아 있는 느낌을 줌
[RequireComponent(typeof(Light))]
public class TorchFlicker : MonoBehaviour
{
    [Tooltip("밝기가 흔들리는 정도 (0.2 = ±20%)")]
    [Range(0f, 1f)] public float intensityAmount = 0.2f;
    [Tooltip("불빛 위치가 흔들리는 거리 (그림자·명암이 같이 일렁임)")]
    public float moveAmount = 0.03f;
    [Tooltip("일렁이는 속도")]
    public float speed = 6f;

    private Light lightSource;
    private float baseIntensity;
    private Vector3 baseLocalPos;
    private float seed;

    private void Awake()
    {
        lightSource = GetComponent<Light>();
        baseIntensity = lightSource.intensity;
        baseLocalPos = transform.localPosition;
        seed = Random.value * 100f; // 횃불마다 따로 일렁이게
    }

    private void Update()
    {
        float t = Time.time * speed + seed;
        float n = Mathf.PerlinNoise(t, seed) * 2f - 1f;                 // -1 ~ 1
        float flick = Mathf.PerlinNoise(t * 3f, seed + 7f) * 2f - 1f;   // 빠른 잔떨림
        lightSource.intensity = baseIntensity * (1f + (n * 0.7f + flick * 0.3f) * intensityAmount);

        transform.localPosition = baseLocalPos + new Vector3(
            Mathf.PerlinNoise(t, seed + 3f) - 0.5f,
            Mathf.PerlinNoise(t, seed + 5f) - 0.5f,
            Mathf.PerlinNoise(t, seed + 9f) - 0.5f) * 2f * moveAmount;
    }
}