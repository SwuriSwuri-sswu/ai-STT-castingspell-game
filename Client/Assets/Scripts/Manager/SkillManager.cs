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

    [Header("이펙트 크기·속도 (씬마다 다르게)")]
    [Tooltip("몹 위치에 터지는 이펙트(폭발·장판·차지 마무리) 크기 배율. 맵이 큰 BossScene은 1.8~2.2 추천, GameScene은 1")]
    [Min(0.1f)] public float effectScale = 1f;
    [Tooltip("지팡이에서 날아가는 투사체 크기 배율 (카메라 가까이에서 출발하므로 너무 키우면 화면을 가림. 1.2~1.5 정도)")]
    [Min(0.1f)] public float projectileScale = 1f;
    [Tooltip("투사체가 날아가는 속도 배율. 맵이 커서 마법이 늦게 닿으면 올리기")]
    [Min(0.1f)] public float projectileSpeedScale = 1f;

    // ProjectileController 등에서 쓰는 현재 씬의 배율 (SkillManager가 없으면 1)
    public static float EffectScale => Instance != null ? Instance.effectScale : 1f;

    // 데미지가 들어가는 순간 (EnemyController.TakeDamage에서 호출)
    //   주문마다 SpellData의 Impact Prefab을 몹 몸 중앙에 터뜨림 (비어 있으면 아무것도 안 함)
    public static void SpawnHitEffect(SpellData spell, Vector3 position)
    {
        SoundManager.PlayHit(spell);   // 적중 소리 (SpellData의 Hit Sound → 없으면 속성 기본)
        if (spell == null || spell.impactPrefab == null) return;
        EffectUtil.SpawnScaled(spell.impactPrefab, position + spell.impactOffset, Quaternion.identity, EffectScale * spell.impactScale);
    }

    [Header("시전 순간 카메라 움찔")]
    [Tooltip("세기 (0 = 끔, 1 = 보통). 강한 영창이면 영창 배율만큼 더 크게")]
    [Min(0f)] public float castKick = 1f;
    [Tooltip("움찔하는 시간 (초)")]
    public float castKickTime = 0.18f;

    [Header("장판·빔 연출 시간")]
    [Tooltip("장판·빔 마법의 Effect Duration이 0일 때 보여 줄 최대 시간 (초).\n" +
             "이 시간이 끝나면 이펙트를 멈추고 데미지 + 적중 폭발. 그동안은 다음 주문을 받지 않음")]
    [Min(0.1f)] public float areaEffectTime = 1.2f;
    [Tooltip("장판·빔이 끝난 뒤 남은 입자가 사라지는 최대 시간 (초). 길면 끝난 뒤에도 잔상이 오래 남음")]
    [Min(0f)] public float areaEffectTail = 0.3f;

    [Header("장판 바닥 맞추기")]
    [Tooltip("장판을 몹 발밑의 실제 바닥(레드카펫 등 Collider 윗면)에 맞춰 놓기")]
    public bool snapAreaToFloor = true;
    [Tooltip("바닥에서 살짝 띄우는 높이 (m). 장판이 바닥과 겹쳐 깜빡이면 조금 올리기")]
    [Min(0f)] public float areaFloorLift = 0.03f;
    [Tooltip("바닥을 찾을 때 몹 발밑에서 위로 몇 m부터 아래로 찾을지")]
    public float floorProbeHeight = 1f;

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
    // 도감·설정 창으로 일시정지 중이면 영창을 받지 않음
    public static bool CanCast => EnemyController.AnyReady && !GamePause.IsPaused && !CastBlocked;

    // 보스전처럼 '내 턴'에만 영창을 받아야 할 때 true (BossBattleDirector가 관리)
    public static bool CastBlocked { get; set; }
    // 영창이 막혀 있는 동안 자막에 보여 줄 안내 (비우면 "적이 다가오고 있습니다...")
    public static string BlockedHint { get; set; }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        // 이전 씬(보스전)에서 막아 둔 영창 잠금이 남지 않게
        CastBlocked = false;
        BlockedHint = null;
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
            case CastType.Beam: casted = CastBeam(spell, target, origin, power); break;
            default: casted = CastInstant(spell, target, power); break;
        }

        if (!casted) return false;

        // 데미지 예약: 이 마법으로 몹이 쓰러질 예정이면 그 즉시 영창이 막힌다 (이펙트 연출과 별개)
        if (target != null) target.ReserveDamage(spell, power);
        OnSpellCast?.Invoke(spell, target, power);
        SoundManager.PlayCast(spell);   // 시전 소리 (SpellData의 Cast Sound → 없으면 속성 기본)
        if (castKick > 0f) CameraKick.Kick(castKick * Mathf.Max(1f, power), castKickTime);   // 시전 순간 카메라 움찔 (강한 영창일수록 크게)

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
        EffectUtil.Scale(projectile, projectileScale);

        // 에셋 팩 이펙트에는 스크립트가 없을 수 있으므로 없으면 붙여줌
        ProjectileController controller = projectile.GetComponent<ProjectileController>();
        if (controller == null) controller = projectile.AddComponent<ProjectileController>();
        controller.speed *= projectileSpeedScale;
        controller.hitDistance *= Mathf.Max(1f, projectileScale);
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

        Vector3 spawnPos = FloorPoint(target) + Vector3.up * instantSpawnHeightOffset + spell.explosionOffset;
        float effectLength = 0f;
        if (spell.explosionPrefab != null)
        {
            GameObject area = Instantiate(spell.explosionPrefab, spawnPos, Quaternion.identity);
            EffectUtil.Scale(area, effectScale * spell.explosionScale);
            effectLength = AreaLength(spell, area);
            StartCoroutine(EndAreaEffect(area, effectLength));
        }

        // 기본값: 장판을 Area Effect Time(1.2초)만큼 보여 준 뒤 데미지 + 적중 폭발 → 사망 애니메이션
        target.ReceiveSpell(spell, spell.ResolveDamageDelay(effectLength), power);

        Debug.Log($"[마법 시전] {spell.spellName} 타겟 위치에 발동!");
        return true;
    }

    // 빔형: 지팡이 끝에서 몹을 향해 뿜어짐 (지팡이를 따라다니며 계속 몹을 겨눔) → 닿는 순간 데미지
    private bool CastBeam(SpellData spell, EnemyController target, Vector3 origin, float power)
    {
        if (spell.beamPrefab == null)
        {
            Debug.LogWarning($"[{spell.spellName}] beamPrefab이 비어 있습니다.");
            return false;
        }
        if (target == null)
        {
            Debug.Log($"[시전 실패] 타겟이 없어 {spell.spellName}을(를) 시전할 수 없습니다.");
            return false;
        }

        GameObject beam = Instantiate(spell.beamPrefab, origin, Quaternion.identity);
        Vector3 baseScale = beam.transform.localScale;

        // 길이를 늘이려면 파티클도 오브젝트 크기를 따르게
        if (spell.beamLength > 0f)
        {
            foreach (ParticleSystem ps in beam.GetComponentsInChildren<ParticleSystem>(true))
            {
                ParticleSystem.MainModule main = ps.main;
                main.scalingMode = ParticleSystemScalingMode.Hierarchy;
            }
        }

        // 연출 시간: Effect Duration → 없으면 Area Effect Time (단발 이펙트가 더 짧으면 그 길이)
        float length = AreaLength(spell, beam);

        AimBeam(beam, spell, target, baseScale);
        StartCoroutine(RunBeam(beam, spell, target, baseScale, length));

        // 데미지 (기본: 빔이 끝나는 순간 → 몹 위치 폭발)
        float delay = spell.ResolveDamageDelay(length);
        target.ReceiveSpell(spell, delay, power);
        if (spell.explosionPrefab != null) StartCoroutine(BeamImpact(spell, target, delay));

        Debug.Log($"[마법 시전] {spell.spellName} 빔 발사! ({length:F1}초)");
        return true;
    }

    private IEnumerator RunBeam(GameObject beam, SpellData spell, EnemyController target, Vector3 baseScale, float length)
    {
        for (float t = 0f; t < length && beam != null; t += Time.deltaTime)
        {
            AimBeam(beam, spell, target, baseScale);   // 지팡이가 휘둘러져도 끝을 따라가며 몹을 겨눔
            yield return null;
        }
        if (beam == null) yield break;
        float tail = EffectUtil.StopEmitting(beam);
        Destroy(beam, Mathf.Min(tail, areaEffectTail) + 0.05f);
    }

    // 몹 발밑의 실제 바닥 윗면 (레드카펫처럼 바닥보다 높은 Collider가 있으면 그 위). 못 찾으면 몹 발 높이
    private Vector3 FloorPoint(EnemyController target)
    {
        Vector3 feet = target.GroundPoint;
        if (!snapAreaToFloor) return feet;

        Vector3 from = feet + Vector3.up * floorProbeHeight;
        RaycastHit[] hits = Physics.RaycastAll(from, Vector3.down, floorProbeHeight + 2f, ~0, QueryTriggerInteraction.Ignore);
        float best = float.MaxValue;
        Vector3 point = feet;
        foreach (RaycastHit h in hits)
        {
            if (h.transform.IsChildOf(target.transform)) continue;   // 몹 자신의 Collider는 무시
            if (h.distance < best) { best = h.distance; point = h.point; }
        }
        return best < float.MaxValue ? point + Vector3.up * areaFloorLift : feet;
    }

    // 장판·빔 연출 시간: Effect Duration이 있으면 그 시간, 없으면 Area Effect Time (단발 이펙트가 더 짧으면 그 길이)
    private float AreaLength(SpellData spell, GameObject instance)
    {
        if (spell.effectDuration > 0f) return spell.effectDuration;
        if (IsLooping(instance)) return areaEffectTime;
        return Mathf.Min(EffectUtil.GetDuration(instance), areaEffectTime);
    }

    // 장판을 length초 보여 준 뒤 방출을 멈추고, 남은 입자는 Area Effect Tail 안에 정리
    private IEnumerator EndAreaEffect(GameObject area, float length)
    {
        yield return new WaitForSeconds(length);
        if (area == null) yield break;
        float tail = EffectUtil.StopEmitting(area);
        Destroy(area, Mathf.Min(tail, areaEffectTail) + 0.05f);
    }

    // 지팡이 끝에 두고 몹 몸 중앙을 향하게 + (Beam Length가 있으면) 거리에 맞춰 길이 조절
    private void AimBeam(GameObject beam, SpellData spell, EnemyController target, Vector3 baseScale)
    {
        Vector3 from = wandTip != null ? wandTip.position : transform.position;
        beam.transform.position = from;
        if (target == null || target.IsDying) return;

        Vector3 to = target.AimPoint;
        Vector3 dir = to - from;
        if (dir.sqrMagnitude < 0.0001f) return;
        Quaternion offset = Quaternion.Euler(spell.beamRotationOffset);
        beam.transform.rotation = Quaternion.LookRotation(dir.normalized) * offset;

        if (spell.beamLength > 0f)
        {
            // 에셋에서 빔이 뻗는 축 = 회전 보정을 되돌렸을 때의 앞(Z) 방향
            Vector3 axis = Quaternion.Inverse(offset) * Vector3.forward;
            Vector3 a = new Vector3(Mathf.Abs(axis.x), Mathf.Abs(axis.y), Mathf.Abs(axis.z));
            int i = a.x >= a.y && a.x >= a.z ? 0 : (a.y >= a.z ? 1 : 2);
            Vector3 scale = baseScale;
            scale[i] = baseScale[i] * dir.magnitude / spell.beamLength;
            beam.transform.localScale = scale;
        }
    }

    private IEnumerator BeamImpact(SpellData spell, EnemyController target, float delay)
    {
        if (delay > 0f) yield return new WaitForSeconds(delay);
        if (target == null) yield break;
        EffectUtil.SpawnScaled(spell.explosionPrefab, target.AimPoint + spell.explosionOffset, Quaternion.identity, effectScale * spell.explosionScale);
    }

    private static bool IsLooping(GameObject go)
    {
        foreach (ParticleSystem ps in go.GetComponentsInChildren<ParticleSystem>(true))
            if (ps.main.loop) return true;
        return false;
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
        GameObject charge = Instantiate(spell.chargePrefab, origin, parent.rotation, parent);   // 지팡이 끝(카메라 앞)이라 배율 적용 안 함

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
            EffectUtil.SpawnScaled(spell.explosionPrefab, target.AimPoint + spell.explosionOffset, Quaternion.identity, effectScale * spell.explosionScale);
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
        if (DemoMode.On) { DemoUnlocks(); return; }
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

    // [시연 모드] 게임씬: 스펠 하나만 빼고 전부 해금 (그 하나는 첫 몹이 드랍) / 보스씬: 전부 해금
    private void DemoUnlocks()
    {
        SpellData locked = DemoMode.IsGameScene(gameObject.scene.name) ? DemoMode.LockedSpell(spellDatabase) : null;
        SpellCollection.Clear();
        foreach (SpellData spell in spellDatabase)
        {
            if (spell == null || spell == locked || !unlockedSpells.Add(spell)) continue;
            unlockOrder.Add(spell);
            SpellCollection.Add(spell);
        }
        Debug.Log($"[Demo] 스펠 {unlockedSpells.Count}개 해금" + (locked != null ? $" (잠금: {locked.spellName} → 첫 몹이 드랍)" : ""));
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

    // TryCastByVoice 실패 사유 (VoiceRecorder가 플레이어용 문구로 바꿔 표시)
    public const string FailUnknown = "UNKNOWN_SPELL";
    public const string FailNotLearned = "NOT_LEARNED";
    public const string FailNoTarget = "NO_TARGET";
    public const string FailCast = "CAST_FAILED";   // 타겟 또는 이펙트 없음

    private string TryCastByVoiceInternal(string spellName, float power)
    {
        SpellData spell = FindSpell(spellName);
        if (spell == null) return FailUnknown;
        if (!IsUnlocked(spell)) return FailNotLearned;
        if (!CanCast) return FailNoTarget;
        return CastSpell(spell.spellName, power) ? null : FailCast;
    }
}