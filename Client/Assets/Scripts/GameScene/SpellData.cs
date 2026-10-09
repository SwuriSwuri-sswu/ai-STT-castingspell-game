using UnityEngine;

public enum ElementType { Normal, Water, Fire, Grass }

[CreateAssetMenu(fileName = "NewSpell", menuName = "Magic/Spell Data")]
public class SpellData : ScriptableObject
{
    public string spellName;          // STT와 정확히 일치해야 할 이름 (예: "아쿠아 스플래시")
    public ElementType elementType;   // 속성
    public float baseDamage;          // 기본 데미지
    public GameObject projectilePrefab; // 날아가는 이펙트 프리팹
    public GameObject explosionPrefab;  // 적중 시 터지는 이펙트 프리팹
    public bool isUnlocked = false;   // 해금 여부 (기본 노말 스펠 2개만 Inspector에서 true로 체크)
}