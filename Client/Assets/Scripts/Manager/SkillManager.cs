using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// 음성 인식 결과 → 마법 시전, 스펠 해금 상태, 드랍할 스펠 선택을 담당
public class SkillManager : MonoBehaviour
{
    public static SkillManager Instance { get; private set; }

    [Header("Magic Casting Setup")]
    [Tooltip("지팡이 끝 빈 오브젝트")]
    public Transform wandTip;
    [Tooltip("게임에 존재하는 모든 SpellData 에셋")]
    public List<SpellData> spellDatabase = new List<SpellData>();

    [Header("즉발형(장판) 이펙트 위치 보정")]
    [Tooltip("몹 발밑 기준 높이 보정 (메테오처럼 위에서 떨어지는 이펙트면 값을 올리세요)")]
    public float instantSpawnHeightOffset = 0f;

    // 스펠이 새로 해금될 때 (UI 갱신용)
    public event Action<SpellData> OnSpellUnlocked;
    // 마법이 실제로 시전된 순간 (스펠, 타겟, 영창 보너스 power). 지팡이 휘두르기 연출 등에 사용
    public event Action<SpellData, EnemyController, float> OnSpellCast;
    // 음성으로 외친 주문이 시전되지 못한 순간 (사유). 플레이어 체력 패널티 등에 사용
    public event Action<string> OnVoiceCastFailed;

    // 플레이 중 해금 상태 (에셋 파일을 직접 바꾸지 않기 위해 런타임에서만 관리)
    private readonly HashSet<SpellData> unlockedSpells = new HashSet<SpellData>();

    public IReadOnlyCollection<SpellData> UnlockedSpells => unlockedSpells;
    [Header("회차 이어하기")]
    [Tooltip("켜면 이전 회차에서 한 번이라도 배운 스펠을 새 게임 시작 때 바로 쓸 수 있습니다.\n" +
             "(이 컴퓨터에 저장된 스펠 도감 기록 기준)")]
    public bool carryOverLearnedSpells = true;

    // 해금된 순서대로 (스펠 도감에서 차곡차곡 쌓을 때 사용)
    private readonly List<SpellData> unlockOrder = new List<SpellData>();
    public IReadOnlyList<SpellData> UnlockedInOrder => unlockOrder;

    // 몹이 스폰포인트에 도착해 있어야 시전 가능 (VoiceRecorder에서 녹음 시작 전에 확인)
    public static bool CanCast => EnemyController.AnyReady;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        ResetSpellUnlocks();
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    // =========================================================
    // [1] 마법 시전 (VoiceRecorder → 여기)
    // =========================================================
    // 실제로 시전되면 true. 실패하면 로그를 남기고 false (기존처럼 반환값을 무시해도 된다)
    // power: 영창 보너스 (VoiceBonusCalculator 결과, 기본 1.0)
    public bool CastSpell(string recognizedText, float power = 1f)
    {
        if (string.IsNullOrWhiteSpace(recognizedText)) return false;

        SpellData spell = FindSpell(recognizedText);
        if (spell == null)
        {
            Debug.Log($"[시전 실패] 알 수 없는 주문입니다: {recognizedText}");
            return false;
        }
        if (!IsUnlocked(spell))
        {
            Debug.Log($"[시전 실패] 아직 배우지 않은 주문입니다: {spell.spellName}");
            return false;
        }

        Vector3 origin = wandTip != null ? wandTip.position : transform.position;
        EnemyController target = EnemyController.GetNearest(origin);

        bool casted;
        switch (spell.castType)
        {
            case CastType.Projectile: casted = CastProjectile(spell, target, origin, power); break;
            case CastType.WandCharge: casted = CastCharge(spell, target, origin, power); break;
            default: casted = CastInstant(spell, target, power); break;
        }

        if (!casted) return false;

        // 데미지 예약: 이 마법으로 몹이 쓰러질 예정이면 그 즉시 영창이 막힌다 (이펙트 연출과 별개)
        if (target != null) target.ReserveDamage(spell, power);
        OnSpellCast?.Invoke(spell, target, power);

        // 점수 처리 (GameManager가 씬에 없을 수도 있으므로 null 체크)
        if (GameManager.Instance != null)
        {
            GameManager.Instance.OnSpellRecognized(spell.spellName, Mathf.Max(1, spell.tier) * 50);
        }
        return true;
    }

    private bool CastProjectile(SpellData spell, EnemyController target, Vector3 origin, float power)
    {
        if (spell.projectilePrefab == null)
        {
            Debug.LogWarning($"[{spell.spellName}] projectilePrefab이 비어 있습니다.");
            return false;
        }

        // 1:1 구조: 몹이 없거나 이미 쓰러지는 중이면 시전하지 않음 (다음 몹 등장 전)
        if (target == null)
        {
            Debug.Log($"[시전 실패] 타겟이 없어 {spell.spellName}을(를) 시전할 수 없습니다.");
            return false;
        }

        Quaternion rotation = wandTip != null ? wandTip.rotation : transform.rotation;
        Vector3 dir = target.AimPoint - origin;
        if (dir.sqrMagnitude > 0.0001f) rotation = Quaternion.LookRotation(dir);

        GameObject projectile = Instantiate(spell.projectilePrefab, origin, rotation);

        // 에셋 팩 이펙트에는 스크립트가 없을 수 있으므로 없으면 붙여줌
        ProjectileController controller = projectile.GetComponent<ProjectileController>();
        if (controller == null) controller = projectile.AddComponent<ProjectileController>();
        controller.Initialize(spell, target, power);

        Debug.Log($"[마법 시전] {spell.spellName} 발사!");
        return true;
    }

    private bool CastInstant(SpellData spell, EnemyController target, float power)
    {
        if (target == null)
        {
            Debug.Log($"[시전 실패] 타겟이 없어 {spell.spellName}을(를) 시전할 수 없습니다.");
            return false;
        }

        Vector3 spawnPos = target.GroundPoint + Vector3.up * instantSpawnHeightOffset;
        float effectLength = EffectUtil.SpawnAndAutoDestroy(spell.explosionPrefab, spawnPos, Quaternion.identity,
                                                            spell.effectDuration, this);

        // 기본값: 장판 연출이 끝난 뒤 데미지 → 사망 애니메이션
        target.ReceiveSpell(spell, spell.ResolveDamageDelay(effectLength), power);

        Debug.Log($"[마법 시전] {spell.spellName} 타겟 위치에 발동!");
        return true;
    }

    // 차지형: 지팡이 끝에 이펙트가 붙어서 재생 → 한 번 끝나면 몹에게 데미지 (→ 쓰러짐)
    private bool CastCharge(SpellData spell, EnemyController target, Vector3 origin, float power)
    {
        if (spell.chargePrefab == null)
        {
            Debug.LogWarning($"[{spell.spellName}] chargePrefab이 비어 있습니다.");
            return false;
        }
        if (target == null)
        {
            Debug.Log($"[시전 실패] 타겟이 없어 {spell.spellName}을(를) 시전할 수 없습니다.");
            return false;
        }

        // 지팡이 끝의 자식으로 생성 → 지팡이가 움직여도 따라다님
        Transform parent = wandTip != null ? wandTip : transform;
        GameObject charge = Instantiate(spell.chargePrefab, origin, parent.rotation, parent);

        // 연출 시간
        // - Effect Duration이 있으면: 그 시간 동안 (루프면 반복) 재생
        // - 없으면: 루프를 끄고 한 사이클만 재생, 길이 자동 계산
        float chargeLength;
        if (spell.effectDuration > 0f)
        {
            chargeLength = spell.effectDuration;
        }
        else
        {
            EffectUtil.ForceSingleCycle(charge);
            chargeLength = EffectUtil.GetDuration(charge);
        }

        StartCoroutine(FinishCharge(charge, chargeLength, spell, target, power));

        Debug.Log($"[마법 시전] {spell.spellName} 차징 시작! ({chargeLength:F1}초)");
        return true;
    }

    private IEnumerator FinishCharge(GameObject charge, float chargeLength, SpellData spell, EnemyController target, float power)
    {
        float damageDelay = spell.ResolveDamageDelay(chargeLength);

        // 데미지가 차지보다 먼저 들어가도록 설정했다면 그 시점에 처리
        if (damageDelay < chargeLength)
        {
            yield return new WaitForSeconds(damageDelay);
            HitAfterCharge(spell, target, power);
            yield return new WaitForSeconds(chargeLength - damageDelay);
        }
        else
        {
            yield return new WaitForSeconds(chargeLength);
        }

        // 차지 이펙트 종료 (루프 이펙트여도 여기서 멈춤)
        float tail = EffectUtil.StopEmitting(charge);
        if (charge != null) Destroy(charge, tail + 0.1f);

        // 기본값: 차지가 끝나는 순간 데미지 → (HP 0이면) 쓰러짐
        if (damageDelay >= chargeLength)
        {
            if (damageDelay > chargeLength) yield return new WaitForSeconds(damageDelay - chargeLength);
            HitAfterCharge(spell, target, power);
        }
    }

    private void HitAfterCharge(SpellData spell, EnemyController target, float power)
    {
        if (target == null || target.IsDying) return;

        // (선택) 몹 위치에 터지는 이펙트
        if (spell.explosionPrefab != null)
        {
            EffectUtil.SpawnAndAutoDestroy(spell.explosionPrefab, target.AimPoint, Quaternion.identity);
        }
        target.TakeDamage(spell, power);
    }

    // 띄어쓰기·대소문자를 무시하고 스펠 이름 비교
    public SpellData FindSpell(string text)
    {
        string key = Normalize(text);
        return spellDatabase.Find(s => s != null && Normalize(s.spellName) == key);
    }

    private static string Normalize(string s)
    {
        return string.IsNullOrEmpty(s) ? string.Empty : s.Replace(" ", "").Trim().ToLowerInvariant();
    }

    // =========================================================
    // [2] 해금 관리
    // =========================================================
    public bool IsUnlocked(SpellData spell) => spell != null && unlockedSpells.Contains(spell);

    // 드랍 아이템 획득 시 호출. 새로 해금되면 true
    public bool UnlockSpell(SpellData spell)
    {
        if (spell == null || !unlockedSpells.Add(spell)) return false;
        unlockOrder.Add(spell);
        SpellCollection.Add(spell); // 스펠 도감 누적 기록 (타이틀 화면 도감에 표시)

        Debug.Log($"[스펠 해금] 새로운 마법을 배웠습니다!: {spell.spellName} (Tier {spell.tier})");
        OnSpellUnlocked?.Invoke(spell);
        SpellsChanged?.Invoke();
        return true;
    }

    // 게임 시작/재시작 시 기본 스펠만 해금된 상태로 초기화
    public void ResetSpellUnlocks()
    {
        unlockedSpells.Clear();
        unlockOrder.Clear();
        foreach (SpellData spell in spellDatabase)
        {
            if (spell != null && spell.unlockedByDefault && unlockedSpells.Add(spell))
            {
                unlockOrder.Add(spell);
                SpellCollection.Add(spell);
            }
        }
        int defaultCount = unlockedSpells.Count;

        // 이전 회차에서 배운 스펠 이어받기 (배운 순서 그대로)
        if (carryOverLearnedSpells)
        {
            foreach (string name in SpellCollection.CollectedNames)
            {
                SpellData spell = spellDatabase.Find(s =>
                    s != null && (string.IsNullOrEmpty(s.spellName) ? s.name : s.spellName) == name);
                if (spell != null && unlockedSpells.Add(spell)) unlockOrder.Add(spell);
            }
        }

        Debug.Log($"[SkillManager] 스펠 해금 상태 초기화 완료 (기본 {defaultCount}개 + 이전 회차 {unlockedSpells.Count - defaultCount}개)");
        SpellsChanged?.Invoke();
    }

    // TODO(2회차): 세이브 데이터에서 해금 목록을 불러올 때 사용
    public void LoadUnlockedSpells(IEnumerable<string> spellNames)
    {
        foreach (string name in spellNames)
        {
            SpellData spell = FindSpell(name);
            if (spell != null && unlockedSpells.Add(spell))
            {
                unlockOrder.Add(spell);
                SpellCollection.Add(spell);
            }
        }
        SpellsChanged?.Invoke();
    }

    // =========================================================
    // [3] 드랍할 스펠 선택
    // =========================================================
    // 해당 속성에서 아직 배우지 않은 스펠 중 가중치 랜덤으로 하나 선택.
    // 약한 스펠(낮은 단계)일수록 가중치가 커서 자주 나옵니다. 남은 게 없으면 null
    public SpellData PickDropSpell(MagicElement element)
    {
        if (element == MagicElement.Normal) return null; // 노말 스펠은 기본 지급

        float totalWeight = 0f;
        List<SpellData> candidates = new List<SpellData>();
        foreach (SpellData spell in spellDatabase)
        {
            if (spell == null || spell.elementType != element || IsUnlocked(spell)) continue;
            candidates.Add(spell);
            totalWeight += spell.ResolveDropWeight();
        }
        if (candidates.Count == 0) return null;

        float roll = UnityEngine.Random.Range(0f, totalWeight);
        foreach (SpellData spell in candidates)
        {
            roll -= spell.ResolveDropWeight();
            if (roll <= 0f) return spell;
        }
        return candidates[candidates.Count - 1];
    }

    // 기존 GameManager.AddMonsterKill()에서 호출하던 함수.
    // 이제 해금은 '드랍 아이템 획득'으로만 일어나므로 아무 것도 하지 않습니다.
    // (GameManager의 호출 부분을 지운 뒤 이 함수도 삭제해도 됩니다)
    [Obsolete("해금은 DropItem 획득 시 UnlockSpell()로 처리됩니다.")]
    public void UnlockRandomSpell() { }

    // =========================================================
    // [4] 음성 연동 (Voice 폴더의 VoiceSocket, VoiceRecorder 가 사용)
    // =========================================================
    // 해금 목록이 바뀔 때 (해금, 초기화, 불러오기). VoiceSocket 가 서버로 스펠 목록을 다시 보낸다
    public event Action SpellsChanged;

    // 음성 판정 후보로 서버에 보낼 스펠 (unlockedOnly 면 해금된 것만)
    public List<SpellData> GetVoiceSpells(bool unlockedOnly = true)
    {
        return spellDatabase.FindAll(s => s != null && !string.IsNullOrWhiteSpace(s.spellName)
                                          && (!unlockedOnly || IsUnlocked(s)));
    }

    // 음성 판정 결과로 시전. 성공이면 null, 실패면 사유 (음성 쪽은 사유를 로그로만 남긴다)
    // power: 영창 보너스 (VoiceBonusCalculator.Evaluate 결과의 power)
    public string TryCastByVoice(string spellName, float power = 1f)
    {
        string fail = TryCastByVoiceInternal(spellName, power);
        if (fail != null) OnVoiceCastFailed?.Invoke(fail);
        return fail;
    }

    private string TryCastByVoiceInternal(string spellName, float power)
    {
        SpellData spell = FindSpell(spellName);
        if (spell == null) return "알 수 없는 주문";
        if (!IsUnlocked(spell)) return "아직 배우지 않은 주문";
        if (!CanCast) return "공격할 적이 없음";
        return CastSpell(spell.spellName, power) ? null : "시전 실패 (타겟 또는 이펙트 없음)";
    }
}