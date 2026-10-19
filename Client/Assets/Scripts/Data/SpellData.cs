using UnityEngine;
using UnityEngine.Serialization;

public enum MagicElement { Normal, Water, Fire, Grass }

// 마법 연출 방식
// Projectile    : 지팡이 끝에서 투사체 발사 → 몹에 닿으면 폭발 이펙트
// TargetInstant : 몹 위치에 장판/즉발 이펙트 바로 생성
// WandCharge    : 지팡이 끝에 차지 이펙트가 머물렀다가, 한 번 끝나면 몹에게 데미지
public enum CastType { Projectile, TargetInstant, WandCharge }

[CreateAssetMenu(fileName = "NewSpell", menuName = "Magic/Spell Data")]
public class SpellData : ScriptableObject
{
    [Header("Basic Info")]
    [Tooltip("STT 결과와 비교할 이름. 띄어쓰기·대소문자는 무시하고 비교합니다.")]
    public string spellName;
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

    [Tooltip("폭발/장판 이펙트 연출 시간(초). 0이면 파티클 길이로 자동 계산합니다.")]
    [Min(0f)] public float effectDuration = 0f;

    [Tooltip("이펙트가 시작된 뒤 데미지가 들어가기까지의 시간(초).\n" +
             "-1 = 자동 (투사체형: 닿는 순간 / 즉발형·차지형: 이펙트가 끝날 때)")]
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
             "WandCharge: (선택) 차지가 끝나는 순간 몹 위치에 터질 이펙트. 없으면 비워두기")]
    public GameObject explosionPrefab;

    // 실제로 적용될 데미지 지연 시간 계산
    public float ResolveDamageDelay(float effectLength)
    {
        if (damageDelay >= 0f) return damageDelay;
        return castType == CastType.Projectile ? 0f : effectLength;
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