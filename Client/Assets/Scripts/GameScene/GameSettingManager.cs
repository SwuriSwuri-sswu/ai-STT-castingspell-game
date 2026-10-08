using UnityEngine;
using UnityEngine.Audio;

public class GameSettingManager : MonoBehaviour
{
    [Header("Audio Settings")]
    public AudioMixer audioMixer;

    // ========================================================
    // MainMenuController에서 호출할 수 있도록 public으로 열어둔 함수들
    // ========================================================

    // 1. 음악 전체 On/Off 기능 (SetMusicToggle)
    public void SetMusicToggle(bool isMusicOn)
    {
        if (audioMixer != null)
        {
            // 켜지면 원래 볼륨(0dB), 꺼지면 음소거(-80dB)로 오디오 믹서 조절
            float volume = isMusicOn ? 0f : -80f;
            audioMixer.SetFloat("Master", volume);
        }
        Debug.Log($"음악 상태 변경: {isMusicOn}");
    }

    // 2. 전체화면 설정 기능 (SetFullscreen)
    public void SetFullscreen(bool isFullscreen)
    {
        Screen.fullScreen = isFullscreen;
        Debug.Log($"전체화면 상태 변경: {isFullscreen}");
    }

    // 3. 해상도 변경 기능 (SetResolution)
    public void SetResolution(int index)
    {
        switch (index)
        {
            case 0: Screen.SetResolution(1920, 1080, Screen.fullScreen); break;
            case 1: Screen.SetResolution(1600, 900, Screen.fullScreen); break;
            case 2: Screen.SetResolution(1280, 720, Screen.fullScreen); break;
        }
        Debug.Log($"해상도 변경 완료 (Index: {index})");
    }

    // ========================================================
    // 슬라이더를 통한 세부 볼륨 조절 (기존 기능 유지)
    // ========================================================

    public void SetBGMVolume(float volume)
    {
        if (audioMixer == null) return;
        // 볼륨 값(0.0001 ~ 1)을 오디오 믹서용 데시벨(dB)로 변환
        float db = Mathf.Log10(Mathf.Clamp(volume, 0.0001f, 1f)) * 20;
        audioMixer.SetFloat("BGM", db);
    }

    public void SetSFXVolume(float volume)
    {
        if (audioMixer == null) return;
        float db = Mathf.Log10(Mathf.Clamp(volume, 0.0001f, 1f)) * 20;
        audioMixer.SetFloat("SFX", db);
    }
}