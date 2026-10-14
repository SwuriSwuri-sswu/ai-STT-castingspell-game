using UnityEngine;
using TMPro;
using System.Collections;
using System.IO;
using UnityEngine.Networking;

// 스페이스바를 누르는 동안 녹음 → 서버(STT)로 전송 → 자막 연출 → 마법 시전
// - 몹이 스폰포인트에 도착해야만 녹음 가능
// - 한 번에 하나의 주문만 처리 (처리 중에는 새 녹음 불가)
// - 서버 없이 테스트할 수 있는 Mock 모드 지원
public class VoiceRecorder : MonoBehaviour
{
    [Header("UI 설정")]
    public TextMeshProUGUI subtitleText;

    [Header("네트워크 설정")]
    [Tooltip("체크하면 서버 대신 아래 Mock 스펠로 테스트 (백엔드 연결 전용)")]
    public bool useMockServer = true;
    // 백엔드 API 주소 (FastAPI 서버의 엔드포인트 입력)
    public string backendUrl = "http://localhost:8000/transcribe";
    [Tooltip("서버 응답 대기 최대 시간(초)")]
    public int requestTimeout = 10;

    [Header("Mock 테스트 (Use Mock Server 체크 시)")]
    [Tooltip("시전할 때마다 순서대로 사용할 스펠 이름. SpellData의 Spell Name과 같아야 합니다.")]
    public string[] mockSpellSequence = { "빛", "바람", "돌" };
    [Tooltip("서버 응답 시간 흉내 (초)")]
    public float mockDelay = 1.0f;

    [Header("녹음 설정")]
    [Tooltip("너무 짧은 녹음은 무시 (초)")]
    public float minRecordingTime = 0.2f;
    [Tooltip("체크하면 일정 성량 이상으로 말해야 주문이 전송됩니다")]
    public bool useVolumeGate = false;
    [Tooltip("최소 성량 (dBFS, 0이 최대. -30 ~ -20 정도에서 조절)")]
    public float minVolumeDb = -30f;

    [Header("자막 연출 설정")]
    public float typingSpeed = 0.05f; // 글자당 출력 대기 시간 (초)

    [Header("매니저 연결")]
    public SkillManager skillManager;

    // 직전 녹음의 성량 (추후 데미지 공식에 사용 가능)
    public float LastVolumeDb { get; private set; } = -80f;

    private const int SampleRate = 16000;
    private const int MaxRecordingTime = 10;

    private const string HintReady = "스페이스바를 누른 채로 주문을 영창하세요.";
    private const string HintWaiting = "적이 다가오고 있습니다...";

    private string selectedDevice;
    private AudioClip recordingClip;
    private bool isRecording;      // 스페이스바를 누르고 있는 동안
    private bool isProcessing;     // 서버 응답 대기 ~ 자막 연출 ~ 시전까지
    private float recordStartTime;
    private bool wasCastable;
    private int mockIndex;
    private Coroutine typingCoroutine;

    private SkillManager Skill => skillManager != null ? skillManager : SkillManager.Instance;

    void Start()
    {
        SelectMicrophone();

        if (string.IsNullOrEmpty(selectedDevice))
        {
            Debug.LogError("마이크 장치가 없습니다.");
            SetSubtitle("마이크를 확인할 수 없습니다.");
            return;
        }

        wasCastable = SkillManager.CanCast;
        SetSubtitle(wasCastable ? HintReady : HintWaiting);
    }

    // 환경설정에서 고른 마이크가 있으면 그걸 사용, 없으면 첫 번째 마이크
    private void SelectMicrophone()
    {
        if (Microphone.devices.Length == 0) return;

        string preferred = GameSettingManager.Instance != null ? GameSettingManager.Instance.selectedMicName : null;
        selectedDevice = !string.IsNullOrEmpty(preferred) && System.Array.IndexOf(Microphone.devices, preferred) >= 0
            ? preferred
            : Microphone.devices[0];
    }

    void Update()
    {
        if (string.IsNullOrEmpty(selectedDevice)) return;

        // 몹이 도착하거나 사라질 때 안내 자막 변경 (녹음·처리 중이 아닐 때만)
        bool canCast = SkillManager.CanCast;
        if (canCast != wasCastable && !isRecording && !isProcessing)
        {
            SetSubtitle(canCast ? HintReady : HintWaiting);
        }
        wasCastable = canCast;

        if (Input.GetKeyDown(KeyCode.Space))
        {
            if (isProcessing) Debug.Log("[VoiceRecorder] 이전 주문을 처리 중이라 무시합니다.");
            else if (!canCast)
            {
                Debug.Log("[VoiceRecorder] 아직 적이 자리에 오지 않아 녹음할 수 없습니다.");
                SetSubtitle("아직 적이 자리에 오지 않았습니다!");
            }
            else StartRecording();
        }

        // 최대 녹음 시간(10초)을 넘기면 자동으로 끝내고 전송
        if (isRecording && Time.time - recordStartTime >= MaxRecordingTime - 0.1f)
        {
            StopRecording();
        }

        if (Input.GetKeyUp(KeyCode.Space) && isRecording)
        {
            StopRecording();
        }
    }

    private void StartRecording()
    {
        if (Microphone.IsRecording(selectedDevice)) Microphone.End(selectedDevice);

        recordingClip = Microphone.Start(selectedDevice, false, MaxRecordingTime, SampleRate);
        recordStartTime = Time.time;
        isRecording = true;
        SetSubtitle("[ 마법 영창 중... ]");
        Debug.Log($"[VoiceRecorder] 녹음 시작 (마이크: {selectedDevice})");
    }

    private void StopRecording()
    {
        isRecording = false;

        int recordingLength = Microphone.IsRecording(selectedDevice)
            ? Microphone.GetPosition(selectedDevice)
            : (recordingClip != null ? recordingClip.samples : 0); // 최대 시간까지 녹음된 경우
        Microphone.End(selectedDevice);

        Debug.Log($"[VoiceRecorder] 녹음 종료 ({(float)recordingLength / SampleRate:F2}초)");

        if (recordingClip == null || recordingLength <= SampleRate * minRecordingTime)
        {
            Debug.Log("[VoiceRecorder] 녹음이 너무 짧아 무시합니다. 스페이스바를 0.2초 이상 눌러 주세요.");
            SetSubtitle("주문이 들리지 않았습니다.");
            return;
        }

        AudioClip trimmedClip = TrimClip(recordingClip, recordingLength);

        // 성량 측정 (성량 조건이 켜져 있으면 미달 시 취소)
        LastVolumeDb = MeasureVolumeDb(trimmedClip);
        if (useVolumeGate && LastVolumeDb < minVolumeDb)
        {
            Debug.Log($"[VoiceRecorder] 성량 부족: {LastVolumeDb:F1} dB (기준 {minVolumeDb} dB)");
            SetSubtitle("목소리가 너무 작습니다! 더 크게 외쳐 주세요.");
            return;
        }

        isProcessing = true;
        SetSubtitle("[ 마나 흐름 해석 중... ]");
        Debug.Log($"[VoiceRecorder] {(useMockServer ? "Mock" : "서버")}로 전송 (성량 {LastVolumeDb:F1} dB)");

        if (useMockServer) StartCoroutine(SendAudioToMock());
        else StartCoroutine(SendAudioToBackend(trimmedClip));
    }

    private AudioClip TrimClip(AudioClip clip, int samplesRecorded)
    {
        float[] soundData = new float[samplesRecorded * clip.channels];
        clip.GetData(soundData, 0);

        AudioClip trimmed = AudioClip.Create("RecordedSpell", samplesRecorded, clip.channels, clip.frequency, false);
        trimmed.SetData(soundData, 0);
        return trimmed;
    }

    // RMS 성량을 dBFS로 계산 (0 = 최대, 조용할수록 음수)
    private float MeasureVolumeDb(AudioClip clip)
    {
        float[] samples = new float[clip.samples * clip.channels];
        clip.GetData(samples, 0);
        if (samples.Length == 0) return -80f;

        double sum = 0;
        foreach (float s in samples) sum += s * s;
        float rms = Mathf.Sqrt((float)(sum / samples.Length));
        return rms > 0.0001f ? 20f * Mathf.Log10(rms) : -80f;
    }

    // =========================================================
    // 서버 통신
    // =========================================================
    private IEnumerator SendAudioToBackend(AudioClip clip)
    {
        // 1. AudioClip을 WAV 바이트 배열로 인코딩
        byte[] wavData = EncodeToWAV(clip);

        // 2. HTTP 전송용 폼(Form) 데이터 생성
        WWWForm form = new WWWForm();
        // 백엔드 API에서 받을 파일 파라미터 이름("file"), 바이트 데이터, 임시 파일명, MIME 타입
        form.AddBinaryData("file", wavData, "spell.wav", "audio/wav");

        // 3. 서버로 POST 요청 전송
        using (UnityWebRequest www = UnityWebRequest.Post(backendUrl, form))
        {
            www.timeout = requestTimeout;
            yield return www.SendWebRequest();

            if (www.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError($"통신 에러: {www.error}");
                SetSubtitle("주문 해석에 실패했습니다. (통신 오류)");
                isProcessing = false;
                yield break;
            }

            // 4. 성공 시 JSON 응답 파싱
            WhisperResponse responseData = null;
            try
            {
                responseData = JsonUtility.FromJson<WhisperResponse>(www.downloadHandler.text);
            }
            catch (System.Exception e)
            {
                Debug.LogError($"응답 파싱 실패: {e.Message}\n{www.downloadHandler.text}");
            }

            if (responseData == null)
            {
                SetSubtitle("주문 해석에 실패했습니다. (응답 오류)");
                isProcessing = false;
                yield break;
            }

            // 자막용 날것의 텍스트와 마법 시전용 확정 텍스트를 인자로 각각 넘김
            DisplayRecognizedSpell(responseData.raw_text, responseData.mapped_spell);
        }
    }

    // 백엔드 미연결 테스트용: mockSpellSequence를 순서대로 시전
    private IEnumerator SendAudioToMock()
    {
        yield return new WaitForSeconds(mockDelay);

        string spell = (mockSpellSequence != null && mockSpellSequence.Length > 0)
            ? mockSpellSequence[mockIndex++ % mockSpellSequence.Length]
            : "";

        Debug.Log($"[VoiceRecorder] Mock 인식 결과: \"{spell}\"");
        DisplayRecognizedSpell(spell, spell);
    }

    // =========================================================
    // 자막 연출 + 시전
    // =========================================================
    private void DisplayRecognizedSpell(string rawText, string mappedSpell)
    {
        if (typingCoroutine != null) StopCoroutine(typingCoroutine);
        typingCoroutine = StartCoroutine(SpellTextSequence(rawText ?? "", mappedSpell ?? ""));
    }

    // 순차적 자막 연출 코루틴 (타이핑 -> 판정 -> 팝업 효과 -> 스킬 시전)
    private IEnumerator SpellTextSequence(string rawText, string mappedSpell)
    {
        // ----------------------------------------------------
        // 1단계: 날것의 텍스트 타이핑 연출
        // ----------------------------------------------------
        if (subtitleText != null && !string.IsNullOrEmpty(rawText))
        {
            subtitleText.text = "\"";
            foreach (char c in rawText)
            {
                subtitleText.text += c;
                yield return new WaitForSeconds(typingSpeed);
            }
            subtitleText.text += "\"";

            // 유저가 날것의 텍스트를 인식할 수 있도록 0.5초 대기
            yield return new WaitForSeconds(0.5f);
        }

        // ----------------------------------------------------
        // 2단계: 시전 가능한 주문인지 판정 (실패 이유를 자막으로 안내)
        // ----------------------------------------------------
        SkillManager skill = Skill;
        SpellData spell = (skill != null && !string.IsNullOrWhiteSpace(mappedSpell)) ? skill.FindSpell(mappedSpell) : null;

        string failMessage = null;
        if (skill == null) failMessage = "SkillManager가 연결되지 않았습니다.";
        else if (spell == null) failMessage = "알 수 없는 주문입니다...";
        else if (!skill.IsUnlocked(spell)) failMessage = $"아직 배우지 않은 주문입니다: {spell.spellName}";
        else if (!SkillManager.CanCast) failMessage = "공격할 적이 없습니다.";

        if (failMessage != null)
        {
            Debug.LogWarning($"[VoiceRecorder] 시전 실패 - {failMessage} (인식된 주문: \"{mappedSpell}\")");
            SetSubtitle($"<color=#AAAAAA>{failMessage}</color>");
            isProcessing = false;
            typingCoroutine = null;
            yield break;
        }

        // ----------------------------------------------------
        // 3단계: 확정된 스펠로 덮어씌우기 + 팝(Pop) 애니메이션
        // ----------------------------------------------------
        if (subtitleText != null)
        {
            // TextMeshPro Rich Text를 사용해 텍스트를 노란색 굵은 글씨로 변경
            subtitleText.text = $"<color=#FFD700><b>{spell.spellName}!</b></color>";

            Vector3 originalScale = subtitleText.transform.localScale;
            Vector3 targetScale = originalScale * 1.5f; // 1.5배로 확대
            const float popDuration = 0.15f;

            float elapsed = 0f;
            while (elapsed < popDuration) // 텍스트 커지기
            {
                subtitleText.transform.localScale = Vector3.Lerp(originalScale, targetScale, elapsed / popDuration);
                elapsed += Time.deltaTime;
                yield return null;
            }

            elapsed = 0f;
            while (elapsed < popDuration) // 텍스트 원래 크기로 돌아오기
            {
                subtitleText.transform.localScale = Vector3.Lerp(targetScale, originalScale, elapsed / popDuration);
                elapsed += Time.deltaTime;
                yield return null;
            }

            subtitleText.transform.localScale = originalScale; // 크기 오차 보정
        }

        // ----------------------------------------------------
        // 4단계: 연출이 끝난 직후 실제 마법 시전
        // ----------------------------------------------------
        skill.CastSpell(spell.spellName);

        isProcessing = false;
        typingCoroutine = null;
    }

    private void SetSubtitle(string message)
    {
        if (subtitleText != null) subtitleText.text = message;
    }

    // AudioClip -> WAV 헤더 및 16-bit PCM 데이터 바이트 변환기
    private byte[] EncodeToWAV(AudioClip clip)
    {
        float[] samples = new float[clip.samples * clip.channels];
        clip.GetData(samples, 0);

        short[] intData = new short[samples.Length];
        for (int i = 0; i < samples.Length; i++)
        {
            // float(-1.0f ~ 1.0f) 데이터를 16비트 정수(-32768 ~ 32767)로 변환
            intData[i] = (short)(Mathf.Clamp(samples[i], -1f, 1f) * 32767f);
        }

        using (MemoryStream memoryStream = new MemoryStream())
        using (BinaryWriter writer = new BinaryWriter(memoryStream))
        {
            writer.Write("RIFF".ToCharArray());
            writer.Write(36 + intData.Length * 2);
            writer.Write("WAVE".ToCharArray());
            writer.Write("fmt ".ToCharArray());
            writer.Write(16);
            writer.Write((short)1); // PCM 포맷
            writer.Write((short)clip.channels);
            writer.Write(clip.frequency);
            writer.Write(clip.frequency * clip.channels * 2);
            writer.Write((short)(clip.channels * 2));
            writer.Write((short)16);
            writer.Write("data".ToCharArray());
            writer.Write(intData.Length * 2);

            foreach (short sample in intData)
            {
                writer.Write(sample);
            }
            return memoryStream.ToArray();
        }
    }

    // JSON 응답을 파싱하기 위한 데이터 클래스
    [System.Serializable]
    public class WhisperResponse
    {
        public string raw_text;     // 화면 자막에 띄울 날것의 발음
        public string mapped_spell; // 스킬 매니저로 넘길 확정된 마법 이름 (매칭 실패 시 빈 문자열)
    }
}