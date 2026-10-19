using System;
using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

// �÷��̾� ü�� (GameScene�� [SYSTEM_MANAGERS] �Ʒ� �� ������Ʈ�� ���̱�)
// ü���� ���̴� ���
//   1. ���� ���� (EnemyController�� ���� �ð����� ����)
//   2. �ֹ� ���� �г�Ƽ (����): �� �� ���� �ֹ� / �Һи��� �ֹ� / ���� ����� ���� �ֹ�
// ü���� 0�� �Ǹ�: �� ���������� �Է� ���� �� ���θ�� �� ȭ�� ���� �� EndingScene (���� ����)
public class PlayerHealth : MonoBehaviour
{
    public static PlayerHealth Instance { get; private set; }

    [Header("ü��")]
    public float maxHP = 100f;
    [Tooltip("�� �� ���� �� �� �ð� ������ �߰� ���� ���� (���� �ǰ� ����)")]
    public float invulnerableTime = 0.5f;

    [Header("�ֹ� ���� �г�Ƽ")]
    [Tooltip("�ֹ��� �߸� ��ġ�� ������ �����ؼ� ü���� ���� ����")]
    public bool failPenalty = true;
    public float failDamage = 4f;
    [Tooltip("�г�Ƽ�� �� ���� �ҹ� ���� (EMPTY��NOISE ���� '�� �鸲'�� ���� �� ����ũ ſ���� ������� �ʰ�)")]
    public string[] penaltyRejectReasons = { "NO_MATCH", "AMBIGUOUS" };
    [Tooltip("����� ���� �ֹ��� ������ ���� �г�Ƽ")]
    public bool penalizeUnlearnedSpell = true;
    [SerializeField] VoiceSocket voiceSocket;

    [Header("���� ����")]
    public string endingSceneName = "EndingScene";
    [Tooltip("������ �� �������� ���� (���� �ð� ��)")]
    public float slowMotionTime = 1.2f;
    [Range(0.05f, 1f)] public float slowMotionScale = 0.3f;
    [Tooltip("(����) ȭ���� ��İ� ���� CanvasGroup. ���� �ٷ� �Ѿ")]
    public CanvasGroup fadeOverlay;
    public float fadeTime = 1f;

    public float CurrentHP { get; private set; }
    public bool IsDead { get; private set; }
    public float Ratio => maxHP > 0f ? CurrentHP / maxHP : 0f;

    // (����, �ִ�)
    public event Action<float, float> OnHealthChanged;
    // (���� ��, ����)
    public event Action<float, string> OnDamaged;
    public event Action OnDied;
    // (ȸ���� ��, ����)
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
        Debug.Log($"[�÷��̾�] {reason} -{amount} �� ü�� {CurrentHP}/{maxHP}");
        OnDamaged?.Invoke(amount, reason);
        OnHealthChanged?.Invoke(CurrentHP, maxHP);

        if (CurrentHP <= 0f) Die();
    }

    // ������ ȸ���� ���� ������ (�̹� ���� �� ������ 0)
    public float Heal(float amount, string reason = "")
    {
        if (IsDead || amount <= 0f) return 0f;
        float before = CurrentHP;
        CurrentHP = Mathf.Min(maxHP, CurrentHP + amount);
        float healed = CurrentHP - before;
        if (healed <= 0f) return 0f;

        Debug.Log($"[�÷��̾�] {reason} +{healed} �� ü�� {CurrentHP}/{maxHP}");
        OnHealed?.Invoke(healed, reason);
        OnHealthChanged?.Invoke(CurrentHP, maxHP);
        return healed;
    }

    // ���� �ֹ� ���� �г�Ƽ ����
    private void HandleCastResult(CastResult r)
    {
        if (!failPenalty || r == null || r.Success) return;
        if (Array.IndexOf(penaltyRejectReasons, r.RejectReason) < 0) return;
        if (!SkillManager.CanCast) return; // ���� ���ų� �������� ���̸� �г�Ƽ ����
        TakeDamage(failDamage, "�ֹ� ���� (���� ����)");
    }

    private void HandleCastFailed(string fail)
    {
        if (!failPenalty || !penalizeUnlearnedSpell) return;
        if (fail != "���� ����� ���� �ֹ�") return;
        TakeDamage(failDamage, "����� ���� �ֹ� (���� ����)");
    }

    // ���� ���� ���� ����
    private void Die()
    {
        if (IsDead) return;
        IsDead = true;
        Debug.Log("[�÷��̾�] ���������ϴ�. ���� ����");
        OnDied?.Invoke();

        // �� ���������� �Է� ����
        if (EnemyManager.Instance != null) EnemyManager.Instance.StopSpawning();
        VoiceSender sender = FindFirstObjectByType<VoiceSender>();
        if (sender != null) sender.enabled = false;
        VoiceRecorder recorder = FindFirstObjectByType<VoiceRecorder>();
        if (recorder != null) recorder.enabled = false;

        GameResult.Record(false, Time.time - startTime);

        // ������ ���� ��� (���� �Ŵ����� ���� ����)
        if (GameManager.Instance != null && NetworkManager.Instance != null)
        {
            GameManager.Instance.SetState(GameState.GameOver);
        }

        StartCoroutine(GameOverSequence());
    }

    private IEnumerator GameOverSequence()
    {
        // ���θ��
        Time.timeScale = slowMotionScale;
        yield return new WaitForSecondsRealtime(slowMotionTime);

        // ����
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