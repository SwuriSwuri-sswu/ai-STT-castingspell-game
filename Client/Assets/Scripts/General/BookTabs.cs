using UnityEngine;
using UnityEngine.UI;

// 도감 탭 전환 (BookPanel에 붙이기)
// 스펠 버튼 → 스펠 도감 / 몬스터 버튼 → 몹 도감. 선택된 탭 버튼은 밝게, 나머지는 어둡게
// 칸을 비워 두면 자식 중 SpellPanel / MonsterPanel / Spell_Button / Monster_Button을 이름으로 찾음
public class BookTabs : MonoBehaviour
{
    [Header("페이지")]
    public GameObject spellPage;
    public GameObject monsterPage;

    [Header("탭 버튼")]
    public Button spellButton;
    public Button monsterButton;

    [Header("표시")]
    [Tooltip("도감을 열 때 마지막으로 보던 탭을 다시 보여 줌 (끄면 항상 스펠 탭부터)")]
    public bool rememberLastTab = false;
    public Color selectedColor = Color.white;
    public Color unselectedColor = new Color(0.65f, 0.65f, 0.65f);

    private bool showingMonsters;
    private bool wired;

    private void Awake() => Wire();

    private void OnEnable()
    {
        Wire();
        if (rememberLastTab && showingMonsters) ShowMonsters();
        else ShowSpells();
    }

    private void Wire()
    {
        if (wired) return;
        wired = true;

        if (spellPage == null) spellPage = FindChild("SpellPanel");
        if (monsterPage == null) monsterPage = FindChild("MonsterPanel");
        if (spellButton == null) spellButton = FindChild("Spell_Button")?.GetComponent<Button>();
        if (monsterButton == null) monsterButton = FindChild("Monster_Button")?.GetComponent<Button>();

        if (spellButton != null) spellButton.onClick.AddListener(ShowSpells);
        if (monsterButton != null) monsterButton.onClick.AddListener(ShowMonsters);
    }

    public void ShowSpells() => Show(false);
    public void ShowMonsters() => Show(true);

    private void Show(bool monsters)
    {
        showingMonsters = monsters;
        if (spellPage != null) spellPage.SetActive(!monsters);
        if (monsterPage != null) monsterPage.SetActive(monsters);
        Highlight(spellButton, !monsters);
        Highlight(monsterButton, monsters);
    }

    // 선택된 탭은 밝게, 나머지는 어둡게 (크기는 그대로)
    private void Highlight(Button button, bool selected)
    {
        if (button == null) return;
        if (button.targetGraphic != null) button.targetGraphic.color = selected ? selectedColor : unselectedColor;
    }

    private GameObject FindChild(string childName)
    {
        foreach (Transform t in GetComponentsInChildren<Transform>(true))
        {
            if (t.name == childName) return t.gameObject;
        }
        return null;
    }
}