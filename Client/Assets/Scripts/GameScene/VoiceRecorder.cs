using System.Collections;
using TMPro;
using UnityEngine;

/// <summary>
/// 음성 주문 결과를 자막으로 보여 주고 스펠 발동을 요청한다.
/// 녹음·통신은 MicRecorder / VoiceWsTest 가 하고, 이 클래스는 그 이벤트만 받는다.
/// 발동은 VoiceSpellBook(가상의 SkillManager)에 요청한다.
/// subtitleText / typingSpeed 이름은 기존과 같게 유지했다 (씬 연결값 유지).
/// </summary>
public class VoiceRecorder : MonoBehaviour
{
    public TextMeshProUGUI subtitleText;
    public float typingSpeed = 0.05f;   // 글자당 출력 시간 (초)

    [SerializeField] MicRecorder micRecorder;
    [SerializeField] VoiceWsTest wsClient;
    [SerializeField] VoiceSpellBook spellBook;

    Coroutine sequence;
    Vector3 baseScale = Vector3.one;   // 자막 원래 크기 (연출이 끊겨도 크기가 누적되지 않게 한 번만 저장)

    void Awake()
    {
        if (subtitleText != null) baseScale = subtitleText.transform.localScale;
    }

    void OnEnable() => Subscribe(true);
    void OnDisable() { Subscribe(false); Show(null); }
    void Start() => Show("주문을 영창하세요.");

    void Subscribe(bool on)
    {
        if (micRecorder != null)
        {
            if (on) { micRecorder.RecordingStarted += OnStarted; micRecorder.RecordingSubmitted += OnSubmitted; micRecorder.RecordingRejected += Show; }
            else { micRecorder.RecordingStarted -= OnStarted; micRecorder.RecordingSubmitted -= OnSubmitted; micRecorder.RecordingRejected -= Show; }
        }
        if (wsClient != null)
        {
            if (on) { wsClient.CastResultReceived += OnResult; wsClient.ErrorReceived += OnError; }
            else { wsClient.CastResultReceived -= OnResult; wsClient.ErrorReceived -= OnError; }
        }
    }

    // ── 상태 표시 ──
    void OnStarted() => Show("[ 마법 영창 중... ]");
    void OnSubmitted() => Show("[ 마나 흐름 해석 중... ]");

    void OnError(ErrorResponse e)
    {
        // request_id 없는 오류(스펠 목록 오류 등)는 플레이어에게 보여 주지 않는다 (Console 에는 출력됨)
        if (!string.IsNullOrEmpty(e?.RequestId)) Show("주문 해석에 실패했습니다. (서버 오류)");
    }

    void OnResult(CastResult r)
    {
        // 결과가 늦게 와서 이미 다음 주문을 말하는 중이면 덮어쓰지 않는다
        if (r == null || (micRecorder != null && micRecorder.IsRecording)) return;

        Show(null);
        sequence = StartCoroutine(ResultSequence(r.Stt?.Text, r.Success ? r.Skill?.Name : null));
    }

    // ── 연출: 들린 발음 타이핑 → (발동) 스펠 이름 팝업 + 발동 요청 / (불발) 실패 문구 ──
    IEnumerator ResultSequence(string rawText, string spellName)
    {
        if (subtitleText == null) yield break;

        if (!string.IsNullOrEmpty(rawText))
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

        if (spellName == null)
        {
            subtitleText.text = "<color=#888888>주문이 흩어졌습니다...</color>";
            yield break;
        }

        subtitleText.text = $"<color=#FFD700><b>{spellName}!</b></color>";
        yield return Pop();

        if (spellBook != null && !spellBook.TryCast(spellName))
            subtitleText.text = "<color=#888888>아직 다룰 수 없는 주문입니다.</color>";
    }

    IEnumerator Pop()   // 0.15초 동안 1.5배로 커졌다가 0.15초 동안 원래 크기로
    {
        Transform t = subtitleText.transform;
        for (float e = 0f; e < 0.3f; e += Time.deltaTime)
        {
            float k = e < 0.15f ? e / 0.15f : (0.3f - e) / 0.15f;
            t.localScale = baseScale * (1f + 0.5f * k);
            yield return null;
        }
        t.localScale = baseScale;
    }

    /// <summary>진행 중인 연출을 끊고, text 가 있으면 표시한다</summary>
    void Show(string text)
    {
        if (sequence != null) { StopCoroutine(sequence); sequence = null; }
        if (subtitleText == null) return;
        subtitleText.transform.localScale = baseScale;
        if (text != null) subtitleText.text = text;
    }
}