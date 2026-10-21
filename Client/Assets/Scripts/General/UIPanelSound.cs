using UnityEngine;

// 패널이 켜질 때 / 꺼질 때 소리 (BookPanel에 붙이기)
//   비워 두면 SoundLibrary의 Book Open / Book Close 소리
//   씬이 시작되거나 바뀔 때는 소리를 내지 않음
public class UIPanelSound : MonoBehaviour
{
    [Tooltip("켜질 때 소리 (비우면 SoundLibrary의 Book Open)")]
    public AudioClip openSound;
    [Tooltip("꺼질 때 소리 (비우면 SoundLibrary의 Book Close → 그것도 없으면 Book Open)")]
    public AudioClip closeSound;
    public bool playOnOpen = true;
    public bool playOnClose = true;

    private static bool quitting;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatic()
    {
        quitting = false;
        Application.quitting += () => quitting = true;
    }

    private void OnEnable()
    {
        if (!playOnOpen || Time.timeSinceLevelLoad < 0.3f) return;   // 씬 시작할 때 켜져 있던 경우는 조용히
        SoundLibrary lib = SoundManager.Lib;
        SoundManager.PlayUI(openSound != null ? openSound : lib != null ? lib.bookOpen : null);
    }

    private void OnDisable()
    {
        if (!playOnClose || quitting || !gameObject.scene.isLoaded) return;   // 씬이 바뀌며 꺼지는 경우는 조용히
        SoundLibrary lib = SoundManager.Lib;
        AudioClip clip = closeSound;
        if (clip == null && lib != null) clip = lib.bookClose != null ? lib.bookClose : lib.bookOpen;
        SoundManager.PlayUI(clip);
    }
}