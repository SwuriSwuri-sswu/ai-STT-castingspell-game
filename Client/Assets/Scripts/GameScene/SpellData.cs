using UnityEngine;

public enum MagicElement { Normal, Water, Fire, Grass }

[CreateAssetMenu(fileName = "NewSpell", menuName = "Magic/Spell Data")]
public class SpellData : ScriptableObject
{
    [Header("Basic Info")]
    public string spellName;          // STT와 정확히 일치해야 할 이름 (예: "차가운 소나기")
    public MagicElement elementType;  // 마법 속성
    public int tier;                  // 마법 단계 (1~3)
    public bool isUnlocked = false;   // 해금 여부 (기본 노말 3종만 true로 체크)

    [Header("Combat Stats")]
    public float baseDamage;          // 기본 데미지

    [Header("Visual Effects")]
    public GameObject projectilePrefab; // 날아가는 이펙트 프리팹
    public GameObject explosionPrefab;  // 적중 시 터지는 이펙트 프리팹
}