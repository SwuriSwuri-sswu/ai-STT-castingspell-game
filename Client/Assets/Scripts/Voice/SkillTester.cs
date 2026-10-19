using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class SkillTester : MonoBehaviour
{
    Vector2 scroll;
    int tab;                      // 0 = All, 1~ = 각 속성
    string[] tabLabels;
    MagicElement[] elements;
    List<SpellData> sorted;       // 속성 → 단계 → 이름 순

    void Start()
    {
        var sm = SkillManager.Instance;
        if (sm == null)
        {
            Debug.LogWarning("[SkillTester] 씬에 SkillManager가 없습니다.");
            return;
        }

        foreach (var s in sm.spellDatabase)
            if (s != null) sm.UnlockSpell(s);   // 전부 해금 (종료 시 기록 자동 삭제)

        sorted = sm.spellDatabase.Where(s => s != null)
                                 .OrderBy(s => s.elementType)
                                 .ThenBy(s => s.tier)
                                 .ThenBy(s => s.spellName)
                                 .ToList();

        // 탭 이름: MagicElement에서 자동 생성 (속성이 추가되면 탭도 자동 추가)
        elements = (MagicElement[])Enum.GetValues(typeof(MagicElement));
        tabLabels = new string[elements.Length + 1];
        tabLabels[0] = $"All ({sorted.Count})";
        for (int i = 0; i < elements.Length; i++)
        {
            MagicElement e = elements[i];
            tabLabels[i + 1] = $"{e} ({sorted.Count(s => s.elementType == e)})";
        }
    }

    // 플레이 종료 또는 씬 전환 시: 테스트로 쌓인 도감 기록 삭제 → GameScene에 영향 없음
    void OnDestroy()
    {
        SpellCollection.Clear();
        Time.timeScale = 1f;
    }

    void OnGUI()
    {
        if (sorted == null || SkillManager.Instance == null) return;

        GUILayout.Label($"Time Scale: {Time.timeScale:0.00}");
        Time.timeScale = GUILayout.HorizontalSlider(Time.timeScale, 0.1f, 1f, GUILayout.Width(200));

        tab = GUILayout.Toolbar(tab, tabLabels, GUILayout.Width(460));

        scroll = GUILayout.BeginScrollView(scroll, GUILayout.Width(280), GUILayout.Height(600));
        MagicElement? lastElement = null;
        foreach (var s in sorted)
        {
            if (tab > 0 && s.elementType != elements[tab - 1]) continue;

            // All 탭에서는 속성이 바뀔 때마다 구분 제목 표시
            if (tab == 0 && lastElement != s.elementType)
            {
                GUILayout.Label($"── {s.elementType} ──");
                lastElement = s.elementType;
            }

            string label = string.IsNullOrWhiteSpace(s.spellName) ? $"(이름 없음: {s.name})" : s.spellName;
            if (GUILayout.Button($"T{s.tier}  {label}  ({s.name})"))
                SkillManager.Instance.CastSpell(s.spellName);
        }
        GUILayout.EndScrollView();
    }
}
