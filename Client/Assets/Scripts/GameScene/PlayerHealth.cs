using System;
using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

// 플레이어 체력 (GameScene의 [SYSTEM_MANAGERS] 아래 빈 오브젝트에 붙이기)
// 체력이 깎이는 경우
//   1. 몹의 공격 (EnemyController가 일정 시간마다 공격)
//   2. 주문 실패 패널티 (선택): 알 수 없는 주문 / 불분명한 주문 / 아직 배우지 않은 주문
// 체력이 0이 되면: 몹 스폰·음성 입력 정지 → 슬로모션 → 화면 암전 → EndingScene (게임 오버)
public class PlayerHealth : MonoBehaviour
{
    public static PlayerHealth Instance { get; private set; }

    [Header("체력")]
    public float maxHP = 100f;
    [Tooltip("한 번 맞은 뒤 이 시간 동안은 추가 피해 없음 (연속 피격 방지)")]
    public float invulnerableTime = 0.5f;

    [Header("주문 실패 패널티")]
    [Tooltip("주문을 잘못 외치면 마력이 역류해서 체력이 조금 깎임")]
    public bool failPenalty = true;
    public float failDamage = 4f;
    [Tooltip("패널티를 줄 서버 불발 사유 (EMPTY·NOISE 같은 '안 들림'은 제외 → 마이크 탓으로 억울하지 않게)")]
    public string[] penaltyRejectReasons = { "NO_MATCH", "AMBIGUOUS" };
    [Tooltip("배우지 않은 주문을 외쳤을 때도 패널티")]
    public bool penalizeUnlearnedSpell = true;
    [SerializeField] VoiceSocket voiceSocket;

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
        if (voiceSocket == null) voiceSocket = FindFirstObjectByType<VoiceSocket>();
        if (voiceSocket != null) voiceSocket.CastResultReceived += HandleCastResult;
        if (SkillManager.Instance != null) SkillManager.Instance.OnVoiceCastFailed += HandleCastFailed;
        OnHealthChanged?.Invoke(CurrentHP, maxHP);
    }

    private void OnDestroy()
    {
        if (voiceSocket != null) voiceSocket.CastResultReceived -= HandleCastResult;
        if (SkillManager.Instance != null) SkillManager.Instance.OnVoiceCastFailed -= HandleCastFailed;
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

        if (CurrentHP <= 0f) Die();
    }

    public void Heal(float amount)
    {
        if (IsDead || amount <= 0f) return;
        CurrentHP = Mathf.Min(maxHP, CurrentHP + amount);
        OnHealthChanged?.Invoke(CurrentHP, maxHP);
    }

    // ── 주문 실패 패널티 ──
    private void HandleCastResult(CastResult r)
    {
        if (!failPenalty || r == null || r.Success) return;
        if (Array.IndexOf(penaltyRejectReasons, r.RejectReason) < 0) return;
        if (!SkillManager.CanCast) return; // 몹이 없거나 쓰러지는 중이면 패널티 없음
        TakeDamage(failDamage, "주문 실패 (마력 역류)");
    }

    private void HandleCastFailed(string fail)
    {
        if (!failPenalty || !penalizeUnlearnedSpell) return;
        if (fail != "아직 배우지 않은 주문") return;
        TakeDamage(failDamage, "배우지 않은 주문 (마력 역류)");
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