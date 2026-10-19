using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Audio;
using UnityEngine.SceneManagement;

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
    // 고른 마이크 이름을 이 컴퓨터에 저장하는 키. 빈 문자열 = OS 기본 입력 장치 (Default)
    private const string MicKey = "SelectedMic";

    // 설정 패널 해상도 드롭다운의 항목 (순서 = 드롭다운 번호). ResolutionDropdown 이 이 목록으로 항목을 채운다
    public static readonly string[] ResolutionOptions = { "1920x1080", "1600x900", "1280x720" };
    // 지금 해상도의 드롭다운 번호 (목록에 없으면 0)
    public int ResolutionIndex => Mathf.Max(0, Array.IndexOf(ResolutionOptions, resolutionOp));

    [Serializable]
    public class SceneBgm
    {
        public string sceneName;
        public AudioClip clip;
    }

    [Header("배경 음악 (BGM)")]
    [Tooltip("씬별 BGM이 따로 없을 때 틀 음악")]
    public AudioClip defaultBgm;
    [Tooltip("씬마다 다른 음악 (예: TitleScene / GameScene / EndingScene)")]
    public List<SceneBgm> sceneBgms = new List<SceneBgm>();
    [Tooltip("BGM 기본 크기 (설정의 BGM 볼륨에 곱해짐)")]
    [Range(0f, 1f)] public float bgmBaseVolume = 0.6f;
    [Tooltip("BGM을 켜고 끌 때 서서히 바뀌는 시간 (초)")]
    public float bgmFadeTime = 0.5f;

    // BGM 켜짐 여부 (이 컴퓨터에 저장, 씬이 바뀌어도 유지)
    public bool IsBgmOn { get; private set; } = true;
    public event Action<bool> OnBgmToggled;

    private const string BgmOnKey = "BGMOn";
    private AudioSource bgmSource;
    private Coroutine bgmFade;


    // 싱글톤 생성 및 씬 전환 시 파괴 방지
    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
            // 다른 오브젝트의 Start(VoiceRecorder 등)보다 먼저 마이크 설정을 읽어 둔다
            LoadMicrophoneSetting();
            SetupBgm();
        }
        else
        {
            Destroy(gameObject);
        }
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            SceneManager.sceneLoaded -= HandleSceneLoaded;
            if (NetworkManager.Instance != null) NetworkManager.Instance.OnSessionReady -= HandleSessionReady;
        }
    }

    // 시작 시 서버에서 설정값 로드
    private void Start()
    {
        // 첫 씬의 VoiceInput 에도 고른 마이크를 적용 (씬 전환 때는 HandleSceneLoaded 에서)
        ApplyMicrophoneToScene();

        // 세션(session-id)이 만들어진 뒤에 서버 설정을 불러온다 (없으면 400 Bad Request)
        LoadSettingsWhenSessionReady();
    }

    private void LoadSettingsWhenSessionReady()
    {
        NetworkManager nm = NetworkManager.Instance;
        if (nm == null) return;

        if (nm.HasSession)
        {
            LoadSettingsFromServer();
        }
        else
        {
            nm.OnSessionReady -= HandleSessionReady;
            nm.OnSessionReady += HandleSessionReady;
            Debug.Log("[GameSettingManager] 세션 생성을 기다렸다가 설정을 불러옵니다.");
        }
    }

    private void HandleSessionReady()
    {
        if (NetworkManager.Instance != null) NetworkManager.Instance.OnSessionReady -= HandleSessionReady;
        LoadSettingsFromServer();
    }

    // 1. 서버 통신 (GET / PATCH /v1/settings)
    // 서버에서 설정을 조회(GET)하여 유니티 환경에 적용
    public void LoadSettingsFromServer()
    {
        if (NetworkManager.Instance == null) return;
        if (!NetworkManager.Instance.HasSession)
        {
            LoadSettingsWhenSessionReady(); // 아직 세션이 없으면 생기면 다시 시도
            return;
        }

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
        if (!NetworkManager.Instance.HasSession)
        {
            Debug.LogWarning("[GameSettingManager] 세션이 아직 없어 설정을 서버에 저장하지 못했습니다.");
            return;
        }

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
        ApplyBgmVolume(true);

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
        ApplyBgmVolume(true);
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

    // 드롭다운 인덱스 기반 해상도 변경 (UI Dropdown 연동: ResolutionDropdown)
    // <param name="index">ResolutionOptions 의 번호 (0: 1080p, 1: 900p, 2: 720p)</param>
    // 고를 때마다 바로 서버에 저장한다 (PATCH /v1/settings). ※ 에디터 Game 창에서는 해상도가 바뀌지 않으므로 빌드에서 확인
    public void SetResolutionByIndex(int index)
    {
        resolutionOp = (index >= 0 && index < ResolutionOptions.Length) ? ResolutionOptions[index] : ResolutionOptions[0];

        ApplyResolutionString(resolutionOp);
        Debug.Log($"해상도 변경 완료: {resolutionOp}");
        SaveSettingsToServer();
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
        ApplyBgmVolume(true);
    }

    /// BGM 볼륨 조절 (UI 슬라이더 연동)
    public void SetBGMVolume(float volume)
    {
        bgmVolume = volume;
        if (audioMixer != null)
        {
            audioMixer.SetFloat("BGM", ConvertToDecibel(bgmVolume));
        }
        ApplyBgmVolume(true);
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

    // 연결된 마이크 목록을 다시 읽는다 (설정 패널을 열 때 MicDropdown 이 호출 → 게임 중에 꽂은 마이크도 보임)
    public string[] RefreshMicrophones()
    {
        availableMics = Microphone.devices;
        return availableMics;
    }

    // 마이크 장치 변경 (UI Dropdown 연동: MicDropdown). 빈 문자열 = OS 기본 입력 장치 (Default)
    // 이 컴퓨터에 저장하고(서버 전송 X), 지금 씬의 VoiceInput 에 바로 적용한다
    public void SetMicrophone(string deviceName)
    {
        selectedMicName = deviceName ?? "";

        PlayerPrefs.SetString(MicKey, selectedMicName);
        PlayerPrefs.Save();

        ApplyMicrophoneToScene();
        Debug.Log($"마이크 변경 및 로컬 저장 완료: {(selectedMicName.Length > 0 ? selectedMicName : "Default (System)")}");
    }

    // 예전 방식 호환: 연결된 마이크 목록(Microphone.devices)의 번호로 고른다
    public void SetMicrophoneByIndex(int index)
    {
        RefreshMicrophones();
        if (index >= 0 && index < availableMics.Length) SetMicrophone(availableMics[index]);
    }

    // 저장된 마이크를 불러온다. 처음 실행이거나 저장한 장치가 지금 연결되어 있지 않으면 OS 기본 장치 (빈 문자열)
    private void LoadMicrophoneSetting()
    {
        RefreshMicrophones();
        string saved = PlayerPrefs.GetString(MicKey, "");
        selectedMicName = (saved.Length > 0 && Array.IndexOf(availableMics, saved) >= 0) ? saved : "";
    }

    // 지금 씬의 VoiceInput 에 고른 마이크를 적용 (VoiceInput 이 다음 프레임에 새 장치로 다시 시작한다)
    private void ApplyMicrophoneToScene()
    {
        foreach (VoiceInput input in FindObjectsByType<VoiceInput>(FindObjectsSortMode.None))
        {
            input.DeviceName = selectedMicName;
        }
    }


    // =========================================================
    // BGM 켜기 / 끄기 (MusicToggle 버튼 등에서 호출)
    // =========================================================
    public void ToggleBGM() => SetBGMOn(!IsBgmOn);

    public void SetBGMOn(bool on)
    {
        IsBgmOn = on;
        PlayerPrefs.SetInt(BgmOnKey, on ? 1 : 0);
        PlayerPrefs.Save();
        ApplyBgmVolume(false);
        OnBgmToggled?.Invoke(on);
        Debug.Log($"BGM {(on ? "켜짐" : "꺼짐")}");
    }

    private void SetupBgm()
    {
        IsBgmOn = PlayerPrefs.GetInt(BgmOnKey, 1) == 1;

        bgmSource = gameObject.AddComponent<AudioSource>();
        bgmSource.loop = true;
        bgmSource.playOnAwake = false;
        bgmSource.spatialBlend = 0f; // 2D 사운드
        if (audioMixer != null)
        {
            AudioMixerGroup[] groups = audioMixer.FindMatchingGroups("BGM");
            if (groups != null && groups.Length > 0) bgmSource.outputAudioMixerGroup = groups[0];
        }

        SceneManager.sceneLoaded += HandleSceneLoaded;
        PlayBgmForScene(SceneManager.GetActiveScene().name);
    }

    private void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        PlayBgmForScene(scene.name);
        ApplyMicrophoneToScene();   // 새 씬의 VoiceInput 에도 고른 마이크 적용 (Inspector 값보다 우선)
    }

    // 씬에 맞는 BGM 재생 (같은 곡이면 끊지 않고 이어서)
    private void PlayBgmForScene(string sceneName)
    {
        if (bgmSource == null) return;
        AudioClip clip = defaultBgm;
        foreach (SceneBgm s in sceneBgms)
        {
            if (s != null && s.clip != null && s.sceneName == sceneName) { clip = s.clip; break; }
        }
        if (clip == null) { bgmSource.Stop(); return; }
        if (bgmSource.clip == clip && bgmSource.isPlaying) return;

        bgmSource.clip = clip;
        bgmSource.Play();
        ApplyBgmVolume(true);
    }

    // 실제 BGM 크기 = 기본 크기 × BGM 볼륨 × (믹서가 없으면 마스터 볼륨) / 꺼짐·음소거면 0
    private void ApplyBgmVolume(bool instant)
    {
        if (bgmSource == null) return;
        float target = 0f;
        if (IsBgmOn && !isMuted)
        {
            target = bgmBaseVolume;
            if (audioMixer == null) target *= Mathf.Clamp01(bgmVolume) * Mathf.Clamp01(masterVolume);
        }

        if (bgmFade != null) StopCoroutine(bgmFade);
        if (instant || bgmFadeTime <= 0f) bgmSource.volume = target;
        else bgmFade = StartCoroutine(FadeBgm(target));
    }

    private IEnumerator FadeBgm(float target)
    {
        float start = bgmSource.volume;
        float t = 0f;
        while (t < bgmFadeTime)
        {
            t += Time.unscaledDeltaTime;
            bgmSource.volume = Mathf.Lerp(start, target, t / bgmFadeTime);
            yield return null;
        }
        bgmSource.volume = target;
        bgmFade = null;
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