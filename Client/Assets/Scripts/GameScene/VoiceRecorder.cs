using System.Collections;
using TMPro;
using UnityEngine;

// �����̽��ٸ� ������ ���� ���� �� ����(STT)�� ���� �� �ڸ� ���� �� ���� ����
// - ���� ��������Ʈ�� �����ؾ߸� ���� ����
// - �� ���� �ϳ��� �ֹ��� ó�� (ó�� �߿��� �� ���� �Ұ�)
// - ���� ���� �׽�Ʈ�� �� �ִ� Mock ��� ����
public class VoiceRecorder : MonoBehaviour
{
    public TextMeshProUGUI subtitleText;
    public float typingSpeed = 0.05f;   // ���ڴ� ��� �ð� (��)

    [Header("��Ʈ��ũ ����")]
    [Tooltip("üũ�ϸ� ���� ��� �Ʒ� Mock ����� �׽�Ʈ (�鿣�� ���� ����)")]
    public bool useMockServer = true;
    // �鿣�� API �ּ� (FastAPI ������ ��������Ʈ �Է�)
    public string backendUrl = "http://localhost:8000/transcribe";
    [Tooltip("���� ���� ��� �ִ� �ð�(��)")]
    public int requestTimeout = 10;

    [Header("Mock �׽�Ʈ (Use Mock Server üũ ��)")]
    [Tooltip("������ ������ ������� ����� ���� �̸�. SpellData�� Spell Name�� ���ƾ� �մϴ�.")]
    public string[] mockSpellSequence = { "��", "�ٶ�", "��" };
    [Tooltip("���� ���� �ð� �䳻 (��)")]
    public float mockDelay = 1.0f;

    [Header("���� ����")]
    [Tooltip("�ʹ� ª�� ������ ���� (��)")]
    public float minRecordingTime = 0.2f;
    [Tooltip("üũ�ϸ� ���� ���� �̻����� ���ؾ� �ֹ��� ���۵˴ϴ�")]
    public bool useVolumeGate = false;
    [Tooltip("�ּ� ���� (dBFS, 0�� �ִ�. -30 ~ -20 �������� ����)")]
    public float minVolumeDb = -30f;

    [Header("�ڸ� ���� ����")]
    public float typingSpeed = 0.05f; // ���ڴ� ��� ��� �ð� (��)

    [Header("�Ŵ��� ����")]
    public SkillManager skillManager;

    // ���� ������ ���� (���� ������ ���Ŀ� ��� ����)
    public float LastVolumeDb { get; private set; } = -80f;

    private const int SampleRate = 16000;
    private const int MaxRecordingTime = 10;

    private const string HintReady = "�����̽��ٸ� ���� ä�� �ֹ��� ��â�ϼ���.";
    private const string HintWaiting = "���� �ٰ����� �ֽ��ϴ�...";

    private string selectedDevice;
    private AudioClip recordingClip;
    private bool isRecording;      // �����̽��ٸ� ������ �ִ� ����
    private bool isProcessing;     // ���� ���� ��� ~ �ڸ� ���� ~ ��������
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
            Debug.LogError("����ũ ��ġ�� �����ϴ�.");
            SetSubtitle("����ũ�� Ȯ���� �� �����ϴ�.");
            return;
        }

        wasCastable = SkillManager.CanCast;
        SetSubtitle(wasCastable ? HintReady : HintWaiting);
    }

    // ȯ�漳������ ���� ����ũ�� ������ �װ� ���, ������ ù ��° ����ũ
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

        // ���� �����ϰų� ����� �� �ȳ� �ڸ� ���� (������ó�� ���� �ƴ� ����)
        bool canCast = SkillManager.CanCast;
        if (canCast != wasCastable && !isRecording && !isProcessing)
        {
            SetSubtitle(canCast ? HintReady : HintWaiting);
        }
        wasCastable = canCast;

        if (Input.GetKeyDown(KeyCode.Space))
        {
            if (isProcessing) Debug.Log("[VoiceRecorder] ���� �ֹ��� ó�� ���̶� �����մϴ�.");
            else if (!canCast)
            {
                Debug.Log("[VoiceRecorder] ���� ���� �ڸ��� ���� �ʾ� ������ �� �����ϴ�.");
                SetSubtitle("���� ���� �ڸ��� ���� �ʾҽ��ϴ�!");
            }
            else StartRecording();
        }

        // �ִ� ���� �ð�(10��)�� �ѱ�� �ڵ����� ������ ����
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
        SetSubtitle("[ ���� ��â ��... ]");
        Debug.Log($"[VoiceRecorder] ���� ���� (����ũ: {selectedDevice})");
    }

    private void StopRecording()
    {
        isRecording = false;

        int recordingLength = Microphone.IsRecording(selectedDevice)
            ? Microphone.GetPosition(selectedDevice)
            : (recordingClip != null ? recordingClip.samples : 0); // �ִ� �ð����� ������ ���
        Microphone.End(selectedDevice);

        Debug.Log($"[VoiceRecorder] ���� ���� ({(float)recordingLength / SampleRate:F2}��)");

        if (recordingClip == null || recordingLength <= SampleRate * minRecordingTime)
        {
            Debug.Log("[VoiceRecorder] ������ �ʹ� ª�� �����մϴ�. �����̽��ٸ� 0.2�� �̻� ���� �ּ���.");
            SetSubtitle("�ֹ��� �鸮�� �ʾҽ��ϴ�.");
            return;
        }

        AudioClip trimmedClip = TrimClip(recordingClip, recordingLength);

        // ���� ���� (���� ������ ���� ������ �̴� �� ���)
        LastVolumeDb = MeasureVolumeDb(trimmedClip);
        if (useVolumeGate && LastVolumeDb < minVolumeDb)
        {
            Debug.Log($"[VoiceRecorder] ���� ����: {LastVolumeDb:F1} dB (���� {minVolumeDb} dB)");
            SetSubtitle("��Ҹ��� �ʹ� �۽��ϴ�! �� ũ�� ���� �ּ���.");
            return;
        }

        isProcessing = true;
        SetSubtitle("[ ���� �帧 �ؼ� ��... ]");
        Debug.Log($"[VoiceRecorder] {(useMockServer ? "Mock" : "����")}�� ���� (���� {LastVolumeDb:F1} dB)");

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

    // RMS ������ dBFS�� ��� (0 = �ִ�, �����Ҽ��� ����)
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
    // ���� ���
    // =========================================================
    private IEnumerator SendAudioToBackend(AudioClip clip)
    {
        // 1. AudioClip�� WAV ����Ʈ �迭�� ���ڵ�
        byte[] wavData = EncodeToWAV(clip);

        // 2. HTTP ���ۿ� ��(Form) ������ ����
        WWWForm form = new WWWForm();
        // �鿣�� API���� ���� ���� �Ķ���� �̸�("file"), ����Ʈ ������, �ӽ� ���ϸ�, MIME Ÿ��
        form.AddBinaryData("file", wavData, "spell.wav", "audio/wav");

        // 3. ������ POST ��û ����
        using (UnityWebRequest www = UnityWebRequest.Post(backendUrl, form))
        {
            www.timeout = requestTimeout;
            yield return www.SendWebRequest();

            if (www.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError($"��� ����: {www.error}");
                SetSubtitle("�ֹ� �ؼ��� �����߽��ϴ�. (��� ����)");
                isProcessing = false;
                yield break;
            }

            // 4. ���� �� JSON ���� �Ľ�
            WhisperResponse responseData = null;
            try
            {
                responseData = JsonUtility.FromJson<WhisperResponse>(www.downloadHandler.text);
            }
            catch (System.Exception e)
            {
                Debug.LogError($"���� �Ľ� ����: {e.Message}\n{www.downloadHandler.text}");
            }

            if (responseData == null)
            {
                SetSubtitle("�ֹ� �ؼ��� �����߽��ϴ�. (���� ����)");
                isProcessing = false;
                yield break;
            }

            // �ڸ��� ������ �ؽ�Ʈ�� ���� ������ Ȯ�� �ؽ�Ʈ�� ���ڷ� ���� �ѱ�
            DisplayRecognizedSpell(responseData.raw_text, responseData.mapped_spell);
        }
    }

    // �鿣�� �̿��� �׽�Ʈ��: mockSpellSequence�� ������� ����
    private IEnumerator SendAudioToMock()
    {
        yield return new WaitForSeconds(mockDelay);

        string spell = (mockSpellSequence != null && mockSpellSequence.Length > 0)
            ? mockSpellSequence[mockIndex++ % mockSpellSequence.Length]
            : "";

        Debug.Log($"[VoiceRecorder] Mock �ν� ���: \"{spell}\"");
        DisplayRecognizedSpell(spell, spell);
    }

    // =========================================================
    // �ڸ� ���� + ����
    // =========================================================
    private void DisplayRecognizedSpell(string rawText, string mappedSpell)
    {
        if (typingCoroutine != null) StopCoroutine(typingCoroutine);
        typingCoroutine = StartCoroutine(SpellTextSequence(rawText ?? "", mappedSpell ?? ""));
    }

    // ������ �ڸ� ���� �ڷ�ƾ (Ÿ���� -> ���� -> �˾� ȿ�� -> ��ų ����)
    private IEnumerator SpellTextSequence(string rawText, string mappedSpell)
    {
        // ----------------------------------------------------
        // 1�ܰ�: ������ �ؽ�Ʈ Ÿ���� ����
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

            // ������ ������ �ؽ�Ʈ�� �ν��� �� �ֵ��� 0.5�� ���
            yield return new WaitForSeconds(0.5f);
        }

        // ----------------------------------------------------
        // 2�ܰ�: ���� ������ �ֹ����� ���� (���� ������ �ڸ����� �ȳ�)
        // ----------------------------------------------------
        SkillManager skill = Skill;
        SpellData spell = (skill != null && !string.IsNullOrWhiteSpace(mappedSpell)) ? skill.FindSpell(mappedSpell) : null;

        string failMessage = null;
        if (skill == null) failMessage = "SkillManager�� ������� �ʾҽ��ϴ�.";
        else if (spell == null) failMessage = "�� �� ���� �ֹ��Դϴ�...";
        else if (!skill.IsUnlocked(spell)) failMessage = $"���� ����� ���� �ֹ��Դϴ�: {spell.spellName}";
        else if (!SkillManager.CanCast) failMessage = "������ ���� �����ϴ�.";

        if (failMessage != null)
        {
            Debug.LogWarning($"[VoiceRecorder] ���� ���� - {failMessage} (�νĵ� �ֹ�: \"{mappedSpell}\")");
            SetSubtitle($"<color=#AAAAAA>{failMessage}</color>");
            isProcessing = false;
            typingCoroutine = null;
            yield break;
        }

        // ----------------------------------------------------
        // 3�ܰ�: Ȯ���� ����� ������ + ��(Pop) �ִϸ��̼�
        // ----------------------------------------------------
        if (subtitleText != null)
        {
            // TextMeshPro Rich Text�� ����� �ؽ�Ʈ�� ����� ���� �۾��� ����
            subtitleText.text = $"<color=#FFD700><b>{spell.spellName}!</b></color>";

            Vector3 originalScale = subtitleText.transform.localScale;
            Vector3 targetScale = originalScale * 1.5f; // 1.5��� Ȯ��
            const float popDuration = 0.15f;

            float elapsed = 0f;
            while (elapsed < popDuration) // �ؽ�Ʈ Ŀ����
            {
                subtitleText.transform.localScale = Vector3.Lerp(originalScale, targetScale, elapsed / popDuration);
                elapsed += Time.deltaTime;
                yield return null;
            }

            elapsed = 0f;
            while (elapsed < popDuration) // �ؽ�Ʈ ���� ũ��� ���ƿ���
            {
                subtitleText.transform.localScale = Vector3.Lerp(targetScale, originalScale, elapsed / popDuration);
                elapsed += Time.deltaTime;
                yield return null;
            }

            subtitleText.transform.localScale = originalScale; // ũ�� ���� ����
        }

        // ----------------------------------------------------
        // 4�ܰ�: ������ ���� ���� ���� ���� ����
        // ----------------------------------------------------
        skill.CastSpell(spell.spellName);

        isProcessing = false;
        typingCoroutine = null;
    }

    private void SetSubtitle(string message)
    {
        if (subtitleText != null) subtitleText.text = message;
    }

    // AudioClip -> WAV ��� �� 16-bit PCM ������ ����Ʈ ��ȯ��
    private byte[] EncodeToWAV(AudioClip clip)
    {
        float[] samples = new float[clip.samples * clip.channels];
        clip.GetData(samples, 0);

        short[] intData = new short[samples.Length];
        for (int i = 0; i < samples.Length; i++)
        {
            // float(-1.0f ~ 1.0f) �����͸� 16��Ʈ ����(-32768 ~ 32767)�� ��ȯ
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
            writer.Write((short)1); // PCM ����
            writer.Write((short)clip.channels);
            writer.Write(clip.frequency);
            writer.Write(clip.frequency * clip.channels * 2);
            writer.Write((short)(clip.channels * 2));
            writer.Write((short)16);
            writer.Write("data".ToCharArray());
            writer.Write(intData.Length * 2);

            foreach (short sample in intData)
            {
                subtitleText.text += c;
                yield return new WaitForSeconds(typingSpeed);
            }
            subtitleText.text += "\"";
            yield return new WaitForSeconds(0.5f);
        }

        if (spellName == null)
        {
            subtitleText.text = "<color=#888888>�ֹ��� ��������ϴ�...</color>";
            yield break;
        }

        subtitleText.text = $"<color=#FFD700><b>{spellName}!</b></color>";
        yield return Pop();

        if (skillBridge != null && !skillBridge.TryCast(spellName))
            subtitleText.text = "<color=#888888>���� �ٷ� �� ���� �ֹ��Դϴ�.</color>";
    }

    IEnumerator Pop()   // 0.15�� ���� 1.5��� Ŀ���ٰ� 0.15�� ���� ���� ũ���
    {
        public string raw_text;     // ȭ�� �ڸ��� ��� ������ ����
        public string mapped_spell; // ��ų �Ŵ����� �ѱ� Ȯ���� ���� �̸� (��Ī ���� �� �� ���ڿ�)
    }
}