using System;
using System.IO;
using System.Text;
using UnityEngine;

/// <summary>
/// 소리 입력을 계속 읽어서 10ms 단위의 모노 float 샘플 조각으로 내보내는 컴포넌트.
///
/// 입력 소스
///   - Microphone : Microphone.Start 를 반복 녹음(loop) 모드로 켜 두고,
///                  매 프레임 마지막으로 읽은 위치부터 현재 녹음 위치까지의 새 샘플을 읽는다.
///   - WavFile    : StreamingAssets 의 PCM16 WAV 를 읽어, 실제 경과 시간에 맞춰 같은 형식으로 내보낸다.
///                  (마이크 없이 VAD 를 반복 시험하기 위한 테스트용)
///
/// 출력
///   - SamplesAvailable(float[] samples, int count) 이벤트
///   - samples: 모노, -1.0 ~ 1.0, 길이 = SampleRate / 100 (16kHz 면 160개 = 10ms)
///   - Update 안에서 동기적으로 호출되며, 한 프레임에 0번 이상 호출될 수 있다.
///
/// 말소리 여부 판단은 하지 않는다. 그 처리는 VoiceActivityDetector 가 담당한다.
/// </summary>
public class MicStream : MonoBehaviour
{
    public enum SourceType { Microphone, WavFile }

    [Header("입력")]
    [Tooltip("사용할 마이크. (시스템 기본 장치)를 고르면 Windows 기본 마이크를 사용한다. 플레이 중에 바꾸면 바로 다시 시작한다")]
    [MicrophoneDevice]
    [SerializeField] string deviceName = "";

    /// <summary>마이크가 바뀌어 녹음을 다시 시작했을 때 호출된다. 샘플레이트가 바뀔 수 있으므로 구독자는 상태를 초기화해야 한다.</summary>
    public event Action DeviceChanged;

    /// <summary>현재 사용 중인 장치 이름. null 이면 시스템 기본 장치.</summary>
    public string CurrentDevice => device;

    bool micStarted;             // device 가 null(기본 장치)일 수 있으므로 시작 여부를 따로 기록한다
    string appliedDeviceName;    // 마지막으로 적용한 deviceName. Inspector 값과 다르면 다시 시작한다
    /// <summary>
    /// deviceName 에 해당하는 장치 이름을 반환한다. 비어 있거나 연결되어 있지 않으면 null(시스템 기본 장치).
    /// </summary>
    string ResolveDevice()
    {
        string[] list = Microphone.devices;
        for (int i = 0; i < list.Length; i++)
            Debug.Log($"[MicStream] 장치 {i}: {list[i]}");

        if (string.IsNullOrEmpty(deviceName)) return null;
        if (Array.IndexOf(list, deviceName) >= 0) return deviceName;

        Debug.LogWarning($"[MicStream] '{deviceName}' 이(가) 연결되어 있지 않아 시스템 기본 장치를 사용합니다.");
        return null;
    }

    /// <summary>다른 스크립트(예: 게임 설정 화면)에서 장치를 바꿀 때 사용한다. 빈 문자열 = 시스템 기본 장치.</summary>
    public void SelectDevice(string name)
    {
        deviceName = name ?? "";   // 실제 재시작은 Update 에서 변경을 감지해 처리한다
    }

    void StopMicrophone()
    {
        if (micStarted && Microphone.IsRecording(device)) Microphone.End(device);   // device 가 null 이면 기본 장치를 종료
        micStarted = false;
        clip = null;
        IsRunning = false;
    }

    [SerializeField] SourceType source = SourceType.Microphone;
    [SerializeField] int requestedSampleRate = 16000;   // Microphone.Start 에 요청하는 값. 실제 값은 SampleRate 에 저장된다
    [SerializeField] int loopBufferSec = 10;            // 반복 녹음 버퍼 길이(초). 최대 발화 길이(5초)보다 길어야 한다

    [Header("테스트용 WAV 파일 입력 (source = WavFile)")]
    [SerializeField] string testWavFileName = "test.wav"; // Assets/StreamingAssets 기준 파일 이름
    [SerializeField] float leadingSilenceSec = 1.5f;      // 파일 앞에 붙이는 무음 길이. VAD 소음 측정 시간(1초)보다 길어야 한다
    [SerializeField] float gapSilenceSec = 1.0f;          // 파일 뒤에 붙이는 무음 길이. VAD 가 말 끝(0.4초 무음)을 판단할 수 있어야 한다
    [SerializeField] float fillerNoiseDb = -60f;          // 무음 구간에 넣는 잡음의 RMS 크기(dBFS). 완전한 0 이면 VAD 의 dB 계산이 비정상적으로 낮아진다
    [SerializeField] bool loopWavFile = true;             // true: 파일 끝 + 뒤 무음 후 다시 재생, false: 이후 잡음만 계속 출력

    /// <summary>
    /// 10ms 분량의 모노 샘플이 준비될 때마다 호출된다. (샘플 배열, 유효 개수)
    /// 배열은 다음 호출에서 다시 사용되므로, 구독자는 호출 안에서 처리하거나 값을 복사해야 한다.
    /// </summary>
    public event Action<float[], int> SamplesAvailable;

    public int SampleRate { get; private set; }    // 실제로 출력하는 샘플레이트 (마이크: clip.frequency, 파일: WAV 헤더 값)
    public bool IsRunning { get; private set; }    // 출력 중인지 여부
    public SourceType Source => source;

    int blockSize;       // 한 번에 내보내는 샘플 수 = SampleRate / 100 (10ms)
    float[] monoBlock;   // 이벤트로 넘기는 모노 배열. 매번 재사용해 메모리 할당을 줄인다

    // ── 마이크 ──
    string device;       // 사용 중인 마이크 장치 이름
    AudioClip clip;      // 반복 녹음 버퍼. 끝에 도달하면 처음 위치부터 덮어쓴다
    int clipSamples;     // 버퍼의 채널당 샘플 수 (16kHz × 10초 = 160,000)
    int channels;        // 버퍼의 채널 수
    int readPos = -1;    // 다음에 읽을 버퍼 위치(채널당 샘플 번호). -1 = 아직 읽기 시작 전
    float[] readBlock;   // GetData 로 읽어 오는 배열. 채널이 번갈아 저장된 상태 [L0, R0, L1, R1, ...]

    // ── WAV 파일 ──
    float[] fileSamples;                 // 모노로 변환한 파일 전체 샘플
    long timelinePos;                    // 재생 시간표상의 현재 위치(샘플 번호). 앞 무음 → 파일 → 뒤 무음 순서
    long leadSamples, gapSamples;        // 앞 무음, 뒤 무음의 샘플 수
    bool fileFinished;                   // 반복 재생이 꺼져 있고 시간표 끝에 도달했는지
    double samplesOwed;                  // 경과 시간에 비해 아직 내보내지 않은 샘플 수 (소수점 포함)
    float fillerAmp;                     // 무음 구간 잡음의 최대 진폭
    readonly System.Random rng = new System.Random();

    void OnEnable()
    {
        // 컴포넌트가 켜질 때마다 입력을 시작한다 (껐다 켜면 다시 시작)
        if (source == SourceType.Microphone) StartMicrophone();
        else StartWavFile();
    }

    void OnDisable()
    {
        StopMicrophone();
        IsRunning = false;
    }

    void Update()
    {
        // 플레이 중 Inspector 에서 장치를 바꾸면 새 장치로 다시 시작한다
        if (source == SourceType.Microphone && micStarted && deviceName != appliedDeviceName)
        {
            StopMicrophone();
            StartMicrophone();
            DeviceChanged?.Invoke();
            return;
        }

        if (source == SourceType.Microphone) PumpMicrophone();
        else PumpWavFile();
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
            Debug.LogError("[MicStream] 사용할 수 있는 마이크가 없습니다.");
            return;
        }

        device = ResolveDevice();   // null 이면 시스템 기본 장치. Unity 의 Microphone 함수는 null 을 기본 장치로 처리한다
        clip = Microphone.Start(device, true, loopBufferSec, requestedSampleRate);
        if (clip == null)
        {
            Debug.LogError("[MicStream] 마이크 녹음을 시작하지 못했습니다.");
            return;
        }
        micStarted = true;

        SampleRate = clip.frequency;
        channels = clip.channels;
        clipSamples = clip.samples;
        Allocate();
        readPos = -1;

        Debug.Log($"[MicStream] 마이크: {device ?? "시스템 기본 장치"}, 요청 {requestedSampleRate}Hz → 실제 {SampleRate}Hz, " +
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
            Debug.LogWarning("[MicStream] 마이크 녹음이 멈춰 다시 시작합니다.");
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

    // ─────────────────────────────────────────────
    // WAV 파일 (테스트용)
    // ─────────────────────────────────────────────

    /// <summary>
    /// 테스트 WAV 를 모노 배열로 읽고, 재생 시간표(앞 무음 → 파일 → 뒤 무음)와 잡음 크기를 준비한다.
    /// </summary>
    void StartWavFile()
    {
        string path = Path.Combine(Application.streamingAssetsPath, testWavFileName);
        if (!File.Exists(path))
        {
            Debug.LogError("[MicStream] 테스트 WAV 파일이 없습니다: " + path);
            return;
        }
        if (!TryParsePcm16Wav(File.ReadAllBytes(path), out fileSamples, out int sr, out string error))
        {
            Debug.LogError("[MicStream] WAV 해석 실패: " + error);
            return;
        }

        SampleRate = sr;   // 파일의 샘플레이트를 그대로 사용한다 (리샘플링하지 않음)
        channels = 1;      // TryParsePcm16Wav 에서 이미 모노로 변환했다
        Allocate();

        leadSamples = (long)(leadingSilenceSec * SampleRate);
        gapSamples = (long)(gapSilenceSec * SampleRate);
        // -1 ~ 1 균등분포 난수의 RMS 는 (진폭 / √3) 이므로,
        // 목표 RMS 10^(dB/20) 을 맞추려면 진폭에 √3 을 곱한다. (-60dB → 약 0.00173)
        fillerAmp = Mathf.Pow(10f, fillerNoiseDb / 20f) * Mathf.Sqrt(3f);

        timelinePos = 0;
        samplesOwed = 0;
        fileFinished = false;
        IsRunning = true;

        Debug.Log($"[MicStream] 파일 입력: {testWavFileName}, {SampleRate}Hz, " +
                  $"{(float)fileSamples.Length / SampleRate:F2}초 (앞 무음 {leadingSilenceSec}초, 뒤 무음 {gapSilenceSec}초)");
    }

    /// <summary>
    /// 지난 프레임 이후 실제로 흐른 시간만큼의 샘플을 시간표에서 꺼내 blockSize 단위로 내보낸다.
    /// 시간표에서 파일 구간이면 파일 샘플, 그 외 구간이면 잡음을 출력한다.
    /// </summary>
    void PumpWavFile()
    {
        if (!IsRunning || fileSamples == null) return;

        // 경과 시간 × 샘플레이트 만큼 내보내야 할 샘플이 늘어난다.
        // unscaledDeltaTime 을 써서 Time.timeScale 과 관계없이 실제 시간 속도로 재생한다.
        samplesOwed += Time.unscaledDeltaTime * SampleRate;
        // 프레임이 오래 멈췄다 재개될 때 한 번에 많은 샘플을 내보내지 않도록 최대 1초 분량으로 제한한다
        if (samplesOwed > SampleRate) samplesOwed = SampleRate;

        long total = leadSamples + fileSamples.Length + gapSamples;   // 시간표 전체 길이 (샘플)

        while (samplesOwed >= blockSize)
        {
            for (int i = 0; i < blockSize; i++)
            {
                if (!fileFinished && timelinePos >= total)
                {
                    // 반복 재생: 앞 무음을 건너뛰고 파일 시작 위치로 돌아간다 (뒤 무음이 재생 사이 간격이 된다)
                    // 반복 안 함: 이후에는 잡음만 출력한다 (마이크가 조용한 상태와 같은 입력)
                    if (loopWavFile) timelinePos = leadSamples;
                    else fileFinished = true;
                }

                long fileIndex = timelinePos - leadSamples;   // 시간표 위치를 파일 내 인덱스로 변환
                bool inFile = !fileFinished && fileIndex >= 0 && fileIndex < fileSamples.Length;
                monoBlock[i] = inFile
                    ? fileSamples[(int)fileIndex]
                    : (float)(rng.NextDouble() * 2.0 - 1.0) * fillerAmp;

                if (!fileFinished) timelinePos++;
            }

            samplesOwed -= blockSize;   // 소수점 이하로 남은 양은 다음 프레임으로 이어진다
            SamplesAvailable?.Invoke(monoBlock, blockSize);
        }
    }

    /// <summary>
    /// PCM 16비트 WAV 바이트를 모노 float 배열(-1.0 ~ 1.0)로 변환한다.
    /// RIFF 청크를 순서대로 읽어 fmt(형식·채널·샘플레이트·비트 수)와 data(샘플 위치·길이)를 찾는다.
    /// 채널이 여러 개면 샘플마다 채널 평균을 낸다.
    /// </summary>
    static bool TryParsePcm16Wav(byte[] b, out float[] mono, out int sampleRate, out string error)
    {
        mono = null; sampleRate = 0; error = null;

        // 0~3 바이트 "RIFF", 8~11 바이트 "WAVE" 확인
        if (b.Length < 12 || Encoding.ASCII.GetString(b, 0, 4) != "RIFF" || Encoding.ASCII.GetString(b, 8, 4) != "WAVE")
        {
            error = "RIFF/WAVE 형식이 아닙니다";
            return false;
        }

        int channels = 0, bits = 0, dataOffset = -1, dataSize = 0;
        int pos = 12;   // 첫 청크 시작 위치
        while (pos + 8 <= b.Length)
        {
            string id = Encoding.ASCII.GetString(b, pos, 4);   // 청크 이름 (4바이트)
            int size = BitConverter.ToInt32(b, pos + 4);        // 청크 본문 크기 (4바이트, 리틀엔디언)
            int body = pos + 8;                                 // 청크 본문 시작 위치

            if (id == "fmt ")
            {
                short format = BitConverter.ToInt16(b, body);       // 1 = PCM
                channels = BitConverter.ToInt16(b, body + 2);
                sampleRate = BitConverter.ToInt32(b, body + 4);
                bits = BitConverter.ToInt16(b, body + 14);          // 샘플당 비트 수
                if (format != 1) { error = "PCM 형식만 지원합니다"; return false; }
            }
            else if (id == "data")
            {
                dataOffset = body;
                dataSize = Math.Min(size, b.Length - body);         // 헤더 값이 실제 길이보다 크면 실제 길이로 자른다
                break;
            }
            pos = body + size + (size & 1);   // 다음 청크로 이동. 청크 크기가 홀수면 1바이트 패딩을 건너뛴다
        }

        if (bits != 16) { error = $"16비트만 지원합니다 (현재 {bits}비트)"; return false; }
        if (channels < 1 || dataOffset < 0) { error = "fmt 또는 data 청크가 없습니다"; return false; }

        // 16비트 정수(-32768 ~ 32767)를 32768 로 나눠 -1.0 ~ 1.0 으로 바꾸고 채널 평균을 낸다
        int frames = dataSize / (2 * channels);   // 시점 개수 = 데이터 바이트 / (2바이트 × 채널 수)
        mono = new float[frames];
        for (int f = 0; f < frames; f++)
        {
            float sum = 0f;
            for (int c = 0; c < channels; c++)
                sum += BitConverter.ToInt16(b, dataOffset + (f * channels + c) * 2) / 32768f;
            mono[f] = sum / channels;
        }
        return true;
    }
}