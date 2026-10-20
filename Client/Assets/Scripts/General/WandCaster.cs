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

    [Header("걷기 (보스전 입장 연출에서 BossBattleDirector가 켬)")]
    [Tooltip("팔을 앞뒤로 흔드는 거리 (두 걸음에 한 번 앞→뒤)")]
    public float walkSwing = 0.07f;
    [Tooltip("팔을 흔들 때 지팡이가 기우는 각도 (도). 팔이 앞으로 가면 지팡이 끝은 뒤로 살짝 젖혀짐 (음수면 반대)")]
    public float walkSwingAngle = 10f;
    [Tooltip("팔이 앞·뒤 끝에 갈 때 살짝 올라가는 정도 (진자처럼 호를 그림)")]
    public float walkSwingLift = 0.015f;
    [Tooltip("발걸음마다 위아래로 출렁이는 정도 (카메라도 출렁이니 작게)")]
    public float walkBob = 0.005f;
    [Tooltip("두 걸음에 한 번 좌우로 흔들리는 정도")]
    public float walkSway = 0.008f;
    [Tooltip("좌우로 기우는 각도 (도)")]
    public float walkRoll = 1.5f;
    [Tooltip("발걸음마다 앞뒤로 끄덕이는 각도 (도). 팔 흔들기와 겹치므로 보통 0")]
    public float walkPitch = 0f;
    [Tooltip("걷는 동안 살짝 내려 드는 정도")]
    public float walkLower = 0.03f;

    [Header("내리기 (줌인할 때) / 전투 준비 (마왕이 일어날 때)")]
    [Tooltip("화면 아래로 내리는 거리")]
    public float lowerDistance = 0.5f;
    [Tooltip("내리고 올리는 속도 (1초에 0→1)")]
    public float lowerSpeed = 2.5f;
    [Tooltip("지팡이를 치켜드는 전체 시간 (초)")]
    public float readyTime = 1.1f;
    [Tooltip("치켜들 때 올라가는 정도")]
    public float readyRaise = 0.06f;
    [Tooltip("치켜들 때 뒤로 젖히는 각도 (도)")]
    public float readyTilt = 18f;

    private float walkWeight, walkPhase;
    private float lowerWeight, lowerTarget;
    private float readyTimer = -1f;

    // 걷는 흔들림 (weight 0 = 멈춤, phase = 발걸음 위상. 카메라 흔들림과 같은 값을 넘기면 박자가 맞음)
    public void SetWalk(float weight, float phase)
    {
        walkWeight = Mathf.Clamp01(weight);
        walkPhase = phase;
    }

    // 화면 아래로 내리기 / 다시 올리기
    public void SetLowered(bool lowered) => lowerTarget = lowered ? 1f : 0f;

    // 지팡이를 앞으로 치켜들었다가 자리 잡기 (전투 준비)
    public void PlayReady() => readyTimer = 0f;

    // 쉬는 자세 (부모 = 카메라 기준). 카메라가 걸어가거나 돌아도 지팡이가 따라옴
    private Vector3 restLocalPos;
    private Quaternion restLocalRot;
    private Vector3 RestPos => transform.parent != null ? transform.parent.TransformPoint(restLocalPos) : restLocalPos;
    private Quaternion RestRot => transform.parent != null ? transform.parent.rotation * restLocalRot : restLocalRot;
    private float channelWeight;
    private float castTimer = -1f;
    private float castScale = 1f;
    private Vector3 castAimDir;
    private GameObject channelEffect;

    private float CastDuration => windupTime + swingTime + recoverTime;

    private void Start()
    {
        restLocalPos = transform.localPosition;
        restLocalRot = transform.localRotation;
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
        castAimDir = target != null ? (target.AimPoint - RestPos).normalized : Vector3.zero;
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

        // 5. 걷기: 팔을 앞뒤로 흔듦 (두 걸음에 한 번) + 발걸음마다 살짝 출렁임
        if (walkWeight > 0f)
        {
            float step = Mathf.Abs(Mathf.Sin(walkPhase));   // 0 → 1 → 0 (한 걸음)
            float swing = Mathf.Sin(walkPhase);              // -1 ↔ 1 (두 걸음 = 팔 한 번 앞뒤)
            float side = Mathf.Sin(walkPhase * 0.5f);
            float w = walkWeight;

            // 팔 흔들기: 앞뒤로 움직이면서 끝에서 살짝 올라감 (진자), 지팡이는 반대로 살짝 기움
            posOffset += forward * (swing * walkSwing * w)
                       + up * (swing * swing * walkSwingLift * w);
            rotOffset = Quaternion.AngleAxis(-swing * walkSwingAngle * w, right) * rotOffset;

            // 발걸음 출렁임 (작게) + 좌우
            posOffset += up * ((step - 0.5f) * 2f * walkBob * w - walkLower * w) + right * (side * walkSway * w);
            rotOffset = Quaternion.AngleAxis(-side * walkRoll * w, forward)
                      * Quaternion.AngleAxis((0.5f - step) * 2f * walkPitch * w, right) * rotOffset;
        }

        // 6. 내리기 (줌인 동안 화면 밖으로)
        lowerWeight = Mathf.MoveTowards(lowerWeight, lowerTarget, lowerSpeed * Time.deltaTime);
        if (lowerWeight > 0f)
        {
            float k = Smooth(lowerWeight);
            posOffset -= up * (lowerDistance * k);
            rotOffset = Quaternion.AngleAxis(12f * k, right) * rotOffset;
        }

        // 7. 전투 준비: 앞으로 치켜들었다가 원래 자리로
        if (readyTimer >= 0f)
        {
            readyTimer += Time.deltaTime;
            float k = Mathf.Clamp01(readyTimer / Mathf.Max(0.05f, readyTime));
            float a = Mathf.Sin(k * Mathf.PI);
            posOffset += up * (readyRaise * a) + forward * (0.04f * a);
            rotOffset = Quaternion.AngleAxis(-readyTilt * a, right) * rotOffset;
            if (k >= 1f) readyTimer = -1f;
        }

        transform.position = RestPos + posOffset;
        transform.rotation = rotOffset * RestRot;
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