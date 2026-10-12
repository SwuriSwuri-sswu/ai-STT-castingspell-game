using UnityEngine;

/// <summary>
/// 디버그용 실시간 소리 표시 (Game 화면).
///  - 위 그래프: 파형. 최근 waveformSec 초의 샘플을 가로 픽셀 수로 나누고, 열마다 최소~최대 진폭을 세로선으로 그린다.
///               |값| 이 clipLevel 이상이면 빨간색 (입력이 너무 커서 소리가 깨지는 상태).
///  - 아래 그래프: VAD 음량(dB) 기록. 최근 historySec 초를 가로 픽셀 수만큼의 칸으로 나눠,
///               칸마다 그 시간 동안의 최대 dB 를 막대로 그린다. 막대 색은 그 시점의 상태.
///               노란 가로선 = 말소리 기준, 회색 가로선 = 소음 기준.
/// MicStream 의 샘플과 MicRecorder 의 VAD 값을 읽기만 하며, 감지·전송 동작에는 영향을 주지 않는다.
/// 빌드 시에는 이 컴포넌트를 끄거나 제거한다.
/// </summary>
public class MicScope : MonoBehaviour
{
    [Header("연결")]
    [SerializeField] MicStream micStream;
    [SerializeField] MicRecorder micRecorder;

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

    void OnEnable()
    {
        if (micStream != null) micStream.SamplesAvailable += OnSamples;
    }

    void OnDisable()
    {
        if (micStream != null) micStream.SamplesAvailable -= OnSamples;
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
        int rate = micStream.SampleRate;
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
        if (!show || dbHist == null) return;

        // ── dB 기록: VAD 의 현재 값을 읽어 칸 단위로 쌓는다 ──
        VoiceActivityDetector vad = micRecorder != null ? micRecorder.Vad : null;
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
              (micRecorder.IsWaitingForResult ? "Waiting (blocked)" : vad.State.ToString());
    }

    byte CurrentStateCode(VoiceActivityDetector vad)
    {
        if (micRecorder != null && micRecorder.IsWaitingForResult) return StWaiting;   // 판정 대기(막기)를 우선 표시
        if (vad == null) return StIdle;
        switch (vad.State)
        {
            case VadState.Calibrating: return StCalibrating;
            case VadState.Speaking: return StSpeaking;
            default: return StIdle;
        }
    }

    // ── 파형: 열마다 담당 구간의 최소~최대 진폭을 세로선으로 그린다 ──
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

    void OnGUI()
    {
        if (!show || waveTex == null) return;
        float x = screenPosition.x, y = screenPosition.y;

        GUI.Label(new Rect(x, y, width, 20), "Waveform (last " + waveformSec + "s)");
        GUI.DrawTexture(new Rect(x, y + 20, width, waveHeight), waveTex);

        float y2 = y + 20 + waveHeight + 6;
        GUI.Label(new Rect(x, y2, width, 20), $"Level dB (last {historySec}s)  yellow=threshold  gray=noise");
        GUI.DrawTexture(new Rect(x, y2 + 20, width, dbHeight), dbTex);

        GUI.Label(new Rect(x, y2 + 20 + dbHeight + 2, width, 20), statusText);
    }
}