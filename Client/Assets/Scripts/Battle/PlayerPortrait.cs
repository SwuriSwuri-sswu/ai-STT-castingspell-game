using System.Collections;
using UnityEngine;
using UnityEngine.UI;

// 체력바 옆 주인공 초상화 (3D 캐릭터를 별도 카메라로 찍어서 UI에 표시)
//
// 구성
//   [PortraitStudio]  ← 맵에서 멀리 떨어진 곳 (예: 위치 0, -100, 0)
//       ├ PortraitFox     (fox.glb + Animator: 아래 Controller)
//       └ PortraitCamera  (Culling Mask = Portrait 레이어만, Target Texture = RenderTexture)
//   Canvas
//       └ Portrait        ← RawImage (Texture = 같은 RenderTexture) + 이 스크립트
//
// 반응
//   - 평소: Idle
//   - 맞았을 때: Hit 트리거 + 초상화가 붉게 번쩍이며 흔들림
//   - 체력이 적을 때: 초상화가 은은하게 붉은빛
//   - 쓰러졌을 때: Down 트리거 + 회색으로
public class PlayerPortrait : MonoBehaviour
{
    [Header("연결")]
    [Tooltip("초상화용 캐릭터의 Animator")]
    public Animator portraitAnimator;
    [Tooltip("초상화를 보여 주는 RawImage (비우면 이 오브젝트에서 찾음)")]
    public RawImage portraitImage;

    [Header("Animator 트리거 이름 (없으면 비워 두기)")]
    public string hitTrigger = "Hit";
    public string downTrigger = "Down";

    [Header("피격 연출")]
    public Color hitTint = new Color(1f, 0.35f, 0.35f);
    public float hitTintTime = 0.35f;
    [Tooltip("흔들리는 크기 (픽셀)")]
    public float shake = 10f;
    public float shakeTime = 0.3f;
    [Tooltip("맞을 때 살짝 작아졌다 돌아오는 정도")]
    public float punch = 0.08f;

    [Header("체력 상태 표시")]
    [Range(0f, 1f)] public float lowHpThreshold = 0.3f;
    public Color lowHpTint = new Color(1f, 0.75f, 0.75f);
    public Color deadTint = new Color(0.45f, 0.45f, 0.45f);

    private RectTransform rect;
    private Vector2 basePos;
    private Vector3 baseScale;
    private Color baseColor = Color.white;
    private float hitTimer = 999f;
    private Coroutine shakeRoutine;

    private void Start()
    {
        if (portraitImage == null) portraitImage = GetComponent<RawImage>();
        rect = transform as RectTransform;
        if (rect != null)
        {
            basePos = rect.anchoredPosition;
            baseScale = rect.localScale;
        }

        PlayerHealth hp = PlayerHealth.Instance;
        if (hp == null)
        {
            Debug.LogWarning("[PlayerPortrait] 씬에 PlayerHealth가 없습니다.");
            return;
        }
        hp.OnDamaged += HandleDamaged;
        hp.OnDied += HandleDied;
    }

    private void OnDestroy()
    {
        PlayerHealth hp = PlayerHealth.Instance;
        if (hp != null)
        {
            hp.OnDamaged -= HandleDamaged;
            hp.OnDied -= HandleDied;
        }
    }

    private void Update()
    {
        if (portraitImage == null) return;
        PlayerHealth hp = PlayerHealth.Instance;

        // 기본 색: 쓰러짐 > 저체력 > 평소
        if (hp != null && hp.IsDead) baseColor = deadTint;
        else if (hp != null && hp.Ratio <= lowHpThreshold) baseColor = lowHpTint;
        else baseColor = Color.white;

        // 맞은 직후 붉게 번쩍 → 기본 색으로
        hitTimer += Time.unscaledDeltaTime;
        float k = Mathf.Clamp01(hitTimer / Mathf.Max(0.01f, hitTintTime));
        portraitImage.color = Color.Lerp(hitTint, baseColor, k);
    }

    private void HandleDamaged(float amount, string reason)
    {
        hitTimer = 0f;
        Trigger(hitTrigger);

        if (rect != null)
        {
            if (shakeRoutine != null) StopCoroutine(shakeRoutine);
            shakeRoutine = StartCoroutine(Shake());
        }
    }

    private void HandleDied()
    {
        Trigger(downTrigger);
    }

    private void Trigger(string name)
    {
        if (portraitAnimator == null || string.IsNullOrEmpty(name)) return;
        foreach (AnimatorControllerParameter p in portraitAnimator.parameters)
        {
            if (p.name == name && p.type == AnimatorControllerParameterType.Trigger)
            {
                portraitAnimator.SetTrigger(name);
                return;
            }
        }
    }

    private IEnumerator Shake()
    {
        float t = 0f;
        while (t < shakeTime)
        {
            t += Time.unscaledDeltaTime;
            float k = 1f - t / shakeTime;
            rect.anchoredPosition = basePos + Random.insideUnitCircle * shake * k;
            float s = 1f - punch * Mathf.Sin(Mathf.Clamp01(t / shakeTime) * Mathf.PI);
            rect.localScale = baseScale * s;
            yield return null;
        }
        rect.anchoredPosition = basePos;
        rect.localScale = baseScale;
        shakeRoutine = null;
    }
}