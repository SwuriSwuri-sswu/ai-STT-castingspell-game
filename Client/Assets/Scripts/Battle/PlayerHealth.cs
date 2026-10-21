using System;
using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

// 플레이어 체력 (GameScene의 [SYSTEM_MANAGERS] 아래 빈 오브젝트에 붙이기)
// 체력이 깎이는 경우: 몹의 공격만 (EnemyController / 보스전의 BossSpell·하수인)
//   ※ 음성 인식 실패·배우지 않은 주문으로는 체력이 깎이지 않음
// 체력이 0이 되면: 몹 스폰·음성 입력 정지 → 슬로모션 → 화면 암전 → EndingScene (게임 오버)
public class PlayerHealth : MonoBehaviour
{
    public static PlayerHealth Instance { get; private set; }

    [Header("체력")]
    public float maxHP = 100f;
    [Tooltip("한 번 맞은 뒤 이 시간 동안은 추가 피해 없음 (연속 피격 방지)")]
    public float invulnerableTime = 0.5f;

    [Header("맞았을 때 화면 흔들림")]
    [Tooltip("세기 (0 = 끔). 데미지가 클수록 더 세게 (데미지 10 = 이 값 그대로)")]
    [Min(0f)] public float hitShake = 1.5f;
    [Tooltip("흔들리는 시간 (초)")]
    public float hitShakeTime = 0.3f;

    [Header("게임 오버")]
    public string endingSceneName = "EndingScene";
    [Tooltip("쓰러질 때 느려지는 연출 (실제 시간 초)")]
    public float slowMotionTime = 1.2f;
    [Range(0.05f, 1f)] public float slowMotionScale = 0.3f;
    [Tooltip("(선택) 화면을 까맣게 덮을 CanvasGroup. 비우면 바로 넘어감")]
    public CanvasGroup fadeOverlay;
    public float fadeTime = 1f;

    public float CurrentHP { get; private set; }
    public bool IsDead { get; private set; }
    public float Ratio => maxHP > 0f ? CurrentHP / maxHP : 0f;

    // (현재, 최대)
    public event Action<float, float> OnHealthChanged;
    // (깎인 양, 이유)
    public event Action<float, string> OnDamaged;
    public event Action OnDied;
    // (회복한 양, 이유)
    public event Action<float, string> OnHealed;

    private float lastHitTime = -999f;
    private float startTime;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        CurrentHP = maxHP;
        startTime = Time.time;
    }

    private void Start()
    {
        OnHealthChanged?.Invoke(CurrentHP, maxHP);
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    public void TakeDamage(float amount, string reason = "")
    {
        if (IsDead || amount <= 0f) return;
        if (Time.time - lastHitTime < invulnerableTime) return;
        lastHitTime = Time.time;

        CurrentHP = Mathf.Max(0f, CurrentHP - amount);
        Debug.Log($"[플레이어] {reason} -{amount} → 체력 {CurrentHP}/{maxHP}");
        OnDamaged?.Invoke(amount, reason);
        OnHealthChanged?.Invoke(CurrentHP, maxHP);
        if (hitShake > 0f) CameraKick.Shake(hitShake * Mathf.Clamp(amount / 10f, 0.6f, 2f), hitShakeTime);

        if (CurrentHP <= 0f) Die();
    }

    // 실제로 회복된 양을 돌려줌 (이미 가득 차 있으면 0)
    public float Heal(float amount, string reason = "")
    {
        if (IsDead || amount <= 0f) return 0f;
        float before = CurrentHP;
        CurrentHP = Mathf.Min(maxHP, CurrentHP + amount);
        float healed = CurrentHP - before;
        if (healed <= 0f) return 0f;

        Debug.Log($"[플레이어] {reason} +{healed} → 체력 {CurrentHP}/{maxHP}");
        OnHealed?.Invoke(healed, reason);
        OnHealthChanged?.Invoke(CurrentHP, maxHP);
        return healed;
    }

    // 바로 쓰러뜨리기 (게임 오버 테스트용)
    public void Kill(string reason = "")
    {
        if (IsDead) return;
        Debug.Log($"[플레이어] {reason} → 바로 쓰러짐");
        CurrentHP = 0f;
        OnHealthChanged?.Invoke(CurrentHP, maxHP);
        Die();
    }

    // ── 게임 오버 ──
    private void Die()
    {
        if (IsDead) return;
        IsDead = true;
        Debug.Log("[플레이어] 쓰러졌습니다. 게임 오버");
        OnDied?.Invoke();

        // 몹 스폰·음성 입력 정지
        if (EnemyManager.Instance != null) EnemyManager.Instance.StopSpawning();
        VoiceSender sender = FindFirstObjectByType<VoiceSender>();
        if (sender != null) sender.enabled = false;
        VoiceRecorder recorder = FindFirstObjectByType<VoiceRecorder>();
        if (recorder != null) recorder.enabled = false;

        GameResult.Record(false, Time.time - startTime);

        // 서버에 종료 기록 (서버 매니저가 있을 때만)
        if (GameManager.Instance != null && NetworkManager.Instance != null)
        {
            GameManager.Instance.SetState(GameState.GameOver);
        }

        // 씬에 GameOverSequence가 있으면 그 자리에서 3인칭 게임 오버 연출 (없으면 예전처럼 EndingScene으로)
        if (global::GameOverSequence.Instance != null)
        {
            global::GameOverSequence.Instance.Begin(GameResult.IsBossFight ? global::GameOverSequence.Kind.BossDefeat
                                                                            : global::GameOverSequence.Kind.GameOver);
            return;
        }
        StartCoroutine(GameOverSequence());
    }

    private IEnumerator GameOverSequence()
    {
        // 슬로모션
        Time.timeScale = slowMotionScale;
        yield return new WaitForSecondsRealtime(slowMotionTime);

        // 암전
        if (fadeOverlay != null)
        {
            fadeOverlay.gameObject.SetActive(true);
            fadeOverlay.blocksRaycasts = true;
            float t = 0f;
            while (t < fadeTime)
            {
                t += Time.unscaledDeltaTime;
                fadeOverlay.alpha = Mathf.Clamp01(t / fadeTime);
                yield return null;
            }
        }

        Time.timeScale = 1f;
        SceneManager.LoadScene(endingSceneName);
    }
}