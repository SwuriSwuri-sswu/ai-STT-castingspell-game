using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Serialization;
using UnityEngine.UI;
using TMPro;

public class MainMenuController : MonoBehaviour
{
    [Header("Managers")]
    [Tooltip("비워 두면 GameSettingManager.Instance 사용 (씬 이동 후에도 안전)")]
    public GameSettingManager settingManager;

    [Header("UI Panels")]
    public GameObject settingsPanel;
    [FormerlySerializedAs("spellPanel")]
    [Tooltip("도감 패널 (스펠 / 몬스터 탭이 들어 있는 BookPanel)")]
    public GameObject bookPanel;
    public GameObject wandPanel;  // 지팡이 도감 패널

    [Header("음악 On/Off 버튼")]
    [Tooltip("MusicToggle 버튼. 넣어 두면 클릭이 자동 연결됨 (OnClick 칸은 비워 두기)")]
    public Button musicToggleButton;
    [Tooltip("꺼졌을 때 어둡게 할 이미지 (비우면 버튼의 이미지)")]
    public Graphic musicToggleGraphic;
    public Color musicOnColor = Color.white;
    [Tooltip("BGM이 꺼졌을 때 덮을 짙은 회색")]
    public Color musicOffColor = new Color(0.35f, 0.35f, 0.35f, 1f);
    [Tooltip("(선택) 꺼졌을 때만 보일 표시 (예: 빗금 아이콘, 'OFF' 글자)")]
    public GameObject musicOffMark;

    // 씬 이동 뒤에도 살아 있는 매니저를 사용 (씬에 놓인 복제본은 Awake에서 지워질 수 있음)
    private GameSettingManager Settings => GameSettingManager.Instance != null ? GameSettingManager.Instance : settingManager;

    private void Start()
    {
        // 초기 UI 상태 설정 (모든 패널 닫기)
        if (settingsPanel != null) settingsPanel.SetActive(false);
        if (bookPanel != null) bookPanel.SetActive(false);
        if (wandPanel != null) wandPanel.SetActive(false);

        if (musicToggleButton != null)
        {
            if (musicToggleGraphic == null) musicToggleGraphic = musicToggleButton.targetGraphic;
            musicToggleButton.onClick.AddListener(OnClickMusicToggle);
        }

        if (Settings != null) Settings.OnBgmToggled += UpdateMusicVisual;
        UpdateMusicVisual(Settings == null || Settings.IsBgmOn);
    }

    private void OnDestroy()
    {
        if (Settings != null) Settings.OnBgmToggled -= UpdateMusicVisual;
    }

    // =====================
    // --- 패널 열기 버튼용 ---
    // =====================
    public void OnClickSettings()
    {
        if (settingsPanel != null) settingsPanel.SetActive(true);
    }

    public void OnClickBook()
    {
        if (bookPanel != null) bookPanel.SetActive(true);
    }

    // 예전 버튼 연결 호환용 (스펠 버튼 OnClick에 연결돼 있던 함수)
    public void OnClickSpell() => OnClickBook();

    public void OnClickWand()
    {
        if (wandPanel != null) wandPanel.SetActive(true);
    }

    // =====================
    // --- 음악 On/Off ---
    // =====================
    public void OnClickMusicToggle()
    {
        if (Settings != null) Settings.ToggleBGM(); // 화면 표시는 OnBgmToggled 이벤트로 갱신
        else Debug.LogWarning("[MainMenuController] GameSettingManager가 없어 BGM을 바꿀 수 없습니다.");
    }

    private void UpdateMusicVisual(bool on)
    {
        if (musicToggleGraphic != null) musicToggleGraphic.color = on ? musicOnColor : musicOffColor;
        if (musicOffMark != null) musicOffMark.SetActive(!on);
    }

    // =====================
    // --- 공통 패널 닫기 함수 ---
    // =====================
    public void OnClickClosePanel(GameObject targetPanel)
    {
        if (targetPanel != null)
        {
            targetPanel.SetActive(false);
        }
    }

    public void OnClickGameQuit()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    // =====================
    // --- UI 연출용 함수 ---
    // =====================
    private IEnumerator FadeImage(Image img, float startAlpha, float targetAlpha, float duration)
    {
        float time = 0f;
        Color color = img.color;
        while (time < duration)
        {
            time += Time.deltaTime;
            color.a = Mathf.Lerp(startAlpha, targetAlpha, time / duration);
            img.color = color;
            yield return null;
        }
        color.a = targetAlpha;
        img.color = color;
    }

    // 설정창 슬라이더 연결용
    public void OnBGMVolumeChange(float v)
    {
        if (Settings != null) Settings.SetBGMVolume(v);
    }

    public void OnSFXVolumeChange(float v)
    {
        if (Settings != null) Settings.SetSFXVolume(v);
    }
}