using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

/// <summary>
/// 디버그용 실시간 소리 표시 (Game 화면).
///  - 위 그래프: 파형. 최근 waveformSec 초의 샘플을 가로 픽셀 수로 나누고, 열마다 최소~최대 진폭을 세로선으로 그린다.
///               |값| 이 clipLevel 이상이면 빨간색 (입력이 너무 커서 소리가 깨지는 상태).
///  - 아래 그래프: VAD 음량(dB) 기록. 최근 historySec 초를 가로 픽셀 수만큼의 칸으로 나눠,
///               칸마다 그 시간 동안의 최대 dB 를 막대로 그린다. 막대 색은 그 시점의 상태.
///               노란 가로선 = 말소리 기준, 회색 가로선 = 소음 기준.
///  - 맨 위 한 줄: 서버 연결 상태, 녹음·판정 대기 상태, 마지막 발화 정보.
///  - 디버그 WAV 저장: VoiceSender 가 서버로 보낸 WAV 를 persistentDataPath/mic_debug 에 저장한다 (판정 문제 확인용).
/// VoiceInput·VoiceSender·VoiceSocket 의 값을 읽기만 하며, 감지·전송 동작에는 영향을 주지 않는다.
///  - 테스트 키: C = 해금된 스펠 하나를 음성 없이 시전, U = 잠긴 스펠 하나의 두루마리 드랍(실제 드랍 모션 → 획득),
///               Shift+U = 잠긴 스펠 하나 바로 해금, R = 해금 초기화.
/// 테스트용 컴포넌트이므로 배포할 때는 끄거나 제거한다.
/// </summary>
public class VoiceDebug : MonoBehaviour
{
    [Header("연결")]
    [SerializeField] VoiceInput voiceInput;
    [SerializeField] VoiceSender voiceSender;
    [SerializeField] VoiceSocket voiceSocket;

    [Header("디버그 WAV 저장")]
    [Tooltip("켜면 서버로 보낸 발화 WAV 를 persistentDataPath/mic_debug 에 저장한다")]
    [SerializeField] bool saveDebugWav = true;

    [Header("테스트 키 (게임 조작과 겹치면 None)")]
    [SerializeField] KeyCode castTestKey = KeyCode.C;   // 해금된 스펠 하나를 음성 없이 시전 (시전 경로 확인)
    [SerializeField] KeyCode unlockKey = KeyCode.U;     // 잠긴 스펠 하나의 두루마리 드랍 (Shift 와 함께 누르면 바로 해금)
    [SerializeField] KeyCode resetKey = KeyCode.R;      // 해금 초기화 (게임 재시작 흉내)

    [Header("테스트 드랍 (U = 두루마리 드랍, Shift+U = 바로 해금)")]
    [Tooltip("U 키로 떨어뜨릴 두루마리 프리팹 (Assets/Prefabs/SpellDrop). 비우면 U 도 바로 해금한다")]
    [SerializeField] DropItem dropItemPrefab;
    [Tooltip("몹 위치에서 떨어뜨릴 때 두루마리가 처음 생기는 위치 보정 (몹 발밑 기준)")]
    [SerializeField] Vector3 dropOffset = new Vector3(0f, 1f, 0f);
    [Tooltip("몹이 없을 때 두루마리가 생기는 거리 (카메라 정면, m)")]
    [SerializeField] float cameraDropDistance = 4f;

    // 날아오는 중인 테스트 두루마리 (같은 스펠이 겹쳐 떨어지지 않게). 획득하면 두루마리가 삭제되어 null 이 된다
    readonly Dictionary<SpellData, DropItem> pendingDrops = new Dictionary<SpellData, DropItem>();

    [Header("표시")]
    [SerializeField] bool show = true;
    [SerializeField] Vector2 screenPosition = new Vector2(10, 260);   // 화면 왼쪽 위 기준 위치 (픽셀)
    [SerializeField] int width = 480;          // 그래프 가로 픽셀 수
    [SerializeField] int waveHeight = 100;     // 파형 그래프 세로 픽셀 수
    [SerializeField] int dbHeight = 120;       // dB 그래프 세로 픽셀 수
    [SerializeField] float waveformSec = 0.5f; // 파형에 표시할 시간 (초)
    [SerializeField] float historySec = 6f;    // dB 기록에 표시할 시간 (초)
    [SerializeField] float dbMin = -80f;       // dB 그래프 아래 끝
    [SerializeField] float dbMax = 0f;         // dB 그래프 위 끝 (0dBFS = 최대 입력)
    [SerializeField] float clipLevel = 0.99f;  // 이 이상이면 소리 깨짐으로 표시

    // 색상
    static readonly Color32 Bg = new Color32(20, 20, 20, 220);
    static readonly Color32 Grid = new Color32(70, 70, 70, 255);
    static readonly Color32 Wave = new Color32(80, 220, 120, 255);
    static readonly Color32 Clip = new Color32(255, 60, 60, 255);
    static readonly Color32 ThresholdLine = new Color32(255, 200, 0, 255);
    static readonly Color32 NoiseLine = new Color32(170, 170, 170, 255);
    static readonly Color32 CalibratingColor = new Color32(200, 180, 60, 255);
    static readonly Color32 IdleColor = new Color32(100, 100, 100, 255);
    static readonly Color32 SpeakingColor = new Color32(80, 200, 120, 255);
    static readonly Color32 WaitingColor = new Color32(90, 140, 230, 255);

    // 표시 상태 (VadState 3개 + 판정 대기)
    const byte StCalibrating = 0, StIdle = 1, StSpeaking = 2, StWaiting = 3;

    Texture2D waveTex, dbTex;
    Color32[] wavePixels, dbPixels;

    // 파형: 최근 샘플을 보관하는 원형 버퍼 (waveHead = 가장 오래된 샘플 위치 = 다음에 쓸 위치)
    float[] waveRing;
    int waveHead;
    int waveRate;

    // dB 기록: 가로 픽셀 1칸 = historySec / width 초 (histHead = 가장 오래된 칸 = 다음에 쓸 칸)
    float[] dbHist, thrHist, noiseHist;
    byte[] stateHist;
    int histHead;
    float histTimer;
    float peakDbInColumn = float.NegativeInfinity;   // 현재 칸 시간 동안의 최대 dB

    string statusText = "";
    string headerText = "";   // 맨 위 한 줄 (연결·녹음 상태)

    void OnEnable()
    {
        if (voiceInput != null) voiceInput.SamplesAvailable += OnSamples;
        if (voiceSender != null) voiceSender.UtteranceSent += SaveDebugWav;
    }

    void OnDisable()
    {
        if (voiceInput != null) voiceInput.SamplesAvailable -= OnSamples;
        if (voiceSender != null) voiceSender.UtteranceSent -= SaveDebugWav;
    }

    void Start()
    {
        waveTex = CreateTexture(width, waveHeight, out wavePixels);
        dbTex = CreateTexture(width, dbHeight, out dbPixels);

        dbHist = new float[width];
        thrHist = new float[width];
        noiseHist = new float[width];
        stateHist = new byte[width];
        for (int i = 0; i < width; i++) { dbHist[i] = dbMin; thrHist[i] = float.NaN; noiseHist[i] = float.NaN; stateHist[i] = StIdle; }
    }

    void OnDestroy()
    {
        if (waveTex != null) Destroy(waveTex);
        if (dbTex != null) Destroy(dbTex);
    }

    static Texture2D CreateTexture(int w, int h, out Color32[] pixels)
    {
        var tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
        pixels = new Color32[w * h];
        return tex;
    }

    // ── 샘플 수신: 파형 원형 버퍼에 복사 (배열은 재사용되므로 즉시 복사) ──
    void OnSamples(float[] samples, int count)
    {
        int rate = voiceInput.SampleRate;
        if (waveRing == null || rate != waveRate)   // 처음 또는 장치 변경으로 샘플레이트가 바뀐 경우
        {
            waveRate = rate;
            waveRing = new float[Mathf.Max(width, Mathf.RoundToInt(waveformSec * rate))];
            waveHead = 0;
        }
        for (int i = 0; i < count; i++)
        {
            waveRing[waveHead] = samples[i];
            waveHead = (waveHead + 1) % waveRing.Length;
        }
    }

    void Update()
    {
        HandleTestKeys();
        if (!show || dbHist == null) return;

        // ── dB 기록: VAD 의 현재 값을 읽어 칸 단위로 쌓는다 ──
        VoiceActivityDetector vad = voiceSender != null ? voiceSender.Vad : null;
        if (vad != null) peakDbInColumn = Mathf.Max(peakDbInColumn, vad.CurrentDb);

        float columnSec = historySec / width;
        histTimer += Time.unscaledDeltaTime;
        while (histTimer >= columnSec)
        {
            histTimer -= columnSec;
            dbHist[histHead] = vad != null && !float.IsNegativeInfinity(peakDbInColumn) ? peakDbInColumn : dbMin;
            thrHist[histHead] = vad != null ? vad.ThresholdDb : float.NaN;
            noiseHist[histHead] = vad != null ? vad.NoiseFloorDb : float.NaN;
            stateHist[histHead] = CurrentStateCode(vad);
            histHead = (histHead + 1) % width;
            peakDbInColumn = float.NegativeInfinity;
        }

        DrawWaveform();
        DrawDbHistory();

        statusText = vad == null
            ? "VAD: not ready"
            : $"dB {vad.CurrentDb:F1}  |  noise {vad.NoiseFloorDb:F1}  |  threshold {vad.ThresholdDb:F1}  |  " +
              (voiceSender.IsWaitingForResult ? "Waiting (blocked)" : vad.State.ToString());

        // 맨 위 한 줄: 서버 연결, 녹음·판정 대기 상태, 마지막 발화 정보
        string server = voiceSocket == null ? "-" : voiceSocket.IsConnected ? "Connected" : "Disconnected";
        string rec = voiceSender == null ? "-"
            : voiceSender.IsRecording ? "● REC"
            : voiceSender.IsWaitingForResult ? "Waiting for result (blocked)"
            : "Ready";
        headerText = $"Server: {server}   |   {rec}   |   Last: {(voiceSender != null ? voiceSender.LastInfo : "-")}";
    }

    byte CurrentStateCode(VoiceActivityDetector vad)
    {
        if (voiceSender != null && voiceSender.IsWaitingForResult) return StWaiting;   // 판정 대기(막기)를 우선 표시
        if (vad == null) return StIdle;
        switch (vad.State)
        {
            case VadState.Calibrating: return StCalibrating;
            case VadState.Speaking: return StSpeaking;
            default: return StIdle;
        }
    }

    // ── 파형: 열마다 담당 구간의 최소~최대 진폭을 세로선으로 그린다 ──
    // ── 테스트 키: SkillManager 를 직접 호출한다. 목록이 바뀌면 SpellsChanged 로 서버에 자동 재전송된다 ──
    void HandleTestKeys()
    {
        SkillManager sm = SkillManager.Instance;
        if (sm == null) return;

        if (Input.GetKeyDown(castTestKey))
        {
            var list = sm.GetVoiceSpells();
            if (list.Count > 0)
            {
                string name = list[UnityEngine.Random.Range(0, list.Count)].spellName;
                Debug.Log($"[테스트] {name} 시전: {sm.TryCastByVoice(name) ?? "성공"}");
            }
        }
        if (Input.GetKeyDown(unlockKey))
        {
            bool instant = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
            SpellData spell = PickLockedSpell(sm);
            if (spell == null) Debug.Log("[테스트] 해금할 스펠이 없습니다 (모두 해금했거나 두루마리가 날아오는 중).");
            else if (instant || dropItemPrefab == null) sm.UnlockSpell(spell);
            else SpawnTestDrop(spell);
        }
        if (Input.GetKeyDown(resetKey)) sm.ResetSpellUnlocks();
    }

    // 잠긴 스펠 중 하나를 고른다. 날아오는 중인 두루마리의 스펠은 뺀다
    SpellData PickLockedSpell(SkillManager sm)
    {
        // 획득되어 삭제된 두루마리는 목록에서 정리 (Unity 오브젝트는 삭제되면 null 과 같다)
        var finished = new List<SpellData>();
        foreach (var pair in pendingDrops)
            if (pair.Value == null) finished.Add(pair.Key);
        foreach (var s in finished) pendingDrops.Remove(s);

        var locked = sm.spellDatabase.FindAll(s => s != null && !sm.IsUnlocked(s) && !pendingDrops.ContainsKey(s));
        return locked.Count > 0 ? locked[UnityEngine.Random.Range(0, locked.Count)] : null;
    }

    // 실제 처치 드랍과 같은 두루마리를 만든다: 현재 몹 위치(없으면 카메라 앞) → 날아와 획득 → UnlockSpell + 획득 알림
    void SpawnTestDrop(SpellData spell)
    {
        EnemyController enemy = EnemyManager.Instance != null ? EnemyManager.Instance.CurrentEnemy : null;
        DropItem drop;
        if (enemy != null)
        {
            drop = Instantiate(dropItemPrefab, enemy.transform.position + dropOffset, Quaternion.identity);
            drop.Initialize(spell, enemy.GroundPoint);   // 바닥 높이 기준 (EnemyController 와 같은 방식)
        }
        else
        {
            Camera cam = Camera.main;
            if (cam == null)
            {
                Debug.LogWarning("[테스트] 메인 카메라가 없어 두루마리 대신 바로 해금합니다.");
                SkillManager.Instance.UnlockSpell(spell);
                return;
            }
            drop = Instantiate(dropItemPrefab, cam.transform.position + cam.transform.forward * cameraDropDistance, Quaternion.identity);
            drop.Initialize(spell);
        }
        pendingDrops[spell] = drop;
        Debug.Log($"[테스트] {spell.spellName} 두루마리 드랍 ({(enemy != null ? "몹 위치" : "카메라 앞")})");
    }

    void DrawWaveform()
    {
        Fill(wavePixels, Bg);
        int mid = waveHeight / 2;
        HLine(wavePixels, width, mid, Grid);   // 0 기준선

        if (waveRing != null)
        {
            int n = waveRing.Length;
            float perColumn = (float)n / width;
            for (int x = 0; x < width; x++)
            {
                int start = Mathf.FloorToInt(x * perColumn);
                int end = Mathf.Max(start + 1, Mathf.FloorToInt((x + 1) * perColumn));
                float min = 0f, max = 0f;
                for (int i = start; i < end; i++)
                {
                    float v = waveRing[(waveHead + i) % n];   // waveHead 부터 읽으면 오래된 샘플 → 최신 샘플 순서
                    if (v < min) min = v;
                    if (v > max) max = v;
                }
                Color32 c = (max >= clipLevel || min <= -clipLevel) ? Clip : Wave;
                int y0 = Mathf.Clamp(mid + Mathf.RoundToInt(min * (mid - 1)), 0, waveHeight - 1);
                int y1 = Mathf.Clamp(mid + Mathf.RoundToInt(max * (mid - 1)), 0, waveHeight - 1);
                for (int y = y0; y <= y1; y++) wavePixels[y * width + x] = c;
            }
        }
        waveTex.SetPixels32(wavePixels);
        waveTex.Apply(false);
    }

    // ── dB 기록: 막대(상태 색) + 소음 기준선 + 말소리 기준선 ──
    void DrawDbHistory()
    {
        Fill(dbPixels, Bg);
        // -20, -40, -60 dB 눈금선
        for (float g = -20f; g > dbMin; g -= 20f) HLine(dbPixels, width, DbToY(g), Grid);

        for (int x = 0; x < width; x++)
        {
            int idx = (histHead + x) % width;   // 가장 오래된 칸부터 왼쪽에 그린다
            int top = DbToY(dbHist[idx]);
            Color32 c = StateColor(stateHist[idx]);
            for (int y = 0; y <= top; y++) dbPixels[y * width + x] = c;

            if (!float.IsNaN(noiseHist[idx])) dbPixels[DbToY(noiseHist[idx]) * width + x] = NoiseLine;
            if (!float.IsNaN(thrHist[idx])) dbPixels[DbToY(thrHist[idx]) * width + x] = ThresholdLine;
        }
        dbTex.SetPixels32(dbPixels);
        dbTex.Apply(false);
    }

    int DbToY(float db)
    {
        float t = Mathf.InverseLerp(dbMin, dbMax, db);   // dbMin → 0, dbMax → 1 (범위 밖은 잘림)
        return Mathf.Clamp(Mathf.RoundToInt(t * (dbHeight - 1)), 0, dbHeight - 1);
    }

    static Color32 StateColor(byte s)
    {
        switch (s)
        {
            case StCalibrating: return CalibratingColor;
            case StSpeaking: return SpeakingColor;
            case StWaiting: return WaitingColor;
            default: return IdleColor;
        }
    }

    static void Fill(Color32[] px, Color32 c) { for (int i = 0; i < px.Length; i++) px[i] = c; }

    static void HLine(Color32[] px, int w, int y, Color32 c) { for (int x = 0; x < w; x++) px[y * w + x] = c; }

    // ── 디버그 WAV 저장: VoiceSender 가 서버로 보낸 WAV 를 그대로 파일로 남긴다 ──
    void SaveDebugWav(byte[] wav)
    {
        if (!saveDebugWav) return;
        string dir = Path.Combine(Application.persistentDataPath, "mic_debug");
        Directory.CreateDirectory(dir);
        string path = Path.Combine(dir, $"mic_{DateTime.Now:yyyyMMdd_HHmmss_fff}.wav");
        File.WriteAllBytes(path, wav);
        Debug.Log($"[DEBUG] 디버그 WAV 저장: {path}");
    }

    void OnGUI()
    {
        if (!show || waveTex == null) return;
        float x = screenPosition.x, y = screenPosition.y;

        GUI.Label(new Rect(x, y, width, 20), headerText);
        y += 22;

        GUI.Label(new Rect(x, y, width, 20), "Waveform (last " + waveformSec + "s)");
        GUI.DrawTexture(new Rect(x, y + 20, width, waveHeight), waveTex);

        float y2 = y + 20 + waveHeight + 6;
        GUI.Label(new Rect(x, y2, width, 20), $"Level dB (last {historySec}s)  yellow=threshold  gray=noise");
        GUI.DrawTexture(new Rect(x, y2 + 20, width, dbHeight), dbTex);

        GUI.Label(new Rect(x, y2 + 20 + dbHeight + 2, width, 20), statusText);
    }
}