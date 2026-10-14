using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using TMPro;

public class MainMenuController : MonoBehaviour
{
    [Header("Managers")]
    public GameSettingManager settingManager;

    [Header("UI Panels")]
    public GameObject settingsPanel;
    public GameObject spellPanel; // 추가: 스펠 패널
    public GameObject wandPanel;  // 추가: 지팡이 도감 패널

    [Header("Music Settings")]
    private bool isMusicOn = true;

    private void Start()
    {
        // 초기 UI 상태 설정 (모든 패널 닫기)
        if (settingsPanel != null) settingsPanel.SetActive(false);
        if (spellPanel != null) spellPanel.SetActive(false);
        if (wandPanel != null) wandPanel.SetActive(false);
    }

    // =====================
    // --- 패널 열기 버튼용 ---
    // =====================
    public void OnClickSettings()
    {
        if (settingsPanel != null) settingsPanel.SetActive(true);
    }

    public void OnClickSpell()
    {
        if (spellPanel != null) spellPanel.SetActive(true);
    }

    public void OnClickWand()
    {
        if (wandPanel != null) wandPanel.SetActive(true);
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

    public void OnBGMVolumeChange(float v) => Debug.Log($"BGM: {v}");
    public void OnSFXVolumeChange(float v) => Debug.Log($"SFX: {v}");
}