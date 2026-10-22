using UnityEngine;
using UnityEngine.Serialization;

public enum MagicElement { Normal, Water, Fire, Grass }

// 마법 연출 방식
// Projectile    : 지팡이 끝에서 투사체 발사 → 몹에 닿으면 폭발 이펙트
// TargetInstant : 몹 위치에 장판/즉발 이펙트 바로 생성
// WandCharge    : 지팡이 끝에 차지 이펙트가 머물렀다가, 한 번 끝나면 몹에게 데미지
// Beam          : 지팡이 끝에서 몹을 향해 뿜어져 나가는 빔·불기둥 (지팡이를 따라다니며 몹을 계속 겨눔)
public enum CastType { Projectile, TargetInstant, WandCharge, Beam }

[CreateAssetMenu(fileName = "NewSpell", menuName = "Magic/Spell Data")]
public class SpellData : ScriptableObject
{
    [Header("Basic Info")]
    [Tooltip("STT 결과와 비교할 이름. 띄어쓰기·대소문자는 무시하고 비교합니다.")]
    public string spellName;
    [Tooltip("도감에 함께 표시할 발음 로마자 (예: 활활 → hwalhwal). 비워 두면 이름만 표시합니다.")]
    public string pronunciation;
    public MagicElement elementType;

    [Tooltip("마법 단계 (노말 기본 스펠은 0 또는 1, 속성 스펠은 1~3)")]
    [Range(0, 3)] public int tier;

    // 기존 에셋의 isUnlocked 체크 값을 그대로 이어받습니다.
    // 이 값은 '게임 시작 시 기본으로 해금되어 있는가'만 의미하고,
    // 플레이 중 해금 상태는 SkillManager가 따로 관리합니다. (에셋 파일이 바뀌지 않도록)
    [FormerlySerializedAs("isUnlocked")]
    [Tooltip("게임 시작 시 기본 해금 여부 (노말 기본 스펠만 체크)")]
    public bool unlockedByDefault = false;

    [Header("Combat Stats")]
    public float baseDamage;

    [Header("Cast")]
    public CastType castType = CastType.Projectile;

    [Tooltip("폭발/장판/빔 이펙트 연출 시간(초).\n" +
             "0 = 자동 (투사체·차지: 파티클 길이 / 장판·빔: SkillManager의 Area Effect Time, 기본 1.2초)\n" +
             "장판·빔은 이 시간이 끝나는 순간 데미지 + 적중 폭발")]
    [Min(0f)] public float effectDuration = 0f;

    [Tooltip("이펙트가 시작된 뒤 데미지가 들어가기까지의 시간(초).\n" +
             "-1 = 자동 (투사체형: 닿는 순간 / 장판·차지·빔: 이펙트가 끝날 때)")]
    public float damageDelay = -1f;

    [Header("Drop")]
    [Tooltip("드랍 가중치. 0이면 단계(tier)에 따라 자동 (1단계 60 / 2단계 30 / 3단계 10)")]
    [Min(0f)] public float dropWeight = 0f;

    [Header("Visual Effects")]
    [Tooltip("Projectile 타입일 때 날아갈 투사체")]
    public GameObject projectilePrefab;
    [Tooltip("WandCharge 타입일 때 지팡이 끝에 머무를 차지 이펙트")]
    public GameObject chargePrefab;
    [Tooltip("Projectile: 적중 시 폭발 / TargetInstant: 몹 위치의 장판 이펙트\n" +
             "WandCharge: (선택) 차지가 끝나는 순간 몹 위치에 터질 이펙트. 없으면 비워두기\n" +
             "Beam: (선택) 빔이 몹에 닿는 순간 몹 위치에 터질 이펙트")]
    public GameObject explosionPrefab;
    [Tooltip("Explosion Prefab 크기 배율 (투사체 폭발 / 장판 / 차지·빔 끝 폭발). 에셋이 작으면 키우기\n" +
             "플레이 중에 바꿔도 값이 남으니 몹을 보면서 맞추면 편함")]
    [Min(0.01f)] public float explosionScale = 1f;
    [Tooltip("Explosion Prefab 위치 보정 (m). 폭발이 너무 위에서 터지면 Y를 음수로 (예: -0.5)\n" +
             "장판형은 장판 위치도 같이 움직임")]
    public Vector3 explosionOffset = Vector3.zero;

    [Header("Hit Explosion (데미지가 들어가는 순간)")]
    [Tooltip("이 마법 전용 적중 폭발. 데미지가 들어가는 순간 몹 몸 중앙에서 터짐 (모든 타입 공통).\n" +
             "장판만 있는 에셋은 여기에 폭발을 넣기. 비워 두면 추가 폭발 없음\n" +
             "※ 투사체형은 Explosion Prefab이 이미 적중 폭발이라 보통 비워 둠")]
    public GameObject impactPrefab;
    [Tooltip("적중 폭발 크기 배율 (SkillManager의 Effect Scale에 곱함)")]
    [Min(0.01f)] public float impactScale = 1f;
    [Tooltip("적중 폭발 위치 보정 (m). 몹 몸 중앙 기준, 아래로 내리려면 Y를 음수로")]
    public Vector3 impactOffset = Vector3.zero;

    [Header("Sound (비우면 SoundLibrary의 속성 기본 소리)")]
    [Tooltip("시전하는 순간 소리 (지팡이에서 마법이 나갈 때)")]
    public AudioClip castSound;
    [Tooltip("데미지가 들어가는 순간 소리 (적중 폭발)")]
    public AudioClip hitSound;

    [Header("Beam (빔·불기둥)")]
    [Tooltip("Beam 타입일 때 지팡이 끝에서 뿜어질 이펙트. 루프 이펙트면 Effect Duration 동안 뿜음 (0이면 1.5초)")]
    public GameObject beamPrefab;
    [Tooltip("에셋 원래 빔 길이(m). 몹까지 거리에 맞춰 이 길이를 늘이거나 줄임. 0 = 원래 길이 그대로")]
    [Min(0f)] public float beamLength = 0f;
    [Tooltip("빔이 몹 쪽으로 뻗지 않으면 돌려 주는 각도. 위(Y)로 뻗는 에셋이면 X 90, 옆(X)으로 뻗으면 Y -90 등")]
    public Vector3 beamRotationOffset = Vector3.zero;

    // 실제로 적용될 데미지 지연 시간 계산
    public float ResolveDamageDelay(float effectLength)
    {
        if (damageDelay >= 0f) return damageDelay;
        if (castType == CastType.Projectile) return 0f;
        return effectLength;
    }

    // 실제로 적용될 드랍 가중치
    public float ResolveDropWeight()
    {
        if (dropWeight > 0f) return dropWeight;
        switch (tier)
        {
            case 1: return 60f;
            case 2: return 30f;
            case 3: return 10f;
            default: return 60f;
        }
    }
}