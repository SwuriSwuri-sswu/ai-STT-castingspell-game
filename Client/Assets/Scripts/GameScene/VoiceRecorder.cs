using UnityEngine;
using System.Collections;

public class VoiceRecorder : MonoBehaviour
{
    private string selectedDevice;
    private AudioClip recordingClip;
    private const int SampleRate = 16000; // Whisper 및 음성인식 AI 표준 주파수 (16kHz)
    private const int MaxRecordingTime = 10; // 최대 녹음 시간(초)

    void Start()
    {
        // 1. 시스템에 연결된 마이크 장치 확인
        if (Microphone.devices.Length > 0)
        {
            selectedDevice = Microphone.devices[0]; // 기본 마이크 선택
            Debug.Log($"사용 중인 마이크: {selectedDevice}");
        }
        else
        {
            Debug.LogError("연결된 마이크 장치가 없습니다!");
        }
    }

    void Update()
    {
        // 마이크 장치가 없을 시 동작 중단
        if (string.IsNullOrEmpty(selectedDevice)) return;

        // 스페이스바를 누르면 녹음 시작
        if (Input.GetKeyDown(KeyCode.Space))
        {
            StartRecording();
        }

        // 스페이스바를 떼면 녹음 종료
        if (Input.GetKeyUp(KeyCode.Space))
        {
            StopRecording();
        }
    }

    private void StartRecording()
    {
        // 이미 녹음 중이면 중지
        if (Microphone.IsRecording(selectedDevice))
        {
            Microphone.End(selectedDevice);
        }

        // 녹음 시작: (장치이름, 루프여부, 최대길이(초), 샘플링레이트)
        recordingClip = Microphone.Start(selectedDevice, false, MaxRecordingTime, SampleRate);
        Debug.Log("마법 영창 녹음 시작...");
    }

    private void StopRecording()
    {
        if (!Microphone.IsRecording(selectedDevice)) return;

        // 마이크가 실제로 기록한 정확한 샘플 위치 가져오기
        int recordingLength = Microphone.GetPosition(selectedDevice);
        Microphone.End(selectedDevice);

        if (recordingLength <= 0)
        {
            Debug.LogWarning("녹음된 데이터가 없습니다.");
            return;
        }

        // 10초로 잡았던 빈 AudioClip에서 실제 말한 구간만큼만 잘라내기
        AudioClip trimmedClip = TrimClip(recordingClip, recordingLength);
        Debug.Log($"녹음 완료: 길이 {trimmedClip.length}초");

        // TODO: 여기서 trimmedClip을 WAV 바이트로 변환해 백엔드 API로 전송
    }

    // 뒤쪽의 무음(미녹음) 영역을 잘라내어 용량을 최적화하는 함수
    private AudioClip TrimClip(AudioClip clip, int samplesRecorded)
    {
        float[] soundData = new float[samplesRecorded * clip.channels];
        clip.GetData(soundData, 0);

        AudioClip trimmed = AudioClip.Create("RecordedSpell", samplesRecorded, clip.channels, clip.frequency, false);
        trimmed.SetData(soundData, 0);
        return trimmed;
    }
}