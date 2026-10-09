using UnityEngine;
using TMPro;
using System.Collections;
using System.IO;
using UnityEngine.Networking;

public class VoiceRecorder : MonoBehaviour
{
    [Header("UI 설정")]
    public TextMeshProUGUI subtitleText;

    [Header("네트워크 설정")]
    // 백엔드 API 주소 (FastAPI 서버의 엔드포인트 입력)
    public string backendUrl = "http://localhost:8000/transcribe";

    private string selectedDevice;
    private AudioClip recordingClip;
    private const int SampleRate = 16000;
    private const int MaxRecordingTime = 10;

    [Header("자막 연출 설정")]
    public float typingSpeed = 0.05f; // 글자당 출력 대기 시간 (초)
    private Coroutine typingCoroutine; // 현재 실행 중인 코루틴 추적용

    [Header("매니저 연결")]
    public SkillManager skillManager;

    void Start()
    {
        if (Microphone.devices.Length > 0)
        {
            selectedDevice = Microphone.devices[0];
        }
        else
        {
            Debug.LogError("마이크 장치가 없습니다.");
            if (subtitleText != null) subtitleText.text = "마이크를 확인할 수 없습니다.";
        }

        if (subtitleText != null) subtitleText.text = "스페이스바를 누른 채로 주문을 영창하세요.";
    }

    void Update()
    {
        if (string.IsNullOrEmpty(selectedDevice)) return;

        if (Input.GetKeyDown(KeyCode.Space)) StartRecording();
        if (Input.GetKeyUp(KeyCode.Space)) StopRecording();
    }

    private void StartRecording()
    {
        if (Microphone.IsRecording(selectedDevice)) Microphone.End(selectedDevice);

        recordingClip = Microphone.Start(selectedDevice, false, MaxRecordingTime, SampleRate);
        if (subtitleText != null) subtitleText.text = "[ 마법 영창 중... ]";
    }

    private void StopRecording()
    {
        if (!Microphone.IsRecording(selectedDevice)) return;

        int recordingLength = Microphone.GetPosition(selectedDevice);
        Microphone.End(selectedDevice);

        if (recordingLength <= 0)
        {
            if (subtitleText != null) subtitleText.text = "주문이 들리지 않았습니다.";
            return;
        }

        AudioClip trimmedClip = TrimClip(recordingClip, recordingLength);
        if (subtitleText != null) subtitleText.text = "[ 마나 흐름 해석 중... ]";

        // 녹음 완료 직후 서버로 WAV 데이터 전송 코루틴 시작
        StartCoroutine(SendAudioToBackend(trimmedClip));
    }

    private AudioClip TrimClip(AudioClip clip, int samplesRecorded)
    {
        float[] soundData = new float[samplesRecorded * clip.channels];
        clip.GetData(soundData, 0);

        AudioClip trimmed = AudioClip.Create("RecordedSpell", samplesRecorded, clip.channels, clip.frequency, false);
        trimmed.SetData(soundData, 0);
        return trimmed;
    }

    // 통신 코루틴
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
            yield return www.SendWebRequest();

            if (www.result == UnityWebRequest.Result.ConnectionError || www.result == UnityWebRequest.Result.ProtocolError)
            {
                Debug.LogError($"통신 에러: {www.error}");
                if (subtitleText != null) subtitleText.text = "주문 해석에 실패했습니다. (통신 오류)";
            }
            else
            {
                // 4. 성공 시 JSON 응답 파싱 및 자막 출력
                string jsonResponse = www.downloadHandler.text;
                WhisperResponse responseData = JsonUtility.FromJson<WhisperResponse>(jsonResponse);

                // 자막용 날것의 텍스트와 마법 시전용 확정 텍스트를 인자로 각각 넘김
                DisplayRecognizedSpell(responseData.raw_text, responseData.mapped_spell);
            }
        }
    }

    // STT 결과를 받아 자막 출력을 준비하는 함수
    private void DisplayRecognizedSpell(string rawText, string mappedSpell)
    {
        if (typingCoroutine != null)
        {
            StopCoroutine(typingCoroutine);
        }

        // 1. 자막 타이핑 및 팝업 효과 연출 코루틴 시작
        typingCoroutine = StartCoroutine(SpellTextSequence(rawText, mappedSpell));
    }

    // 순차적 자막 연출 코루틴 (타이핑 -> 팝업 효과 -> 스킬 시전)
    private IEnumerator SpellTextSequence(string rawText, string mappedSpell)
    {
        if (subtitleText == null) yield break;

        // ----------------------------------------------------
        // 1단계: 날것의 텍스트 타이핑 연출
        // ----------------------------------------------------
        subtitleText.text = "\"";
        foreach (char c in rawText)
        {
            subtitleText.text += c;
            yield return new WaitForSeconds(typingSpeed);
        }
        subtitleText.text += "\"";

        // 유저가 날것의 텍스트를 인식할 수 있도록 0.5초 대기
        yield return new WaitForSeconds(0.5f);

        // ----------------------------------------------------
        // 2단계: 확정된 스펠로 덮어씌우기 + 팝(Pop) 애니메이션
        // ----------------------------------------------------
        // TextMeshPro Rich Text를 사용해 텍스트를 노란색 굵은 글씨로 변경
        subtitleText.text = $"<color=#FFD700><b>{mappedSpell}!</b></color>";

        Vector3 originalScale = subtitleText.transform.localScale;
        Vector3 targetScale = originalScale * 1.5f; // 1.5배로 확대
        float popDuration = 0.15f;
        float elapsed = 0f;

        // 텍스트 커지기
        while (elapsed < popDuration)
        {
            subtitleText.transform.localScale = Vector3.Lerp(originalScale, targetScale, elapsed / popDuration);
            elapsed += Time.deltaTime;
            yield return null;
        }

        elapsed = 0f;
        // 텍스트 원래 크기로 돌아오기
        while (elapsed < popDuration)
        {
            subtitleText.transform.localScale = Vector3.Lerp(targetScale, originalScale, elapsed / popDuration);
            elapsed += Time.deltaTime;
            yield return null;
        }

        // 크기 오차 보정
        subtitleText.transform.localScale = originalScale;

        // ----------------------------------------------------
        // 3단계: 연출이 끝난 직후 실제 마법 시전
        // ----------------------------------------------------
        if (skillManager != null)
        {
            skillManager.CastSpell(mappedSpell);
        }
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
            intData[i] = (short)(samples[i] * 32767f);
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
        public string mapped_spell; // 스킬 매니저로 넘길 확정된 마법 이름
    }
}