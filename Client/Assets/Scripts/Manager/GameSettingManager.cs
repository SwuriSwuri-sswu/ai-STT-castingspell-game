using System.Collections;
using UnityEngine;
using UnityEngine.Audio;

// 오디오 및 화면 설정을 관리하고 백엔드 서버와 설정 데이터를 동기화하는 매니저 클래스
public class GameSettingManager : MonoBehaviour
{
    // 전역에서 접근 가능한 싱글톤 인스턴스
    public static GameSettingManager Instance { get; private set; }

    [Header("Audio Settings")]
    [Tooltip("볼륨 조절용 오디오 믹서")]
    public AudioMixer audioMixer;

    [Header("Current Settings Cache")]
    [Tooltip("마스터 볼륨 (0.0 ~ 1.0)")]
    public float masterVolume = 1.0f;

    [Tooltip("배경음악(BGM) 볼륨 (0.0 ~ 1.0)")]
    public float bgmVolume = 1.0f;

    [Tooltip("효과음(SFX) 볼륨 (0.0 ~ 1.0)")]
    public float sfxVolume = 1.0f;

    [Tooltip("음소거 여부")]
    public bool isMuted = false;

    [Tooltip("창모드 여부 (true: 창모드 / false: 전체화면)")]
    public bool isWindowed = false;

    [Tooltip("해상도 설정값 (예: '1920x1080')")]
    public string resolutionOp = "1920x1080";

    [Header("Microphone Settings")]
    [Tooltip("현재 선택된 마이크 이름")]
    public string selectedMicName = "";

    // 연결된 전체 마이크 목록을 담을 배열
    private string[] availableMics;


    // 싱글톤 생성 및 씬 전환 시 파괴 방지
    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }
        else
        {
            Destroy(gameObject);
        }
    }

    // 시작 시 서버에서 설정값 로드
    private void Start()
    {
        availableMics = Microphone.devices;

        if (availableMics.Length > 0)
        {
            // 기기에 저장된 마이크 이름이 있는지 확인 후 불러오기
            string savedMic = PlayerPrefs.GetString("SelectedMic", availableMics[0]);

            // 저장된 마이크가 현재 연결된 기기 목록에 있는지 확인
            if (System.Array.Exists(availableMics, mic => mic == savedMic))
            {
                selectedMicName = savedMic;
            }
            else
            {
                selectedMicName = availableMics[0]; // 없으면 기본 마이크
            }
        }

        LoadSettingsFromServer();
    }

    // 1. 서버 통신 (GET / PATCH /v1/settings)
    // 서버에서 설정을 조회(GET)하여 유니티 환경에 적용
    public void LoadSettingsFromServer()
    {
        if (NetworkManager.Instance == null) return;

        StartCoroutine(NetworkManager.Instance.GetSettings((settingsData) =>
        {
            if (settingsData != null)
            {
                // 서버 수신 데이터를 캐시에 할당
                masterVolume = settingsData.master_volume;
                bgmVolume = settingsData.bgm_volume;
                sfxVolume = settingsData.sfx_volume;
                isMuted = settingsData.is_muted;
                isWindowed = settingsData.is_windowed;
                resolutionOp = settingsData.resolution_op;

                // 유니티 로컬 환경에 적용
                ApplyAllSettingsToUnity();
            }
        }));
    }

    // 변경된 설정을 서버로 전송하여 저장(PATCH)
    public void SaveSettingsToServer()
    {
        if (NetworkManager.Instance == null) return;

        // 전송용 DTO 생성
        SettingsUpdateRequest updateRequest = new SettingsUpdateRequest
        {
            master_volume = masterVolume,
            bgm_volume = bgmVolume,
            sfx_volume = sfxVolume,
            is_muted = isMuted,
            is_windowed = isWindowed,
            resolution_op = resolutionOp
        };

        StartCoroutine(NetworkManager.Instance.UpdateSettings(updateRequest, (isSuccess) =>
        {
            if (isSuccess)
            {
                Debug.Log("서버에 설정 저장 완료!");
            }
        }));
    }

    // 2. 유니티 실제 로컬 환경 적용
    // 현재 캐시된 모든 설정값을 유니티 실제 시스템(오디오/화면)에 반영
    private void ApplyAllSettingsToUnity()
    {
        // (1) 오디오 설정 적용
        SetMasterVolume(masterVolume);
        SetBGMVolume(bgmVolume);
        SetSFXVolume(sfxVolume);
        SetMuteToggle(isMuted);

        // (2) 창모드/전체화면 적용 (isWindowed == true -> fullScreen = false)
        Screen.fullScreen = !isWindowed;

        // (3) 해상도 적용
        ApplyResolutionString(resolutionOp);
    }

    // 3. UI 이벤트용 공개 함수
    // 음소거 토글 처리 (UI 토글 연동)
    // <param name="isMute">음소거 여부</param>
    public void SetMuteToggle(bool isMute)
    {
        isMuted = isMute;
        if (audioMixer != null)
        {
            float db = isMuted ? -80f : ConvertToDecibel(masterVolume);
            audioMixer.SetFloat("Master", db);
        }
        Debug.Log($"음소거 상태 변경: {isMuted}");
    }

    // 창모드 / 전체화면 전환 (UI 토글 연동)
    // <param name="isWindowedMode">창모드 여부</param>
    public void SetWindowedMode(bool isWindowedMode)
    {
        isWindowed = isWindowedMode;
        Screen.fullScreen = !isWindowed;
        Debug.Log($"창모드 여부 변경: {isWindowed} (전체화면: {!isWindowed})");
    }

    // 드롭다운 인덱스 기반 해상도 변경 (UI Dropdown 연동)
    // <param name="index">0: 1080p, 1: 900p, 2: 720p</param>
    public void SetResolutionByIndex(int index)
    {
        switch (index)
        {
            case 0: resolutionOp = "1920x1080"; break;
            case 1: resolutionOp = "1600x900"; break;
            case 2: resolutionOp = "1280x720"; break;
            default: resolutionOp = "1920x1080"; break;
        }

        ApplyResolutionString(resolutionOp);
        Debug.Log($"해상도 변경 완료: {resolutionOp}");
    }

    // 볼륨 조절 전체: <param name="volume">0.0 ~ 1.0 값</param>
    // 마스터 볼륨 조절 (UI 슬라이더 연동)
    public void SetMasterVolume(float volume)
    {
        masterVolume = volume;
        if (audioMixer != null && !isMuted)
        {
            audioMixer.SetFloat("Master", ConvertToDecibel(masterVolume));
        }
    }

    /// BGM 볼륨 조절 (UI 슬라이더 연동)
    public void SetBGMVolume(float volume)
    {
        bgmVolume = volume;
        if (audioMixer != null)
        {
            audioMixer.SetFloat("BGM", ConvertToDecibel(bgmVolume));
        }
    }

    // SFX 볼륨 조절 (UI 슬라이더 연동)
    public void SetSFXVolume(float volume)
    {
        sfxVolume = volume;
        if (audioMixer != null)
        {
            audioMixer.SetFloat("SFX", ConvertToDecibel(sfxVolume));
        }
    }

    // 마이크 장치 변경 (UI Dropdown 연동)
    public void SetMicrophoneByIndex(int index)
    {
        if (availableMics == null || availableMics.Length == 0) return;

        if (index >= 0 && index < availableMics.Length)
        {
            selectedMicName = availableMics[index];

            // 변경된 마이크 이름을 기기 로컬에 저장 (서버 전송 X)
            PlayerPrefs.SetString("SelectedMic", selectedMicName);
            PlayerPrefs.Save();

            Debug.Log($"마이크 변경 및 로컬 저장 완료: {selectedMicName}");
        }
    }


    // 4. 내부 헬퍼 함수

    // 0.0~1.0 비율 값을 AudioMixer용 데시벨(dB)로 변환 (-80dB ~ 0dB)
    private float ConvertToDecibel(float linear)
    {
        return Mathf.Log10(Mathf.Clamp(linear, 0.0001f, 1f)) * 20;
    }

    // '1920x1080' 형태의 문자열을 파싱하여 Screen.SetResolution 실행
    private void ApplyResolutionString(string resString)
    {
        string[] dimensions = resString.Split('x');
        if (dimensions.Length == 2 && int.TryParse(dimensions[0], out int width) && int.TryParse(dimensions[1], out int height))
        {
            Screen.SetResolution(width, height, !isWindowed);
        }
    }
}