using System;
using UnityEngine;

/// <summary>
/// 마이크 소리를 계속 읽어서 10ms 단위의 모노 float 샘플 조각으로 내보내는 컴포넌트.
///
/// 입력
///   Microphone.Start 를 반복 녹음(loop) 모드로 켜 두고,
///   매 프레임 마지막으로 읽은 위치부터 현재 녹음 위치까지의 새 샘플을 읽는다.
///
/// 출력
///   - SamplesAvailable(float[] samples, int count) 이벤트
///   - samples: 모노, -1.0 ~ 1.0, 길이 = SampleRate / 100 (16kHz 면 160개 = 10ms)
///   - Update 안에서 동기적으로 호출되며, 한 프레임에 0번 이상 호출될 수 있다.
///
/// 말소리 여부 판단은 하지 않는다. 그 처리는 VoiceActivityDetector 가 담당한다.
/// </summary>
public class VoiceInput : MonoBehaviour
{
    [Header("입력")]
    [Tooltip("사용할 마이크. (시스템 기본 장치)를 고르면 Windows 기본 마이크를 사용한다. 플레이 중에 바꾸면 바로 다시 시작한다")]
    [MicrophoneDevice]
    [SerializeField] string deviceName = "";

    /// <summary>마이크가 바뀌어 녹음을 다시 시작했을 때 호출된다. 샘플레이트가 바뀔 수 있으므로 구독자는 상태를 초기화해야 한다.</summary>
    public event Action DeviceChanged;

    /// <summary>
    /// 사용할 마이크 이름 (게임 설정 연동용). 비우면 시스템 기본 장치.
    /// 플레이 중에 바꾸면 다음 프레임에 새 장치로 다시 시작한다 (Inspector 에서 바꿀 때와 같은 동작).
    /// 연동: VoiceRecorder 가 GameSettingManager.Instance.selectedMicName 을 넣어 준다.
    /// </summary>
    public string DeviceName
    {
        get => deviceName;
        set => deviceName = value ?? "";
    }

    bool micStarted;             // device 가 null(기본 장치)일 수 있으므로 시작 여부를 따로 기록한다
    string appliedDeviceName;    // 마지막으로 적용한 deviceName. Inspector 값과 다르면 다시 시작한다
    /// <summary>
    /// deviceName 에 해당하는 장치 이름을 반환한다. 비어 있거나 연결되어 있지 않으면 null(시스템 기본 장치).
    /// </summary>
    string ResolveDevice()
    {
        string[] list = Microphone.devices;
        for (int i = 0; i < list.Length; i++)
            Debug.Log($"[VoiceInput] 장치 {i}: {list[i]}");

        if (string.IsNullOrEmpty(deviceName)) return null;
        if (Array.IndexOf(list, deviceName) >= 0) return deviceName;

        Debug.LogWarning($"[VoiceInput] '{deviceName}' 이(가) 연결되어 있지 않아 시스템 기본 장치를 사용합니다.");
        return null;
    }

    void StopMicrophone()
    {
        if (micStarted && Microphone.IsRecording(device)) Microphone.End(device);   // device 가 null 이면 기본 장치를 종료
        micStarted = false;
        clip = null;
        IsRunning = false;
    }

    [SerializeField] int requestedSampleRate = 16000;   // Microphone.Start 에 요청하는 값. 실제 값은 SampleRate 에 저장된다
    [SerializeField] int loopBufferSec = 10;            // 반복 녹음 버퍼 길이(초). 최대 발화 길이(5초)보다 길어야 한다

    /// <summary>
    /// 10ms 분량의 모노 샘플이 준비될 때마다 호출된다. (샘플 배열, 유효 개수)
    /// 배열은 다음 호출에서 다시 사용되므로, 구독자는 호출 안에서 처리하거나 값을 복사해야 한다.
    /// </summary>
    public event Action<float[], int> SamplesAvailable;

    public int SampleRate { get; private set; }    // 실제로 출력하는 샘플레이트 (clip.frequency)
    public bool IsRunning { get; private set; }    // 출력 중인지 여부

    int blockSize;       // 한 번에 내보내는 샘플 수 = SampleRate / 100 (10ms)
    float[] monoBlock;   // 이벤트로 넘기는 모노 배열. 매번 재사용해 메모리 할당을 줄인다

    // ── 마이크 ──
    string device;       // 사용 중인 마이크 장치 이름
    AudioClip clip;      // 반복 녹음 버퍼. 끝에 도달하면 처음 위치부터 덮어쓴다
    int clipSamples;     // 버퍼의 채널당 샘플 수 (16kHz × 10초 = 160,000)
    int channels;        // 버퍼의 채널 수
    int readPos = -1;    // 다음에 읽을 버퍼 위치(채널당 샘플 번호). -1 = 아직 읽기 시작 전
    float[] readBlock;   // GetData 로 읽어 오는 배열. 채널이 번갈아 저장된 상태 [L0, R0, L1, R1, ...]

    void OnEnable()
    {
        // 컴포넌트가 켜질 때마다 입력을 시작한다 (껐다 켜면 다시 시작)
        StartMicrophone();
    }

    void OnDisable()
    {
        StopMicrophone();
        IsRunning = false;
    }

    void Update()
    {
        // 플레이 중 Inspector 에서 장치를 바꾸면 새 장치로 다시 시작한다
        if (micStarted && deviceName != appliedDeviceName)
        {
            StopMicrophone();
            StartMicrophone();
            DeviceChanged?.Invoke();
            return;
        }

        PumpMicrophone();
    }

    /// <summary>샘플레이트가 정해진 뒤 출력·읽기용 배열을 한 번만 만든다.</summary>
    void Allocate()
    {
        blockSize = Mathf.Max(1, SampleRate / 100);                  // 10ms 분량 (16kHz → 160, 48kHz → 480)
        monoBlock = new float[blockSize];
        readBlock = new float[blockSize * Mathf.Max(1, channels)];   // GetData 는 배열 길이 / 채널 수 만큼 읽는다
    }

    // ─────────────────────────────────────────────
    // 마이크
    // ─────────────────────────────────────────────

    /// <summary>첫 번째 마이크를 반복 녹음 모드로 시작하고 실제 녹음 형식을 저장한다.</summary>
    void StartMicrophone()
    {
        appliedDeviceName = deviceName;   // 이번에 적용한 값을 기록 (Update 의 변경 감지용)

        if (Microphone.devices.Length == 0)
        {
            Debug.LogError("[VoiceInput] 사용할 수 있는 마이크가 없습니다.");
            return;
        }

        device = ResolveDevice();   // null 이면 시스템 기본 장치. Unity 의 Microphone 함수는 null 을 기본 장치로 처리한다
        clip = Microphone.Start(device, true, loopBufferSec, requestedSampleRate);
        if (clip == null)
        {
            Debug.LogError("[VoiceInput] 마이크 녹음을 시작하지 못했습니다.");
            return;
        }
        micStarted = true;

        SampleRate = clip.frequency;
        channels = clip.channels;
        clipSamples = clip.samples;
        Allocate();
        readPos = -1;

        Debug.Log($"[VoiceInput] 마이크: {device ?? "시스템 기본 장치"}, 요청 {requestedSampleRate}Hz → 실제 {SampleRate}Hz, " +
                  $"{channels}ch, 버퍼 {loopBufferSec}초");
    }

    /// <summary>
    /// 마지막으로 읽은 위치(readPos)부터 현재 녹음 위치(writePos)까지 새로 쌓인 샘플을
    /// blockSize 단위로 읽어 모노로 바꾼 뒤 내보낸다. blockSize 미만으로 남은 샘플은 다음 프레임에 읽는다.
    /// </summary>
    void PumpMicrophone()
    {
        if (clip == null) return;

        // 장치 분리 등으로 녹음이 멈춘 경우 다시 시작한다
        if (!Microphone.IsRecording(device))
        {
            Debug.LogWarning("[VoiceInput] 마이크 녹음이 멈춰 다시 시작합니다.");
            StartMicrophone();
            return;
        }

        // 마이크가 다음으로 기록할 버퍼 위치 (채널당 샘플 번호, 0 ~ clipSamples-1).
        // 드라이버가 데이터를 묶음으로 넘기므로 값은 몇 ms 단위로 건너뛰며 증가한다.
        int writePos = Microphone.GetPosition(device);

        if (readPos < 0)
        {
            if (writePos <= 0) return;   // 녹음이 아직 실제로 시작되지 않음
            readPos = writePos;          // 현재 위치부터 읽기 시작한다. 그 앞의 시작 직후 구간은 사용하지 않는다
            IsRunning = true;
            return;
        }

        // 아직 읽지 않은 샘플 수.
        // 녹음 위치가 버퍼 끝을 지나 처음으로 돌아가 readPos 보다 작아진 경우에도
        // clipSamples 를 더한 뒤 나머지를 구하므로 올바른 거리가 나온다.
        // 예) readPos 159,900, writePos 200, clipSamples 160,000 → 300
        // 주의: 읽기가 한 바퀴(10초) 이상 밀리면 이 계산으로는 구분할 수 없고, 덮어쓰인 구간은 유실된다.
        int available = (writePos - readPos + clipSamples) % clipSamples;

        while (available >= blockSize)
        {
            // readPos 부터 blockSize 개를 읽는다.
            // 읽는 범위가 버퍼 끝을 넘으면 Unity 가 버퍼 처음부터 이어서 채워 주는 동작을 전제로 한다.
            clip.GetData(readBlock, readPos);

            // 채널이 번갈아 저장된 데이터를 채널 평균으로 모노 변환한다 (1채널이면 그대로 복사)
            for (int i = 0; i < blockSize; i++)
            {
                float sum = 0f;
                for (int c = 0; c < channels; c++) sum += readBlock[i * channels + c];
                monoBlock[i] = sum / channels;
            }

            readPos = (readPos + blockSize) % clipSamples;   // 읽은 만큼 읽기 위치를 옮긴다 (버퍼 끝에서 0으로 돌아감)
            available -= blockSize;
            SamplesAvailable?.Invoke(monoBlock, blockSize);
        }
    }
}