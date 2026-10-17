using UnityEngine;

// 지팡이 시전 연출 (애니메이션 없이 코드로 움직임)
// 붙이는 곳: 지팡이를 잡은 '손 위치'에 만든 빈 오브젝트 (예: WandRig)
//   [CHARACTER]
//     └ WandRig   ← 이 스크립트. 위치 = 지팡이 손잡이(손목) 부분. 이 점을 중심으로 휘둘러짐
//         ├ fox_arm   (팔 + 지팡이 모델)
//         └ WandTip   (마법이 나가는 지점. 자식으로 넣어야 지팡이 끝을 따라 움직임)
//
// 움직임 (모두 카메라 기준 방향이라 오브젝트 회전값과 상관없이 동작)
//   - 평소: 숨쉬듯 아주 살짝 흔들림
//   - 영창 중(말하는 동안 ~ 판정 대기): 지팡이를 살짝 들어 올리고 미세하게 떨림 (마력을 모으는 느낌)
//   - 시전: 살짝 뒤로 젖혔다가 → 앞으로 휘두름 → 반동으로 천천히 원위치
//           강한 영창(power > 1)일수록 더 크게 휘두름
public class WandCaster : MonoBehaviour
{
    [Header("연결 (비우면 자동으로 찾음)")]
    public VoiceSender voiceSender;
    [Tooltip("방향 기준 카메라 (비우면 Main Camera)")]
    public Camera viewCamera;
    [Tooltip("(선택) 영창 중 지팡이 끝에 켤 이펙트 위치. 보통 WandTip")]
    public Transform wandTip;

    [Header("평소 흔들림")]
    public bool idleSway = true;
    [Tooltip("흔들리는 각도 (도)")]
    public float idleAngle = 0.8f;
    [Tooltip("위아래로 움직이는 거리")]
    public float idleBob = 0.008f;
    public float idleSpeed = 1.2f;

    [Header("영창 중 (말하는 동안)")]
    [Tooltip("지팡이를 들어 올리는 높이")]
    public float channelRaise = 0.04f;
    [Tooltip("지팡이 끝을 위로 세우는 각도 (도)")]
    public float channelTilt = 8f;
    [Tooltip("미세하게 떨리는 정도 (도)")]
    public float channelTremble = 0.8f;
    public float channelTrembleSpeed = 14f;
    [Tooltip("자세가 바뀌는 속도 (클수록 빨리 들어 올림)")]
    public float channelBlendSpeed = 8f;
    [Tooltip("(선택) 영창 중 지팡이 끝에 켤 이펙트 (작은 빛, 반짝이 등)")]
    public GameObject channelEffectPrefab;

    [Header("시전 (휘두르기)")]
    [Tooltip("뒤로 젖히는 각도 (도)")]
    public float windupAngle = 10f;
    public float windupTime = 0.06f;
    [Tooltip("앞으로 휘두르는 각도 (도)")]
    public float swingAngle = 22f;
    [Tooltip("앞으로 내지르는 거리")]
    public float swingPush = 0.06f;
    public float swingTime = 0.08f;
    [Tooltip("원래 자세로 돌아오는 시간")]
    public float recoverTime = 0.4f;
    [Tooltip("몹 쪽으로 살짝 겨누는 정도 (0 = 안 겨눔, 1 = 몹 방향으로 최대한)")]
    [Range(0f, 1f)] public float aimAtTarget = 0.3f;
    [Tooltip("강한 영창(power 1.5)일 때 휘두르는 크기 배율")]
    public float strongCastScale = 1.4f;

    // 쉬는 자세 (월드 기준)
    private Vector3 restPos;
    private Quaternion restRot;
    private float channelWeight;
    private float castTimer = -1f;
    private float castScale = 1f;
    private Vector3 castAimDir;
    private GameObject channelEffect;

    private float CastDuration => windupTime + swingTime + recoverTime;

    private void Start()
    {
        restPos = transform.position;
        restRot = transform.rotation;
        if (viewCamera == null) viewCamera = Camera.main;
        if (voiceSender == null) voiceSender = FindFirstObjectByType<VoiceSender>();
    }

    private void OnEnable()
    {
        // SkillManager가 먼저 생성되지 않았을 수도 있어서 Start 이후에도 한 번 더 시도
        TrySubscribe();
    }

    private void OnDisable()
    {
        if (subscribed && SkillManager.Instance != null) SkillManager.Instance.OnSpellCast -= HandleSpellCast;
        subscribed = false;
        SetChannelEffect(false);
    }

    private bool subscribed;
    private void TrySubscribe()
    {
        if (subscribed || SkillManager.Instance == null) return;
        SkillManager.Instance.OnSpellCast += HandleSpellCast;
        subscribed = true;
    }

    private void HandleSpellCast(SpellData spell, EnemyController target, float power)
    {
        castTimer = 0f;
        // power 1.0 → 1배, 1.5 → strongCastScale배
        castScale = Mathf.Lerp(1f, strongCastScale, Mathf.Clamp01((power - 1f) / 0.5f));
        castAimDir = target != null ? (target.AimPoint - restPos).normalized : Vector3.zero;
    }

    private void LateUpdate()
    {
        if (!subscribed) TrySubscribe();
        if (viewCamera == null) return;

        Transform cam = viewCamera.transform;
        Vector3 right = cam.right, up = cam.up, forward = cam.forward;
        float t = Time.time;

        // 1. 영창 중이면 들어 올린 자세로 (부드럽게)
        bool channeling = voiceSender != null && (voiceSender.IsRecording || voiceSender.IsWaitingForResult);
        if (castTimer >= 0f) channeling = false; // 휘두르는 동안은 시전 동작 우선
        channelWeight = Mathf.MoveTowards(channelWeight, channeling ? 1f : 0f, channelBlendSpeed * Time.deltaTime);
        SetChannelEffect(channeling);

        Vector3 posOffset = Vector3.zero;
        Quaternion rotOffset = Quaternion.identity;

        // 2. 평소 흔들림
        if (idleSway)
        {
            float k = 1f - channelWeight * 0.7f;
            posOffset += up * (Mathf.Sin(t * idleSpeed) * idleBob * k);
            rotOffset = Quaternion.AngleAxis(Mathf.Sin(t * idleSpeed * 0.8f) * idleAngle * k, forward) * rotOffset;
        }

        // 3. 영창 자세 + 떨림
        if (channelWeight > 0f)
        {
            float w = Smooth(channelWeight);
            posOffset += up * (channelRaise * w);
            float tremX = (Mathf.PerlinNoise(t * channelTrembleSpeed, 0.3f) - 0.5f) * 2f * channelTremble;
            float tremZ = (Mathf.PerlinNoise(0.7f, t * channelTrembleSpeed) - 0.5f) * 2f * channelTremble;
            rotOffset = Quaternion.AngleAxis(-channelTilt * w + tremX * w, right)
                      * Quaternion.AngleAxis(tremZ * w, forward) * rotOffset;
        }

        // 4. 시전: 젖히기 → 휘두르기 → 반동 복귀
        if (castTimer >= 0f)
        {
            castTimer += Time.deltaTime;
            float angle, push;
            if (castTimer < windupTime)
            {
                float k = Smooth(castTimer / windupTime);
                angle = -windupAngle * k;
                push = 0f;
            }
            else if (castTimer < windupTime + swingTime)
            {
                float k = Smooth((castTimer - windupTime) / swingTime);
                angle = Mathf.Lerp(-windupAngle, swingAngle, k);
                push = swingPush * k;
            }
            else
            {
                float k = Mathf.Clamp01((castTimer - windupTime - swingTime) / recoverTime);
                float ease = 1f - (1f - k) * (1f - k) * (1f - k); // 처음엔 빠르게, 끝에 천천히
                angle = Mathf.Lerp(swingAngle, 0f, ease);
                push = Mathf.Lerp(swingPush, 0f, ease);
            }

            angle *= castScale;
            push *= castScale;
            posOffset += forward * push;

            // 몹 쪽으로 살짝 겨누기 (좌우)
            Quaternion aim = Quaternion.identity;
            if (aimAtTarget > 0f && castAimDir != Vector3.zero)
            {
                float yaw = Vector3.SignedAngle(Vector3.ProjectOnPlane(forward, Vector3.up),
                                                Vector3.ProjectOnPlane(castAimDir, Vector3.up), Vector3.up);
                float strength = Mathf.Sin(Mathf.Clamp01(castTimer / CastDuration) * Mathf.PI);
                aim = Quaternion.AngleAxis(yaw * aimAtTarget * strength, Vector3.up);
            }

            // 카메라 오른쪽 축으로 돌리면 지팡이 끝이 앞(몹 쪽)으로 숙여짐
            rotOffset = aim * Quaternion.AngleAxis(angle, right) * rotOffset;

            if (castTimer >= CastDuration) castTimer = -1f;
        }

        transform.position = restPos + posOffset;
        transform.rotation = rotOffset * restRot;
    }

    private void SetChannelEffect(bool on)
    {
        if (channelEffectPrefab == null) return;
        if (on && channelEffect == null)
        {
            Transform parent = wandTip != null ? wandTip : transform;
            channelEffect = Instantiate(channelEffectPrefab, parent.position, parent.rotation, parent);
        }
        else if (!on && channelEffect != null)
        {
            foreach (ParticleSystem ps in channelEffect.GetComponentsInChildren<ParticleSystem>())
            {
                ps.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            }
            Destroy(channelEffect, 1f);
            channelEffect = null;
        }
    }

    private static float Smooth(float x)
    {
        x = Mathf.Clamp01(x);
        return x * x * (3f - 2f * x);
    }
}