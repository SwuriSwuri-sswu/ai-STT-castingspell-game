using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using TMPro;

public class MainMenuController : MonoBehaviour
{
    [Header("Managers")]
    public GameSettingManager settingManager; // 설정 제어용 (빈 스크립트 생성 필요)

    [Header("UI Panels")]
    public GameObject settingsPanel;

    [Header("Music Settings")]
    private bool isMusicOn = true;

    [Header("System Settings")]
    public Slider bgmSlider;
    public Slider sfxSlider;
    public Toggle fullscreenToggle;
    public TMP_Dropdown resolutionDropdown;

    private void Start()
    {
        // 초기 UI 상태 설정
        if (settingsPanel != null) settingsPanel.SetActive(false);
        if (fullscreenToggle != null) fullscreenToggle.isOn = Screen.fullScreen;
    }

    // =====================
    // --- 게임 씬 이동 ---
    // =====================
    public void OnClickGameStart()
    {
        // 빌드 세팅에 추가된 게임 씬 이름으로 이동
        SceneManager.LoadScene("GameScene");
    }

    // =====================
    // --- 설정 및 시스템 ---
    // =====================
    public void OnClickSettings()
    {
        settingsPanel.SetActive(!settingsPanel.activeSelf);
    }

    public void OnClickCloseSettings()
    {
        settingsPanel.SetActive(false);
    }

    public void OnClickMusicToggle()
    {
        if (settingManager != null) settingManager.SetMusicToggle(isMusicOn);
    }

    public void OnFullscreenToggle(bool isFullscreen)
    {
        if (settingManager != null) settingManager.SetFullscreen(isFullscreen);
    }

    public void OnResolutionChange(int index)
    {
        if (settingManager != null) settingManager.SetResolution(index);
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

    public void OnBGMVolumeChange(float v) => Debug.Log($"BGM: {v}");
    public void OnSFXVolumeChange(float v) => Debug.Log($"SFX: {v}");
}