using UnityEditor;
using UnityEngine;

// 메뉴 Tools → Demo
//   시연 모드 켜기 / 끄기 : Assets/Resources/DemoConfig.asset의 Enabled를 바꿈 (에셋이 없으면 만듦)
//   시연 설정 열기        : DemoConfig 에셋 선택
public static class DemoModeTool
{
    private const string AssetPath = "Assets/Resources/DemoConfig.asset";
    private const string MenuOn = "Tools/Demo/시연 모드 켜기";
    private const string MenuOff = "Tools/Demo/시연 모드 끄기 (본게임)";

    [MenuItem(MenuOn)]
    private static void TurnOn() => Set(true);

    [MenuItem(MenuOff)]
    private static void TurnOff() => Set(false);

    [MenuItem(MenuOn, true)]
    private static bool CanTurnOn() { DemoConfig c = Load(); return c == null || !c.enabled; }

    [MenuItem(MenuOff, true)]
    private static bool CanTurnOff() { DemoConfig c = Load(); return c != null && c.enabled; }

    [MenuItem("Tools/Demo/시연 설정 열기")]
    private static void Open()
    {
        DemoConfig c = FindOrCreate();
        Selection.activeObject = c;
        EditorGUIUtility.PingObject(c);
    }

    private static void Set(bool on)
    {
        DemoConfig c = FindOrCreate();
        Undo.RecordObject(c, on ? "시연 모드 켜기" : "시연 모드 끄기");
        c.enabled = on;
        EditorUtility.SetDirty(c);
        AssetDatabase.SaveAssets();
        Selection.activeObject = c;
        Debug.Log(on ? "<color=#FFCC00>[Demo] 시연 모드 켜짐 (simulation 브랜치 전용)</color>"
                     : "[Demo] 시연 모드 꺼짐 (본게임)");
        EditorUtility.DisplayDialog("시연 모드", on ? "시연 모드를 켰습니다.\nmain 브랜치에 이 상태로 올리지 마세요!"
                                                  : "시연 모드를 껐습니다. (본게임)", "확인");
    }

    private static DemoConfig Load() => AssetDatabase.LoadAssetAtPath<DemoConfig>(AssetPath);

    private static DemoConfig FindOrCreate()
    {
        DemoConfig c = Load();
        if (c != null) return c;
        if (!AssetDatabase.IsValidFolder("Assets/Resources")) AssetDatabase.CreateFolder("Assets", "Resources");
        c = ScriptableObject.CreateInstance<DemoConfig>();
        AssetDatabase.CreateAsset(c, AssetPath);
        AssetDatabase.SaveAssets();
        return c;
    }
}