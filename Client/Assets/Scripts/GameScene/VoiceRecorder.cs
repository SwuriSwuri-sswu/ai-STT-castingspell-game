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
///   - 발동: 들린 글자 타이핑 → 스펠 이름 강조 → 시전 → 몹에 맞고 결과 문구가 뜬 뒤 다음 영창 가능
///   - 불발, 오류, 시간 초과, 너무 짧음, 적 없음, 시전 실패: 실패 이유를 회색 자막으로 잠시 보여 준 뒤 안내 문구로 복귀
/// </summary>
public class VoiceRecorder : MonoBehaviour
{
    [Header("자막")]
    public TextMeshProUGUI subtitleText;
    public float typingSpeed = 0.05f;             // 글자당 출력 시간 (초)
    [SerializeField] float failHoldSec = 1.5f;    // 실패 이유를 보여 주는 시간 (초)
    [Tooltip("마법이 몹에 맞은 뒤(효과가 있었다 등) 다음 영창을 받기까지 기다리는 시간 (초)")]
    [SerializeField] float afterHitHoldSec = 1f;
    [Tooltip("마법이 맞기를 기다리는 최대 시간 (초). 혹시 안 맞아도 이 시간이 지나면 다음 영창 가능")]
    [SerializeField] float maxHitWaitSec = 6f;

    [Header("속성별 주문 자막 색")]
    [SerializeField] Color normalColor = new Color(1f, 0.84f, 0f);      // 노말: 금색 (#FFD700)
    [SerializeField] Color waterColor = new Color(0.31f, 0.76f, 1f);   // 물: 하늘색 (#4FC3FF)
    [SerializeField] Color fireColor = new Color(1f, 0.35f, 0.21f);   // 불: 주황빛 빨강 (#FF5A36)
    [SerializeField] Color grassColor = new Color(0.42f, 0.86f, 0.36f);// 풀: 연두 (#6BDB5C)

    [Header("음성 연결")]
    [SerializeField] VoiceInput voiceInput;
    [SerializeField] VoiceSender voiceSender;
    [SerializeField] VoiceSocket voiceSocket;

    /// <summary>직전 주문의 성량 (dBFS, 0이 최대). 서버가 계산한 값. 데미지 공식에 사용 가능</summary>
    public float LastVolumeDb { get; private set; } = -80f;

    const string HintReady = "주문을 영창하세요.";
    const string HintWaiting = "적이 다가오고 있습니다...";

    Coroutine sequence;
    bool castPending;   // 판정 성공 ~ 마법이 몹에 맞고 결과 문구가 뜬 뒤까지 (이 사이에는 새 주문을 받지 않는다)
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
                // 적이 없거나(도착 전·쓰러질 예정), 직전 주문의 시전 연출 중이면 발화를 보내지 않는다
                voiceSender.CanAccept = () => CanCast && !castPending;
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

        // 몹을 쓰러뜨리는 중(마법이 날아가는 중 ~ 다음 몹 등장 전)이거나 시전 연출 중에는
        // 영창이 '일시 정지' 상태이므로 실패 자막을 띄우지 않는다
        if (IsFinishingEnemy || castPending) return;

        ShowFailure(reason);
    }

    // 지금 몹이 쓰러질 예정이거나 쓰러지는 중인지
    static bool IsFinishingEnemy
    {
        get
        {
            EnemyController current = EnemyManager.Instance != null ? EnemyManager.Instance.CurrentEnemy : null;
            return current != null && (current.IsDying || current.IsDoomed);
        }
    }

    void OnError(ErrorResponse e)
    {
        // request_id 없는 오류(스펠 목록 오류 등)는 주문과 무관하므로 무시한다 (Console 에는 출력됨)
        if (e == null || string.IsNullOrEmpty(e.RequestId)) return;
        Debug.Log($"[자막] 불발: 서버 오류 {e.Code} ({e.Message})");
        ShowFailure(ErrorToMessage(e.Code));
    }

    void OnResult(CastResult r)
    {
        if (r == null) return;
        // 결과가 늦게 와서 이미 다음 주문을 말하는 중이면 덮어쓰지 않는다
        if (voiceSender != null && voiceSender.IsRecording) return;

        if (r.Audio != null) LastVolumeDb = r.Audio.VolumeDb;

        // 판정이 오는 사이에 몹이 쓰러질 예정이 됐으면 이 주문은 조용히 버린다 (일시 정지)
        if (!CanCast)
        {
            Debug.Log($"[자막] 일시 정지 중이라 판정 결과를 무시합니다 ({r.Skill?.Name ?? r.RejectReason})");
            if (sequence == null) ShowHint();
            return;
        }
        // 직전 주문이 아직 시전되기 전(타이핑·강조 연출 중)이면 끊지 않는다
        if (castPending)
        {
            Debug.Log("[자막] 이전 주문 연출 중이라 판정 결과를 무시합니다");
            return;
        }
        StopSequence();

        if (!r.Success)
        {
            Debug.Log($"[자막] 불발: {r.RejectReason} (들린 글자 \"{r.Stt?.Text}\")");
            ShowFailure(RejectToMessage(r.RejectReason), r.Stt?.Text);
            return;
        }
        sequence = StartCoroutine(SuccessSequence(r.Stt?.Text, r.Skill?.Name));
    }

    // ── 발동 연출: 들린 글자 타이핑 → 스펠 이름 강조 → 시전 → 잠시 뒤 지움 ──
    IEnumerator SuccessSequence(string rawText, string spellName)
    {
        castPending = true;
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
            string hex = ColorUtility.ToHtmlStringRGB(SpellColor(spellName));
            subtitleText.text = $"<color=#{hex}><b>{spellName}!</b></color>";
            yield return Pop();
        }

        // 연출 중에 몹이 쓰러질 예정이 됐으면 조용히 종료 (일시 정지)
        if (!CanCast)
        {
            Debug.Log($"[자막] 일시 정지 중이라 시전하지 않습니다 ({spellName})");
            castPending = false;
            ShowHint();
            sequence = null;
            yield break;
        }

        // 연출이 끝난 직후 시전 (연출 중 적이 사라졌거나 이펙트가 없으면 여기서 실패)
        SkillManager sm = SkillManager.Instance;
        EnemyController target = EnemyManager.Instance != null ? EnemyManager.Instance.CurrentEnemy : null;
        string fail = sm != null ? sm.TryCastByVoice(spellName) : "SkillManager 없음";
        if (fail != null)
        {
            castPending = false;
            Debug.Log($"[자막] 시전 실패: {fail} ({spellName})");
            Show(Gray(CastFailToMessage(fail, spellName)));
            yield return new WaitForSeconds(failHoldSec);
            ShowHint();
            sequence = null;
            yield break;
        }

        // 마법이 날아가서 몹에 맞을 때까지 다음 영창을 받지 않는다 (스펠 이름 자막은 그대로 유지)
        float waited = 0f;
        while (waited < maxHitWaitSec)
        {
            bool landed = target == null || target.IsDying || target.PendingDamage <= 0f;
            if (landed) break;
            waited += Time.deltaTime;
            yield return null;
        }

        // 맞은 뒤 '효과가 있었다' 문구를 읽을 시간
        yield return new WaitForSeconds(afterHitHoldSec);

        castPending = false;   // 이제 다음 주문을 받을 수 있음 (막타였다면 CanCast가 false가 되어 계속 막힘)
        ShowHint();
        sequence = null;
    }

    // ── 실패 피드백: 회색 자막으로 이유를 잠시 보여 주고 안내 문구로 돌아간다 ──
    void ShowFailure(string message, string heardText = null)
    {
        StopSequence();
        if (string.IsNullOrEmpty(message)) { ShowHint(); return; }
        sequence = StartCoroutine(FailureSequence(message, heardText));
    }

    IEnumerator FailureSequence(string message, string heardText)
    {
        string text = string.IsNullOrWhiteSpace(heardText)
            ? message
            : $"\"{heardText}\"\n{message}";
        Show(Gray(text));
        yield return new WaitForSeconds(failHoldSec);
        ShowHint();
        sequence = null;
    }

    void ShowHint() => Show(CanCast ? HintReady : HintWaiting);

    // 스펠 속성에 맞는 자막 색 (스펠을 못 찾으면 노말 색)
    Color SpellColor(string spellName)
    {
        SpellData spell = SkillManager.Instance != null ? SkillManager.Instance.FindSpell(spellName) : null;
        if (spell == null) return normalColor;
        switch (spell.elementType)
        {
            case MagicElement.Water: return waterColor;
            case MagicElement.Fire: return fireColor;
            case MagicElement.Grass: return grassColor;
            default: return normalColor;
        }
    }

    static string Gray(string s) => $"<color=#AAAAAA>{s}</color>";

    // 서버 불발 사유 → 플레이어용 문구
    static string RejectToMessage(string reason)
    {
        switch (reason)
        {
            case "EMPTY":
            case "EMPTY_TEXT": return "주문이 들리지 않았습니다.";
            case "NOISE": return "잡음만 들렸습니다. 다시 외쳐 주세요.";
            case "NO_MATCH": return "알 수 없는 주문입니다...";
            case "AMBIGUOUS": return "주문이 불분명합니다. 또박또박 외쳐 주세요.";
            default: return "주문이 실패했습니다.";
        }
    }

    // 서버 오류 코드 → 플레이어용 문구
    static string ErrorToMessage(string code)
    {
        switch (code)
        {
            case "TOO_SHORT": return "주문이 너무 짧습니다.";
            case "TOO_LONG": return "주문이 너무 깁니다.";
            case "NOT_READY": return "마법서를 펼치는 중입니다... 잠시 후 다시 외쳐 주세요.";
            default: return "주문 해석에 실패했습니다.";
        }
    }

    // SkillManager.TryCastByVoice 실패 사유 → 플레이어용 문구
    static string CastFailToMessage(string fail, string spellName)
    {
        switch (fail)
        {
            case "알 수 없는 주문": return "알 수 없는 주문입니다...";
            case "아직 배우지 않은 주문": return $"아직 배우지 않은 주문입니다: {spellName}";
            case "공격할 적이 없음": return "공격할 적이 없습니다.";
            default: return "마법이 발동하지 않았습니다.";
        }
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
        castPending = false;
        if (subtitleText != null) subtitleText.transform.localScale = baseScale;
    }

    void Show(string text)
    {
        if (subtitleText != null) subtitleText.text = text ?? "";
    }
}