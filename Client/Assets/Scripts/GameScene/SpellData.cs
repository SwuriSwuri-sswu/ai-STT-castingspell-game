using UnityEngine;

public enum ElementType { Normal, Water, Fire, Grass }
public enum CastType { Projectile, TargetInstant } // 투사체형, 타겟 즉발형 추가

[CreateAssetMenu(fileName = "NewSpell", menuName = "Magic/Spell Data")]
public class SpellData : ScriptableObject
{
    public string spellName;
    public ElementType elementType;
    public CastType castType = CastType.Projectile; // 기본값은 투사체
    public float baseDamage;

    [Header("효과 프리팹 (타입에 맞게 채우세요)")]
    public GameObject projectilePrefab; // Projectile 타입일 때 날아갈 투사체
    public GameObject explosionPrefab;  // 적중/즉발 시 나타날 이펙트 (메테오, 장판 등)

    public bool isUnlocked = false;
}