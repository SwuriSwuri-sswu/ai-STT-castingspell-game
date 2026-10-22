using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// 소리 재생 담당 (씬에 붙일 필요 없음: 게임이 시작되면 스스로 생겨서 씬이 바뀌어도 유지됨)
//   - 소리 목록: Assets/Resources/SoundLibrary.asset (없으면 아무 소리도 안 남 + 경고 한 번)
//   - 씬마다 BGM·환경음 자동 재생 (Title / Game / Boss). 엔딩·게임 오버·보스 페이즈는 각 스크립트가 바꿈
//   - 영창(Space) 중에는 BGM·환경음을 줄임
//   - 모든 버튼 클릭음 자동
//   - 크기 = SoundLibrary 크기 × 설정 화면 볼륨 슬라이더(VolumeSlider) / BGM 끄기·음소거(GameSettingManager)도 따름
//     슬라이더는 처음 실행할 때 가운데(= SoundLibrary 크기 그대로), 그 뒤로는 마지막에 맞춘 값을 이 컴퓨터에 저장해 둠
//     끝까지 올리면 2배 / 내리면 0
//   ※ GameSettingManager의 Default Bgm / Scene Bgms 칸은 비워 두기 (BGM이 두 번 나옴)
[DefaultExecutionOrder(-90)]
public class SoundManager : MonoBehaviour
{
    public static SoundManager Instance { get; private set; }
    public static SoundLibrary Lib => Instance != null ? Instance.lib : null;

    private const int SfxSources = 16;

    private SoundLibrary lib;
    private AudioSource bgmA, bgmB, amb;
    private AudioSource bgmCur, bgmOld;
    private float curW, oldW, bgmFade = 1f;
    private float ambW, ambTarget, ambFade = 1f;
    private float duck = 1f;
    private readonly List<AudioSource> sfx = new List<AudioSource>();
    private int nextSfx;
    private readonly Dictionary<AudioClip, float> lastPlayed = new Dictionary<AudioClip, float>();
    private AudioSource typeSrc;          // 타이핑 전용 (겹쳐서 지지직거리지 않게 한 줄로만)
    private float lastTypeTime = -10f, typeW;
    private VoiceSender voice;
    private float voiceSearchTimer;
    private GameObject hovered;
    private readonly List<RaycastResult> hits = new List<RaycastResult>();
    private static bool warned;

    // ---------- 시작 ----------
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Boot()
    {
        if (Instance != null) return;
        SoundLibrary l = Resources.Load<SoundLibrary>("SoundLibrary");
        if (l == null)
        {
            if (!warned) Debug.LogWarning("[Sound] Assets/Resources/SoundLibrary.asset이 없어 소리가 나지 않습니다. 메뉴 Tools → Sound → 사운드 라이브러리 자동 채우기를 눌러 주세요.");
            warned = true;
            return;
        }
        GameObject go = new GameObject("[SoundManager]");
        DontDestroyOnLoad(go);
        SoundManager m = go.AddComponent<SoundManager>();
        m.Init(l);
    }

    private void Init(SoundLibrary l)
    {
        Instance = this;
        lib = l;
        bgmA = NewSource("BGM A", true);
        bgmB = NewSource("BGM B", true);
        amb = NewSource("Ambience", true);
        typeSrc = NewSource("Typing", false);
        for (int i = 0; i < SfxSources; i++) sfx.Add(NewSource("SFX " + i, false));
        bgmCur = bgmA; bgmOld = bgmB;
        SceneManager.sceneLoaded += HandleSceneLoaded;
        ApplyScene(SceneManager.GetActiveScene().name);
    }

    private AudioSource NewSource(string name, bool loop)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(transform, false);
        AudioSource s = go.AddComponent<AudioSource>();
        s.playOnAwake = false;
        s.loop = loop;
        s.spatialBlend = 0f;   // 2D
        s.ignoreListenerPause = true;
        return s;
    }

    private void OnDestroy()
    {
        if (Instance != this) return;
        SceneManager.sceneLoaded -= HandleSceneLoaded;
        Instance = null;
    }

    private void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (mode == LoadSceneMode.Additive) return;
        voice = null;
        hovered = null;
        ApplyScene(scene.name);
    }

    // 씬별 BGM·환경음
    private void ApplyScene(string scene)
    {
        if (lib == null) return;
        if (scene == lib.titleScene) { SetBgm(lib.bgmTitle, true, -1f); SetAmb(null); }
        else if (scene == lib.gameScene) { SetBgm(lib.bgmStage1, true, -1f); SetAmb(lib.ambTorch); }
        else if (scene == lib.bossScene) { SetBgm(lib.bgmBossEntrance, true, -1f); SetAmb(lib.ambBossCastle); }
        else if (scene == lib.endingScene) { SetBgm(null, true, 1f); SetAmb(null); }   // 엔딩은 EndingSceneController가 틀어 줌
    }

    // ---------- 매 프레임: 크기 조절 ----------
    private void Update()
    {
        if (lib == null) return;
        float dt = Time.unscaledDeltaTime;

        // 영창 중 BGM 줄이기
        if (voice == null)
        {
            voiceSearchTimer -= dt;
            if (voiceSearchTimer <= 0f) { voice = FindFirstObjectByType<VoiceSender>(); voiceSearchTimer = 1f; }
        }
        float duckTarget = voice != null && voice.IsRecording ? lib.duckWhileChanting : 1f;
        duck = Mathf.MoveTowards(duck, duckTarget, dt / 0.25f);

        curW = Mathf.MoveTowards(curW, bgmCur.clip != null ? 1f : 0f, dt / Mathf.Max(0.01f, bgmFade));
        oldW = Mathf.MoveTowards(oldW, 0f, dt / Mathf.Max(0.01f, bgmFade));
        if (oldW <= 0f && bgmOld.isPlaying) bgmOld.Stop();

        float bgm = lib.bgmVolume * BgmSetting() * duck;
        bgmCur.volume = curW * bgm * lib.VolumeOf(bgmCur.clip);
        bgmOld.volume = oldW * bgm * lib.VolumeOf(bgmOld.clip);

        ambW = Mathf.MoveTowards(ambW, ambTarget, dt / Mathf.Max(0.01f, ambFade));
        amb.volume = ambW * lib.ambienceVolume * SfxSetting() * duck * lib.VolumeOf(amb.clip);
        if (ambW <= 0f && ambTarget <= 0f && amb.isPlaying) amb.Stop();

        UpdateTyping(dt);
        UpdateUiSounds();
    }

    // 긴 타이핑 소리(반복 재생 중): 글자가 멈추면 빠르게 줄여서 멈춤
    private void UpdateTyping(float dt)
    {
        if (typeSrc == null || !typeSrc.isPlaying || !typeSrc.loop) return;
        bool typing = Time.realtimeSinceStartup - lastTypeTime <= lib.typingStopGap;
        typeW = Mathf.MoveTowards(typeW, typing ? 1f : 0f, dt / 0.06f);
        typeSrc.volume = typeW * lib.typingVolume * lib.VolumeOf(typeSrc.clip) * SfxSetting();
        if (typeW <= 0f) typeSrc.Stop();
    }

    // =========================================================
    // 설정 화면 볼륨 슬라이더 (VolumeSlider가 사용)
    //   0 ~ 1, 가운데(0.5) = SoundLibrary에 맞춘 크기 그대로
    //   처음 실행하면 가운데, 바꾸면 이 컴퓨터에 저장 (PlayerPrefs) → 다음에 켜도 그 값
    // =========================================================
    public enum VolumeKind { Master, Bgm, Sfx }
    private const float DefaultVolume = 0.5f;
    private static float masterSlider = DefaultVolume, bgmSlider = DefaultVolume, sfxSlider = DefaultVolume;
    private static bool volumesLoaded;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetSliders() => volumesLoaded = false;

    private static string KeyOf(VolumeKind kind) => "Volume_" + kind;

    private static void LoadVolumes()
    {
        if (volumesLoaded) return;
        volumesLoaded = true;
        masterSlider = PlayerPrefs.GetFloat(KeyOf(VolumeKind.Master), DefaultVolume);
        bgmSlider = PlayerPrefs.GetFloat(KeyOf(VolumeKind.Bgm), DefaultVolume);
        sfxSlider = PlayerPrefs.GetFloat(KeyOf(VolumeKind.Sfx), DefaultVolume);
    }

    /// <summary>저장된 볼륨을 지우고 처음 상태(가운데)로</summary>
    public static void ResetVolumes()
    {
        foreach (VolumeKind k in System.Enum.GetValues(typeof(VolumeKind))) PlayerPrefs.DeleteKey(KeyOf(k));
        PlayerPrefs.Save();
        volumesLoaded = false;
    }

    /// <summary>바꾼 볼륨을 디스크에 기록 (설정창을 닫을 때 VolumeSlider가 부름)</summary>
    public static void SaveVolumes() => PlayerPrefs.Save();

    public static float GetVolume(VolumeKind kind)
    {
        LoadVolumes();
        switch (kind)
        {
            case VolumeKind.Bgm: return bgmSlider;
            case VolumeKind.Sfx: return sfxSlider;
            default: return masterSlider;
        }
    }

    public static void SetVolume(VolumeKind kind, float value)
    {
        LoadVolumes();
        value = Mathf.Clamp01(value);
        PlayerPrefs.SetFloat(KeyOf(kind), value);
        switch (kind)
        {
            case VolumeKind.Bgm: bgmSlider = value; break;
            case VolumeKind.Sfx: sfxSlider = value; break;
            default: masterSlider = value; break;
        }
    }

    private static float Gain(float slider) => slider * 2f;   // 가운데 = 1배, 끝 = 2배

    // 음소거·BGM 끄기는 GameSettingManager (없으면 무시)
    private static bool Muted => GameSettingManager.Instance != null && GameSettingManager.Instance.isMuted;

    private static float SfxSetting()
    {
        LoadVolumes();
        if (Muted) return 0f;
        return Gain(masterSlider) * Gain(sfxSlider);
    }

    private static float BgmSetting()
    {
        LoadVolumes();
        GameSettingManager g = GameSettingManager.Instance;
        if (Muted || (g != null && !g.IsBgmOn)) return 0f;
        return Gain(masterSlider) * Gain(bgmSlider);
    }

    // ---------- 버튼 클릭·올리기 ----------
    private void UpdateUiSounds()
    {
        EventSystem es = EventSystem.current;
        if (es == null || Cursor.lockState == CursorLockMode.Locked) { hovered = null; return; }
        bool click = lib.uiClick != null && Input.GetMouseButtonDown(0);
        bool hover = lib.uiHover != null;
        if (!click && !hover) return;

        Selectable sel = SelectableUnderMouse(es);
        GameObject go = sel != null ? sel.gameObject : null;
        if (click && sel != null) PlayUI(lib.uiClick);
        if (hover && go != hovered && sel != null && !click) PlayUI(lib.uiHover);
        hovered = go;
    }

    private Selectable SelectableUnderMouse(EventSystem es)
    {
        PointerEventData ped = new PointerEventData(es) { position = Input.mousePosition };
        hits.Clear();
        es.RaycastAll(ped, hits);
        if (hits.Count == 0) return null;
        Selectable s = hits[0].gameObject.GetComponentInParent<Selectable>();
        return s != null && s.IsInteractable() && !(s is Scrollbar) ? s : null;
    }

    // =========================================================
    // 효과음
    // =========================================================
    /// <summary>효과음 재생. 같은 소리를 minInterval초 안에 또 부르면 무시 (여러 마리가 동시에 맞을 때 등)</summary>
    public static void Play(AudioClip clip, float volume = 1f, float pitch = 1f, bool vary = true, float minInterval = 0.06f)
    {
        if (Lib != null) PlayRaw(clip, volume * Lib.sfxVolume, pitch, vary, minInterval);
    }

    // volume = 최종 크기 (설정 화면 볼륨만 더 곱함)
    private static void PlayRaw(AudioClip clip, float volume, float pitch, bool vary, float minInterval)
    {
        SoundManager m = Instance;
        if (m == null || clip == null || m.lib == null) return;
        float now = Time.realtimeSinceStartup;
        if (m.lastPlayed.TryGetValue(clip, out float last) && now - last < minInterval) return;
        m.lastPlayed[clip] = now;

        AudioSource s = m.FreeSource();
        float v = m.lib.pitchVariation;
        s.clip = clip;
        s.pitch = pitch * (vary && v > 0f ? UnityEngine.Random.Range(1f - v, 1f + v) : 1f);
        s.volume = Mathf.Clamp01(volume * m.lib.VolumeOf(clip) * SfxSetting());
        s.Play();
    }

    /// <summary>SoundLibrary에서 골라 재생: SoundManager.Play(L => L.playerHit)</summary>
    public static void Play(Func<SoundLibrary, AudioClip> pick, float volume = 1f, float pitch = 1f, bool vary = true, float minInterval = 0.06f)
    {
        if (Lib != null) Play(pick(Lib), volume, pitch, vary, minInterval);
    }

    /// <summary>목록 중 하나를 무작위로</summary>
    public static void PlayAny(Func<SoundLibrary, AudioClip[]> pick, float volume = 1f, float pitch = 1f)
    {
        if (Lib != null) PlayAny(pick(Lib), volume, pitch);
    }

    public static void PlayAny(AudioClip[] clips, float volume = 1f, float pitch = 1f)
    {
        Play(PickRandom(clips), volume, pitch);
    }

    /// <summary>delay초(실제 시간) 뒤에 재생</summary>
    public static void PlayDelayed(AudioClip clip, float delay, float volume = 1f, float pitch = 1f)
    {
        if (Instance == null || clip == null) return;
        if (delay <= 0f) { Play(clip, volume, pitch); return; }
        Instance.StartCoroutine(Instance.DelayRoutine(clip, delay, volume, pitch));
    }

    private IEnumerator DelayRoutine(AudioClip clip, float delay, float volume, float pitch)
    {
        yield return new WaitForSecondsRealtime(delay);
        Play(clip, volume, pitch);
    }

    /// <summary>UI 소리 (높낮이 변화 없음)</summary>
    public static void PlayUI(AudioClip clip)
    {
        if (Lib != null) PlayRaw(clip, Lib.uiVolume, 1f, false, 0.03f);
    }

    /// <summary>타이핑 한 글자 (너무 촘촘하면 알아서 건너뜀)</summary>
    //   짧은 소리(딸깍 한 번)면 글자마다 새로 재생 (앞 소리는 끊고) → 겹치지 않음
    //   긴 소리(타자 치는 소리 녹음)면 글자가 나오는 동안 이어서 반복 → 줄바꿈·문단 끝에서 멈춤
    public static void Typing()
    {
        SoundManager m = Instance;
        if (m == null || m.lib == null || m.lib.typing == null || m.typeSrc == null) return;
        AudioClip clip = m.lib.typing;
        float now = Time.realtimeSinceStartup;
        AudioSource s = m.typeSrc;

        if (clip.length > 0.35f)
        {
            m.lastTypeTime = now;
            if (s.isPlaying && s.clip == clip && s.loop) return;   // 이미 이어서 재생 중
            s.Stop();
            s.clip = clip;
            s.loop = true;
            s.pitch = 1f;
            s.time = UnityEngine.Random.Range(0f, clip.length * 0.7f);   // 매번 다른 부분부터
            m.typeW = 0f;
            s.volume = 0f;
            s.Play();
            return;
        }

        if (now - m.lastTypeTime < 0.045f) return;   // 너무 촘촘하면 건너뜀
        m.lastTypeTime = now;
        float v = m.lib.pitchVariation;
        s.Stop();
        s.clip = clip;
        s.loop = false;
        s.pitch = v > 0f ? UnityEngine.Random.Range(1f - v, 1f + v) : 1f;
        s.volume = Mathf.Clamp01(m.lib.typingVolume * m.lib.VolumeOf(clip) * SfxSetting());
        s.Play();
    }

    // ---------- 마법 ----------
    /// <summary>시전 순간 (SpellData의 Cast Sound → 없으면 속성 기본)</summary>
    public static void PlayCast(SpellData spell)
    {
        if (Lib == null || spell == null) return;
        Play(spell.castSound != null ? spell.castSound : PickRandom(Lib.CastFor(spell.elementType)));
    }

    /// <summary>데미지가 들어가는 순간 (SpellData의 Hit Sound → 없으면 속성 기본)</summary>
    public static void PlayHit(SpellData spell)
    {
        if (Lib == null || spell == null) return;
        Play(spell.hitSound != null ? spell.hitSound : PickRandom(Lib.HitFor(spell.elementType)));
    }

    /// <summary>상성 적중음 (효과가 굉장했다 / 별로였다). 폭발음보다 살짝 늦게</summary>
    public static void PlayEffectiveness(float multiplier)
    {
        SoundLibrary l = Lib;
        if (l == null) return;
        if (multiplier > 1f) PlayDelayed(l.superEffective, l.effectivenessDelay);
        else if (multiplier < 1f) PlayDelayed(l.notEffective, l.effectivenessDelay);
    }

    private static AudioClip PickRandom(AudioClip[] clips)
    {
        if (clips == null || clips.Length == 0) return null;
        int start = UnityEngine.Random.Range(0, clips.Length);
        for (int i = 0; i < clips.Length; i++)
        {
            AudioClip c = clips[(start + i) % clips.Length];
            if (c != null) return c;
        }
        return null;
    }

    private AudioSource FreeSource()
    {
        for (int i = 0; i < sfx.Count; i++)
        {
            AudioSource s = sfx[(nextSfx + i) % sfx.Count];
            if (!s.isPlaying) { nextSfx = (nextSfx + i + 1) % sfx.Count; return s; }
        }
        AudioSource oldest = sfx[nextSfx];   // 전부 재생 중이면 가장 오래된 것부터 덮어씀
        nextSfx = (nextSfx + 1) % sfx.Count;
        return oldest;
    }

    // =========================================================
    // BGM · 환경음
    // =========================================================
    /// <summary>BGM 바꾸기 (같은 곡이 나오는 중이면 그대로). fade &lt; 0 이면 SoundLibrary의 BGM Fade Time</summary>
    public static void PlayBgm(Func<SoundLibrary, AudioClip> pick, float fade = -1f, bool loop = true)
    {
        if (Instance != null && Instance.lib != null) Instance.SetBgm(pick(Instance.lib), loop, fade);
    }

    /// <summary>delay초(실제 시간) 뒤에 BGM 바꾸기</summary>
    public static void PlayBgmAfter(float delay, Func<SoundLibrary, AudioClip> pick, float fade = -1f, bool loop = true)
    {
        if (Instance == null || Instance.lib == null) return;
        Instance.StartCoroutine(Instance.BgmAfter(delay, pick(Instance.lib), fade, loop));
    }

    private IEnumerator BgmAfter(float delay, AudioClip clip, float fade, bool loop)
    {
        string scene = SceneManager.GetActiveScene().name;
        yield return new WaitForSecondsRealtime(delay);
        if (SceneManager.GetActiveScene().name == scene) SetBgm(clip, loop, fade);   // 그사이 씬이 바뀌었으면 취소
    }

    /// <summary>BGM 서서히 끄기</summary>
    public static void StopBgm(float fade = -1f)
    {
        if (Instance != null) Instance.SetBgm(null, true, fade);
    }

    /// <summary>환경음 바꾸기 (null = 끄기)</summary>
    public static void SetAmbience(AudioClip clip, float fade = 1.5f)
    {
        if (Instance != null) Instance.SetAmb(clip, fade);
    }

    private void SetBgm(AudioClip clip, bool loop, float fade)
    {
        bgmFade = fade >= 0f ? fade : lib.bgmFadeTime;
        if (clip != null && bgmCur.clip == clip && bgmCur.isPlaying) { bgmCur.loop = loop; return; }
        if (clip == null && bgmCur.clip == null) return;

        // 지금 곡은 서서히 줄어들고, 새 곡은 서서히 커짐
        AudioSource prev = bgmCur;
        bgmCur = bgmOld;
        bgmOld = prev;
        oldW = curW;
        curW = 0f;
        if (bgmFade <= 0f) { oldW = 0f; bgmOld.Stop(); }

        bgmCur.Stop();
        bgmCur.clip = clip;
        bgmCur.loop = loop;
        if (clip != null) bgmCur.Play();
    }

    private void SetAmb(AudioClip clip, float fade = 1.5f)
    {
        ambFade = Mathf.Max(0.01f, fade);
        if (clip == null) { ambTarget = 0f; return; }
        ambTarget = 1f;
        if (amb.clip == clip && amb.isPlaying) return;
        amb.clip = clip;
        ambW = 0f;
        amb.Play();
    }
}