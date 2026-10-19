using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// 한글 UI → 영어로 한 번에 바꾸기 (씬마다 한 번 실행 → Ctrl+S)
// 사용: 씬을 열고 상단 메뉴 Tools → Localization → 열린 씬 UI 영어로 바꾸기
//   1. 우리 스크립트의 문구 칸(Inspector에 저장된 한글)을 → 코드에 적힌 영어 기본값으로
//   2. TextMeshPro 글자(씬에 직접 적어 둔 한글)를 → 아래 번역표대로
//   바꾸지 못한 한글은 Console에 경로와 함께 표시 → 직접 수정
//   주문 이름(SpellData)과 게임 제목은 일부러 건드리지 않음
// ※ 이 파일은 반드시 'Editor' 폴더 안에 두기
public static class EnglishUIApplier
{
    private static readonly Regex Hangul = new Regex("[가-힣]");

    // 씬에 직접 적힌 TMP 글자 번역표 (공백·줄바꿈 차이는 무시하고 비교)
    private static readonly Dictionary<string, string> TextTable = new Dictionary<string, string>
    {
        { "닫기", "Close" },
        { "도전!", "Challenge!" },
        { "도전한다!", "Challenge!" },
        { "더 모은다...", "Keep collecting..." },
        { "효과가 굉장했다!", "It's super effective!" },
        { "전체 화면", "Fullscreen" },
        { "해상도", "Resolution" },
        { "마스터 볼륨", "Master Volume" },
        { "마이크 입력 장치", "Microphone" },
        { "노말", "Normal" },
        { "물", "Water" },
        { "불", "Fire" },
        { "풀", "Grass" },
        { "주문을 영창하세요.", "Chant a spell." },
        { "하급 물 속성 몬스터 처치!\nNew!", "Defeated a Low Water monster!\nNew!" },
        { "처치한 몬스터\n모은 주문\n노말\n체력\n모든 주문을 모았습니다!",
          "Monsters defeated\nSpells collected\nNormal\nHP\nYou have spells of every element!" },
        { "<스펠 이름> 스펠을 획득했습니다!", "New spell learned: [Spell]!" },
        { "이름 등급\n5마리 처치\n7초마다 7 공격\n약점: 물", "Name Rank\n5 defeated\n7 dmg every 7s\nWeakness: Water" },
        { "하수인 공격까지 12초", "Minion attacks in 12s" },
        { "침묵의 마왕", "Demon Lord of Silence" },
        { "나의 턴!", "Your turn!" },
        { "마왕의 턴!", "Demon Lord's turn!" },
        { "남은 턴 30", "Turns left 30" },
        {
            "<b>[Space]</b>를 누른 채 주문을 외치면 마법이 나갑니다.\n" +
            "<b>상성</b>: 물 → 불 → 풀 → 물 순서로 강합니다.\n" +
            "<b>체력</b>이 0이 되면 게임 오버! \n" +
            "도감을 열어 몬스터의 공격력을 잘 보세요.\n\n" +
            "{도감}: 지금까지 수집한 마법과 몬스터에 대한 도감입니다.\n" +
            "{지팡이}: 지금 쓰는 지팡이를 확인합니다.\n" +
            "{설정}: 환경설정을 엽니다.\n" +
            "{음악}: 배경음악을 켜고 끕니다.",

            "Hold <b>[Space]</b> and chant a spell to cast magic.\n" +
            "<b>Elements</b>: Water > Fire > Grass > Water.\n" +
            "When your <b>HP</b> hits 0, it's game over!\n" +
            "Open the book to check each monster's attack power.\n\n" +
            "{Book}: Your collection of spells and monsters.\n" +
            "{Wand}: Check the wand you are using.\n" +
            "{Settings}: Open the settings.\n" +
            "{Music}: Turn the background music on or off."
        },
        {
            "<size=140%>턴제 전투!</size>\n" +
            "나의 턴마다 주문을 한 번 외칠 수 있습니다.\n" +
            "마왕은 체력이 줄 때마다 마왕의 속성이 바뀝니다.\n" +
            "체력 바의 <color=#FF5555>색깔</color>을 잘 보세요!\n\n" +
            "상성 마법으로 맞히면 체력을 회복하고, \n" +
            "마왕이 모으는 마법도 끊어낼 수 있습니다.\n\n" +
            "하수인이 나타나면 먼저 처치하세요!\n" +
            "<size=120%>30턴</size> 안에 마왕을 쓰러뜨려야 합니다.\n\n" +
            "무운을 빕니다...",

            "<size=140%>Turn-based battle!</size>\n" +
            "You can chant one spell on each of your turns.\n" +
            "The Demon Lord's element changes as his HP drops.\n" +
            "Watch the <color=#FF5555>color</color> of his HP bar!\n\n" +
            "Hitting him with a strong element heals you\n" +
            "and can break the magic he is charging.\n\n" +
            "If minions appear, defeat them first!\n" +
            "You must defeat the Demon Lord within <size=120%>30 turns</size>.\n\n" +
            "Good luck..."
        },
    };

    // 일부러 한글로 남겨 둘 글자 (게임 제목, 주문 이름 견본)
    private static readonly HashSet<string> KeepKorean = new HashSet<string>
    {
        "영창하는 사람들",
        "춤추는 분홍 꽃잎",
    };

    [MenuItem("Tools/Localization/열린 씬 UI 영어로 바꾸기")]
    private static void ApplyToOpenScenes()
    {
        var table = new Dictionary<string, string>();
        foreach (var kv in TextTable) table[Normalize(kv.Key)] = kv.Value;
        var keep = new HashSet<string>();
        foreach (string k in KeepKorean) keep.Add(Normalize(k));

        var defaults = new Dictionary<System.Type, SerializedObject>();
        var temps = new List<GameObject>();
        var left = new StringBuilder();
        int fields = 0, texts = 0, remaining = 0;

        try
        {
            for (int s = 0; s < SceneManager.sceneCount; s++)
            {
                Scene scene = SceneManager.GetSceneAt(s);
                if (!scene.isLoaded) continue;
                bool changed = false;

                foreach (GameObject root in scene.GetRootGameObjects())
                {
                    // 1. 우리 스크립트의 문구 칸 → 코드 기본값(영어)
                    foreach (MonoBehaviour mb in root.GetComponentsInChildren<MonoBehaviour>(true))
                    {
                        if (mb == null || mb is TMP_Text) continue;
                        System.Type type = mb.GetType();
                        if (type.Assembly.GetName().Name != "Assembly-CSharp") continue;

                        SerializedObject so = new SerializedObject(mb);
                        SerializedObject def = null;
                        bool soChanged = false;
                        SerializedProperty p = so.GetIterator();
                        while (p.Next(true))
                        {
                            if (p.propertyType != SerializedPropertyType.String) continue;
                            string value = p.stringValue;
                            if (string.IsNullOrEmpty(value) || !Hangul.IsMatch(value)) continue;

                            if (def == null) def = GetDefaults(type, defaults, temps);
                            SerializedProperty d = def != null ? def.FindProperty(p.propertyPath) : null;
                            if (d != null && d.propertyType == SerializedPropertyType.String && !Hangul.IsMatch(d.stringValue))
                            {
                                p.stringValue = d.stringValue;
                                soChanged = true;
                                fields++;
                            }
                            else
                            {
                                remaining++;
                                left.AppendLine($"[칸] {Path(mb.transform)} → {type.Name}.{p.propertyPath} = \"{Short(value)}\"");
                            }
                        }
                        if (soChanged)
                        {
                            so.ApplyModifiedProperties();
                            changed = true;
                        }
                    }

                    // 2. 씬에 적힌 TextMeshPro 글자 → 번역표
                    foreach (TMP_Text t in root.GetComponentsInChildren<TMP_Text>(true))
                    {
                        string value = t.text;
                        if (string.IsNullOrEmpty(value) || !Hangul.IsMatch(value)) continue;
                        string key = Normalize(value);
                        if (keep.Contains(key)) continue;
                        if (table.TryGetValue(key, out string english))
                        {
                            Undo.RecordObject(t, "English UI");
                            t.text = english;
                            EditorUtility.SetDirty(t);
                            changed = true;
                            texts++;
                        }
                        else
                        {
                            remaining++;
                            left.AppendLine($"[글자] {Path(t.transform)} = \"{Short(value)}\"");
                        }
                    }
                }

                if (changed) EditorSceneManager.MarkSceneDirty(scene);
            }
        }
        finally
        {
            foreach (GameObject go in temps) if (go != null) Object.DestroyImmediate(go);
        }

        Debug.Log($"[English UI] 문구 칸 {fields}개, TMP 글자 {texts}개를 영어로 바꿨습니다. Ctrl+S로 씬을 저장하세요.");
        if (remaining > 0)
            Debug.LogWarning($"[English UI] 직접 바꿔야 할 한글 {remaining}개:\n{left}");
        else
            Debug.Log("[English UI] 남은 한글 없음 (게임 제목·주문 이름 견본은 일부러 남김)");
    }

    // 코드에 적힌 기본값을 읽기 위한 임시 컴포넌트 (꺼진 오브젝트에 붙여 Awake가 돌지 않게)
    private static SerializedObject GetDefaults(System.Type type, Dictionary<System.Type, SerializedObject> cache, List<GameObject> temps)
    {
        if (cache.TryGetValue(type, out SerializedObject so)) return so;
        so = null;
        try
        {
            GameObject go = new GameObject("__EnglishUIDefaults") { hideFlags = HideFlags.HideAndDontSave };
            go.SetActive(false);
            temps.Add(go);
            Component c = go.AddComponent(type);
            if (c != null) so = new SerializedObject(c);
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"[English UI] {type.Name} 기본값을 읽지 못했습니다: {e.Message}");
        }
        cache[type] = so;
        return so;
    }

    private static string Normalize(string s) => Regex.Replace(s ?? "", @"\s+", " ").Trim();

    private static string Short(string s)
    {
        s = (s ?? "").Replace("\n", "\\n");
        return s.Length > 60 ? s.Substring(0, 60) + "…" : s;
    }

    private static string Path(Transform t)
    {
        string path = t.name;
        while (t.parent != null)
        {
            t = t.parent;
            path = t.name + "/" + path;
        }
        return path;
    }
}
