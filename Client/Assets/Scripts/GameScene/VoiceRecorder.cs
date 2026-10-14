using System.Collections;
using TMPro;
using UnityEngine;

/// <summary>
/// 음성 주문 결과를 자막으로 보여 주고 스펠 시전을 요청한다.
/// 녹음과 전송은 VoiceSender / VoiceSocket 가 하고, 이 클래스는 그 이벤트만 받는다.
/// 시전은 SkillManager.TryCastByVoice 에 요청한다.
///
/// 표시 규칙
///   - 대기: 적이 있으면 "주문을 영창하세요.", 없으면 "적이 다가오고 있습니다..."
///   - 말하는 중 / 해석 중: 상태 문구
///   - 발동: 들린 글자 타이핑 → 스펠 이름 노란 강조 → 시전 → 1초 뒤 자막 지움
///   - 불발, 오류, 시간 초과, 너무 짧음, 적 없음, 시전 실패: 자막을 지우고 로그만 남김
/// </summary>
public class VoiceRecorder : MonoBehaviour
{
    [Header("자막")]
    public TextMeshProUGUI subtitleText;
    public float typingSpeed = 0.05f;             // 글자당 출력 시간 (초)
    [SerializeField] float successHoldSec = 1f;   // 발동 강조를 보여 주는 시간 (초)

    [Header("음성 연결")]
    [SerializeField] VoiceInput voiceInput;
    [SerializeField] VoiceSender voiceSender;
    [SerializeField] VoiceSocket voiceSocket;

    /// <summary>직전 주문의 성량 (dBFS, 0이 최대). 서버가 계산한 값. 데미지 공식에 사용 가능</summary>
    public float LastVolumeDb { get; private set; } = -80f;

    const string HintReady = "주문을 영창하세요.";
    const string HintWaiting = "적이 다가오고 있습니다...";

    Coroutine sequence;
    Vector3 baseScale = Vector3.one;   // 자막 원래 크기 (연출이 끊겨도 크기가 누적되지 않게)
    bool wasCastable;

    // 스폰포인트에 도착한 적이 있어야 시전 가능
    static bool CanCast => SkillManager.CanCast;

    bool Busy => sequence != null ||
                 (voiceSender != null && (voiceSender.IsRecording || voiceSender.IsWaitingForResult));

    void Awake()
    {
        if (subtitleText != null) baseScale = subtitleText.transform.localScale;
    }

    void OnEnable() => Subscribe(true);

    void OnDisable()
    {
        Subscribe(false);
        StopSequence();
        Show(null);
    }

    void Start()
    {
        // 게임 설정에서 고른 마이크 사용 (설정 매니저가 없으면 VoiceInput 의 Inspector 값 사용)
        if (voiceInput != null && GameSettingManager.Instance != null &&
            !string.IsNullOrEmpty(GameSettingManager.Instance.selectedMicName))
            voiceInput.DeviceName = GameSettingManager.Instance.selectedMicName;

        if (SkillManager.Instance == null)
            Debug.LogError("[자막] 씬에 SkillManager 가 없습니다. 주문을 시전할 수 없습니다.");

        wasCastable = CanCast;
        Show(wasCastable ? HintReady : HintWaiting);
    }

    void Update()
    {
        // 적이 도착하거나 사라지면 안내 문구 변경 (녹음, 해석, 연출 중이 아닐 때만)
        bool canCast = CanCast;
        if (canCast != wasCastable && !Busy) Show(canCast ? HintReady : HintWaiting);
        wasCastable = canCast;
    }

    void Subscribe(bool on)
    {
        if (voiceSender != null)
        {
            if (on)
            {
                voiceSender.CanAccept = () => CanCast;   // 적이 없으면 발화를 보내지 않는다
                voiceSender.RecordingStarted += OnStarted;
                voiceSender.RecordingSubmitted += OnSubmitted;
                voiceSender.RecordingRejected += OnRejected;
            }
            else
            {
                voiceSender.CanAccept = null;
                voiceSender.RecordingStarted -= OnStarted;
                voiceSender.RecordingSubmitted -= OnSubmitted;
                voiceSender.RecordingRejected -= OnRejected;
            }
        }
        if (voiceSocket != null)
        {
            if (on) { voiceSocket.CastResultReceived += OnResult; voiceSocket.ErrorReceived += OnError; }
            else { voiceSocket.CastResultReceived -= OnResult; voiceSocket.ErrorReceived -= OnError; }
        }
    }

    // ── 상태 표시 ──
    void OnStarted()
    {
        StopSequence();
        Show("[ 마법 영창 중... ]");
    }

    void OnSubmitted() => Show("[ 마나 흐름 해석 중... ]");

    // 너무 짧음, 시간 초과, 연결 없음, 적 없음 → 자막 지움 + 로그
    void OnRejected(string reason)
    {
        Debug.Log($"[자막] 불발: {reason}");
        StopSequence();
        Show(null);
    }

    void OnError(ErrorResponse e)
    {
        // request_id 없는 오류(스펠 목록 오류 등)는 주문과 무관하므로 무시한다 (Console 에는 출력됨)
        if (e == null || string.IsNullOrEmpty(e.RequestId)) return;
        Debug.Log($"[자막] 불발: 서버 오류 {e.Code}");
        StopSequence();
        Show(null);
    }

    void OnResult(CastResult r)
    {
        if (r == null) return;
        // 결과가 늦게 와서 이미 다음 주문을 말하는 중이면 덮어쓰지 않는다
        if (voiceSender != null && voiceSender.IsRecording) return;

        if (r.Audio != null) LastVolumeDb = r.Audio.VolumeDb;
        StopSequence();

        if (!r.Success)
        {
            Debug.Log($"[자막] 불발: {r.RejectReason} (들린 글자 \"{r.Stt?.Text}\")");
            Show(null);
            return;
        }
        sequence = StartCoroutine(SuccessSequence(r.Stt?.Text, r.Skill?.Name));
    }

    // ── 발동 연출: 들린 글자 타이핑 → 스펠 이름 강조 → 시전 → 잠시 뒤 지움 ──
    IEnumerator SuccessSequence(string rawText, string spellName)
    {
        if (subtitleText != null && !string.IsNullOrEmpty(rawText))
        {
            subtitleText.text = "\"";
            foreach (char c in rawText)
            {
                subtitleText.text += c;
                yield return new WaitForSeconds(typingSpeed);
            }
            subtitleText.text += "\"";
            yield return new WaitForSeconds(0.5f);
        }

        if (subtitleText != null)
        {
            subtitleText.text = $"<color=#FFD700><b>{spellName}!</b></color>";
            yield return Pop();
        }

        // 연출이 끝난 직후 시전 (연출 중 적이 사라졌거나 이펙트가 없으면 여기서 실패)
        SkillManager sm = SkillManager.Instance;
        string fail = sm != null ? sm.TryCastByVoice(spellName) : "SkillManager 없음";
        if (fail != null)
        {
            Debug.Log($"[자막] 시전 실패: {fail} ({spellName})");
            Show(null);
            sequence = null;
            yield break;
        }

        yield return new WaitForSeconds(successHoldSec);
        Show(null);
        sequence = null;
    }

    IEnumerator Pop()   // 0.15초 동안 1.5배로 커졌다가 0.15초 동안 원래 크기로
    {
        Transform t = subtitleText.transform;
        Vector3 big = baseScale * 1.5f;
        const float half = 0.15f;

        for (float e = 0f; e < half; e += Time.deltaTime)
        {
            t.localScale = Vector3.Lerp(baseScale, big, e / half);
            yield return null;
        }
        for (float e = 0f; e < half; e += Time.deltaTime)
        {
            t.localScale = Vector3.Lerp(big, baseScale, e / half);
            yield return null;
        }
        t.localScale = baseScale;
    }

    void StopSequence()
    {
        if (sequence != null) StopCoroutine(sequence);
        sequence = null;
        if (subtitleText != null) subtitleText.transform.localScale = baseScale;
    }

    void Show(string text)
    {
        if (subtitleText != null) subtitleText.text = text ?? "";
    }
}