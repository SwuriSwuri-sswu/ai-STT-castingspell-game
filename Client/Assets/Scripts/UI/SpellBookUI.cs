using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// 스펠 도감 (SpellPanel에 붙이기)
// 패널이 열릴 때마다 배운 스펠을 속성 → 단계(1/2/3) 칸에 배운 순서대로 채웁니다.
//   - 게임 씬 (SkillManager 있음): 이번 판에서 배운 스펠
//   - 타이틀 씬 (SkillManager 없음): 지금까지 한 번이라도 배운 스펠 (SpellCollection 누적 기록)
//
// 칸 자동 인식: 각 속성 묶음(Normal / Fire / Water / Grass) 아래에서
//   이름에 "Spell_Tier1" / "Spell_Tier2" / "Spell_Tier3"이 들어간 오브젝트를 Hierarchy 순서대로 찾습니다.
//   (대소문자 무시: Spell_TIer1도 인식. 이름 앞부분은 상관없음)
// 각 칸 안에 글자(TextMeshPro)가 없으면 Label Template을 복제해서 자동으로 만들어 줍니다.
public class SpellBookUI : MonoBehaviour
{
    [Serializable]
    public class ElementSection
    {
        public MagicElement element;
        [Tooltip("이 속성 칸들이 들어 있는 부모 (예: SpellPanel/Fire)")]
        public Transform root;
        [Tooltip("(선택) 수집 현황 '2 / 7'을 표시할 글자")]
        public TMP_Text countText;

        [NonSerialized] public List<Image>[] slots;
    }

    public enum BookSource
    {
        Auto,        // 게임 씬이면 이번 판, 타이틀 씬이면 누적 기록
        CurrentRun,  // 이번 판에서 배운 스펠
        Collection,  // 지금까지 한 번이라도 배운 스펠 (누적)
    }

    [Header("보여 줄 기록")]
    public BookSource source = BookSource.Auto;
    [Tooltip("전체 스펠 목록. 타이틀 씬처럼 SkillManager가 없는 씬에서는 꼭 넣어 주세요.\n" +
             "(Project 창 검색에 t:SpellData → 전부 선택 → 이 목록 이름 위로 드래그)\n" +
             "SkillManager가 있는 씬에서는 비워 두면 SkillManager의 목록을 사용합니다.")]
    public List<SpellData> allSpells = new List<SpellData>();

    [Header("속성 묶음 (Normal / Fire / Water / Grass)")]
    public List<ElementSection> sections = new List<ElementSection>();

    [Header("칸 안의 글자")]
    [Tooltip("칸 안에 넣을 글자의 견본. 폰트·크기·외곽선을 여기서 꾸미면 모든 칸에 똑같이 복제됩니다.\n" +
             "(씬에 하나 만들어 두고 꺼 두세요)")]
    public TMP_Text labelTemplate;

    [Header("표시 방식")]
    [Tooltip("아직 못 배운 스펠을 '???'로 표시 (끄면 빈칸)")]
    public bool showLockedAsUnknown = true;
    public string unknownText = "???";
    [Tooltip("그 단계에 스펠이 2개뿐이면 3번째 칸은 숨김 → 칸 개수 = 실제 스펠 개수")]
    public bool hideUnusedSlots = true;

    [Header("색")]
    [Tooltip("배운 스펠 이름 (양피지 위에서 잘 보이는 진한 갈색 추천)")]
    public Color learnedColor = new Color(0.29f, 0.18f, 0.10f);
    [Tooltip("못 배운 스펠 '???'")]
    public Color lockedColor = new Color(0.29f, 0.18f, 0.10f, 0.35f);
    [Tooltip("못 배운 칸의 바(이미지)를 흐리게")]
    [Range(0f, 1f)] public float lockedSlotAlpha = 0.55f;
    [Tooltip("지난번에 도감을 연 뒤 새로 배운 스펠")]
    public Color newColor = new Color(0.85f, 0.35f, 0.05f);

    [Header("새 스펠 강조")]
    [Tooltip("새로 배운 스펠 칸이 통통 튀는 연출")]
    public bool popNewSlots = true;
    public float popScale = 1.15f;
    public float popDuration = 0.35f;

    // 도감에서 이미 확인한 스펠 (패널을 닫을 때 '확인함'으로 처리)
    private readonly HashSet<SpellData> seen = new HashSet<SpellData>();
    private readonly List<SpellData> newlyShown = new List<SpellData>();
    private bool initialized;

    // 견본 글자가 '칸 기준으로' 어디에 어떤 크기로 놓여 있는지 (모든 칸에 똑같이 적용)
    private bool hasTemplateLayout;
    private Vector2 templateOffset;   // 칸 중심 기준 위치 (칸의 로컬 단위)
    private Vector2 templateSize;     // 견본 크기 (견본 자신의 로컬 단위)
    private Vector3 templateScale;    // 칸 안에서 견본과 똑같이 보이게 할 배율
    private readonly Dictionary<Transform, Vector3> baseScales = new Dictionary<Transform, Vector3>();

    private void OnEnable()
    {
        if (!initialized) Initialize();
        Refresh();

        SkillManager sm = SkillManager.Instance;
        if (sm != null) sm.SpellsChanged += Refresh; // 열려 있는 동안 배우면 바로 반영
    }

    private void OnDisable()
    {
        SkillManager sm = SkillManager.Instance;
        if (sm != null) sm.SpellsChanged -= Refresh;

        // 튀는 연출 도중에 닫혀도 칸 크기가 원래대로 돌아오게
        StopAllCoroutines();
        foreach (KeyValuePair<Transform, Vector3> pair in baseScales)
        {
            if (pair.Key != null) pair.Key.localScale = pair.Value;
        }

        // 한 번 본 스펠은 다음부터 NEW 아님
        foreach (SpellData s in newlyShown) seen.Add(s);
        if (UseCollection) SpellCollection.MarkSeen(newlyShown);
        newlyShown.Clear();
    }

    private void Initialize()
    {
        initialized = true;
        AutoWire();

        foreach (ElementSection section in sections)
        {
            section.slots = new List<Image>[3];
            for (int t = 0; t < 3; t++) section.slots[t] = new List<Image>();
            if (section.root == null) continue;

            // Hierarchy 순서대로 칸 수집
            foreach (Transform child in section.root.GetComponentsInChildren<Transform>(true))
            {
                for (int t = 0; t < 3; t++)
                {
                    if (child.name.IndexOf($"Spell_Tier{t + 1}", StringComparison.OrdinalIgnoreCase) < 0) continue;
                    Image img = child.GetComponent<Image>();
                    if (img == null) continue;
                    section.slots[t].Add(img);
                    baseScales[img.transform] = img.transform.localScale;
                }
            }
        }

        // 견본이 놓인 칸을 찾아, 그 칸 기준 위치·크기를 기억 (끄기 전에 측정)
        MeasureTemplate();
        if (labelTemplate != null) labelTemplate.gameObject.SetActive(false);

        // 게임 시작 때 기본으로 주는 스펠은 NEW로 표시하지 않음
        foreach (SpellData s in AllSpells())
        {
            if (s != null && s.unlockedByDefault) seen.Add(s);
        }

        int slotCount = 0;
        foreach (ElementSection section in sections)
        {
            if (section.slots != null) foreach (List<Image> l in section.slots) slotCount += l.Count;
        }
        Debug.Log($"[SpellBookUI] 칸 {slotCount}개 인식, 전체 스펠 {AllSpells().Count}개, 표시 기록: {(UseCollection ? "누적" : "이번 판")}");
    }

    // Inspector의 Sections / Label Template이 비어 있으면 자식 이름으로 자동 연결
    //   속성 묶음: 이름이 Normal / Fire / Water / Grass인 자식
    //   수집 현황: 그 안에서 이름이 "_Count"로 끝나는 글자
    //   견본 글자: 이름에 "Template"이 들어간 글자
    private void AutoWire()
    {
        if (sections == null) sections = new List<ElementSection>();
        if (sections.Count == 0)
        {
            foreach (MagicElement e in (MagicElement[])Enum.GetValues(typeof(MagicElement)))
            {
                Transform root = FindChildByName(transform, e.ToString());
                if (root == null) continue;
                ElementSection section = new ElementSection { element = e, root = root };
                foreach (TMP_Text t in root.GetComponentsInChildren<TMP_Text>(true))
                {
                    if (t.name.EndsWith("_Count", StringComparison.OrdinalIgnoreCase)) { section.countText = t; break; }
                }
                sections.Add(section);
            }
            if (sections.Count > 0) Debug.Log($"[SpellBookUI] Sections가 비어 있어 자식에서 {sections.Count}개 속성 묶음을 자동으로 찾았습니다.");
            else Debug.LogWarning("[SpellBookUI] Sections가 비어 있고, Normal / Fire / Water / Grass 자식도 찾지 못했습니다.");
        }

        if (labelTemplate == null)
        {
            foreach (TMP_Text t in GetComponentsInChildren<TMP_Text>(true))
            {
                if (t.name.IndexOf("Template", StringComparison.OrdinalIgnoreCase) >= 0) { labelTemplate = t; break; }
            }
        }
    }

    private static Transform FindChildByName(Transform parent, string childName)
    {
        foreach (Transform child in parent)
        {
            if (string.Equals(child.name, childName, StringComparison.OrdinalIgnoreCase)) return child;
        }
        return null;
    }

    private bool UseCollection =>
        source == BookSource.Collection || (source == BookSource.Auto && SkillManager.Instance == null);

    // 전체 스펠 목록 (Inspector 목록 → 없으면 SkillManager 목록)
    private List<SpellData> AllSpells()
    {
        if (allSpells != null && allSpells.Count > 0) return allSpells;
        SkillManager sm = SkillManager.Instance;
        return sm != null ? sm.spellDatabase : new List<SpellData>();
    }

    // 배운 스펠 (배운 순서대로)
    private List<SpellData> LearnedInOrder(List<SpellData> all)
    {
        List<SpellData> result = new List<SpellData>();
        if (UseCollection)
        {
            // 누적 기록은 이름으로 저장되어 있으므로 전체 목록에서 찾아서 연결
            foreach (string name in SpellCollection.CollectedNames)
            {
                foreach (SpellData s in all)
                {
                    if (s == null || result.Contains(s)) continue;
                    string key = string.IsNullOrEmpty(s.spellName) ? s.name : s.spellName;
                    if (key == name) { result.Add(s); break; }
                }
            }
            // 기본 스펠은 기록이 없어도 배운 것으로
            foreach (SpellData s in all)
            {
                if (s != null && s.unlockedByDefault && !result.Contains(s)) result.Insert(0, s);
            }
        }
        else if (SkillManager.Instance != null)
        {
            result.AddRange(SkillManager.Instance.UnlockedInOrder);
        }
        return result;
    }

    [ContextMenu("스펠 도감 누적 기록 초기화 (테스트용)")]
    private void ClearCollection()
    {
        SpellCollection.Clear();
        seen.Clear();
        if (isActiveAndEnabled) Refresh();
    }

    public void Refresh()
    {
        List<SpellData> allList = AllSpells();
        List<SpellData> learnedList = LearnedInOrder(allList);

        foreach (ElementSection section in sections)
        {
            if (section.slots == null) continue;

            int learnedCount = 0, totalCount = 0;

            for (int t = 0; t < 3; t++)
            {
                int tier = t + 1;

                // 이 속성·단계의 전체 스펠 / 배운 스펠(배운 순서대로)
                List<SpellData> all = new List<SpellData>();
                List<SpellData> learned = new List<SpellData>();
                foreach (SpellData s in allList)
                {
                    if (s != null && s.elementType == section.element && TierOf(s) == tier && !all.Contains(s)) all.Add(s);
                }
                foreach (SpellData s in learnedList)
                {
                    if (s != null && s.elementType == section.element && TierOf(s) == tier) learned.Add(s);
                }
                totalCount += all.Count;
                learnedCount += learned.Count;

                List<Image> slots = section.slots[t];
                for (int i = 0; i < slots.Count; i++)
                {
                    Image slot = slots[i];
                    bool used = !hideUnusedSlots || i < all.Count;
                    slot.gameObject.SetActive(used);
                    if (!used) continue;

                    TMP_Text label = GetOrCreateLabel(slot);

                    if (i < learned.Count)
                    {
                        SpellData spell = learned[i];
                        bool isNew = UseCollection ? !SpellCollection.IsSeen(spell) && !spell.unlockedByDefault
                                                   : !seen.Contains(spell);
                        SetSlotAlpha(slot, 1f);
                        if (label != null)
                        {
                            label.text = spell.spellName;
                            label.color = isNew ? newColor : learnedColor;
                        }
                        if (isNew && !newlyShown.Contains(spell))
                        {
                            newlyShown.Add(spell);
                            if (popNewSlots && gameObject.activeInHierarchy) StartCoroutine(Pop(slot.transform));
                        }
                    }
                    else
                    {
                        SetSlotAlpha(slot, lockedSlotAlpha);
                        if (label != null)
                        {
                            label.text = (showLockedAsUnknown && i < all.Count) ? unknownText : "";
                            label.color = lockedColor;
                        }
                    }
                }
            }

            if (section.countText != null)
            {
                section.countText.text = $"{learnedCount} / {totalCount}";
            }
        }
    }

    // 노말 기본 스펠처럼 단계가 0인 것은 1단계 칸에 넣음
    private static int TierOf(SpellData s) => Mathf.Clamp(s.tier, 1, 3);

    private static void SetSlotAlpha(Image slot, float alpha)
    {
        Color c = slot.color;
        c.a = alpha;
        slot.color = c;
    }

    private TMP_Text GetOrCreateLabel(Image slot)
    {
        TMP_Text label = slot.GetComponentInChildren<TMP_Text>(true);
        if (label != null)
        {
            label.gameObject.SetActive(true);
            return label;
        }
        if (labelTemplate == null)
        {
            Debug.LogWarning("[SpellBookUI] Label Template이 비어 있어 칸에 글자를 만들 수 없습니다.");
            return null;
        }

        label = Instantiate(labelTemplate, slot.transform);
        label.name = "Label";
        label.gameObject.SetActive(true);

        RectTransform rt = label.rectTransform;
        rt.localRotation = Quaternion.identity;

        if (hasTemplateLayout)
        {
            // 견본을 놓아 둔 칸과 똑같은 위치·크기·글자 크기로
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.localScale = templateScale;
            rt.sizeDelta = templateSize;
            rt.anchoredPosition = templateOffset;
        }
        else
        {
            // 견본이 어느 칸 위에도 없으면 칸 크기에 꽉 맞추기
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            rt.localScale = Vector3.one;
        }
        return label;
    }

    // 견본 글자의 중심이 올라가 있는 칸을 찾아서, 그 칸 기준 위치·크기·배율을 계산
    private void MeasureTemplate()
    {
        hasTemplateLayout = false;
        if (labelTemplate == null) return;

        RectTransform tpl = labelTemplate.rectTransform;
        Vector3 worldCenter = tpl.TransformPoint(tpl.rect.center);

        // 견본 중심이 실제로 '올라가 있는' 칸만 기준으로 사용 (가까운 칸으로 추측하지 않음)
        RectTransform best = null;
        foreach (ElementSection section in sections)
        {
            if (section.slots == null) continue;
            foreach (List<Image> list in section.slots)
            {
                foreach (Image img in list)
                {
                    RectTransform slotRT = (RectTransform)img.transform;
                    Vector2 local = slotRT.InverseTransformPoint(worldCenter);
                    if (slotRT.rect.Contains(local)) { best = slotRT; break; }
                }
                if (best != null) break;
            }
            if (best != null) break;
        }

        if (best == null)
        {
            Debug.LogWarning("[SpellBookUI] 견본 글자(Label Template)가 어느 칸 위에도 올라가 있지 않습니다.\n" +
                             "견본을 아무 스펠 칸 위에 겹쳐 두세요. 지금은 글자를 칸 크기에 꽉 맞춰 만듭니다.");
            return;
        }

        Vector2 centerInSlot = best.InverseTransformPoint(worldCenter);
        templateOffset = centerInSlot - best.rect.center;
        templateSize = tpl.rect.size;
        float slotScale = best.lossyScale.x != 0f ? best.lossyScale.x : 1f;
        templateScale = Vector3.one * (tpl.lossyScale.x / slotScale);
        hasTemplateLayout = true;
        Debug.Log($"[SpellBookUI] 견본 기준 칸: {best.name}");
    }

    private IEnumerator Pop(Transform target)
    {
        Vector3 baseScale = baseScales.TryGetValue(target, out Vector3 s0) ? s0 : target.localScale;
        float t = 0f;
        while (t < popDuration)
        {
            t += Time.unscaledDeltaTime; // 일시정지 중에도 재생
            float k = Mathf.Sin(Mathf.Clamp01(t / popDuration) * Mathf.PI);
            target.localScale = baseScale * Mathf.Lerp(1f, popScale, k);
            yield return null;
        }
        target.localScale = baseScale;
    }
}