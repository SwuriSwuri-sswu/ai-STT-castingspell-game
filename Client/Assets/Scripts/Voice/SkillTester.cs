using UnityEngine;

public class SkillTester : MonoBehaviour
{
    [Range(0.1f, 1f)] public float timeScale = 1f;
    Vector2 scroll;

    void Start()
    {
        foreach (var s in SkillManager.Instance.spellDatabase)
            SkillManager.Instance.UnlockSpell(s);   // 전부 해금
    }

    void OnGUI()
    {
        Time.timeScale = GUILayout.HorizontalSlider(timeScale = Time.timeScale, 0.1f, 1f, GUILayout.Width(200));
        scroll = GUILayout.BeginScrollView(scroll, GUILayout.Width(260), GUILayout.Height(600));
        foreach (var s in SkillManager.Instance.spellDatabase)
            if (s != null && GUILayout.Button($"[{s.elementType} T{s.tier}] {s.spellName} ({s.castType})"))
                SkillManager.Instance.CastSpell(s.spellName);
        GUILayout.EndScrollView();
    }
}
