using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;

// 메뉴 Tools → Sound
//   사운드 라이브러리 자동 채우기 : Assets/Resources/SoundLibrary.asset을 만들고(없으면),
//                                  파일 이름이 같은 오디오를 찾아 빈 칸을 채움 (이미 넣어 둔 칸은 건드리지 않음)
//   오디오 가져오기 설정 맞추기   : BGM_ / AMB_ 파일은 Streaming(메모리 절약), SFX_ 파일은 바로 재생되게
public static class SoundLibraryTool
{
    private const string LibraryPath = "Assets/Resources/SoundLibrary.asset";

    [MenuItem("Tools/Sound/사운드 라이브러리 자동 채우기")]
    public static void AutoFill()
    {
        SoundLibrary lib = FindOrCreate();
        Dictionary<string, List<AudioClip>> clips = AllClips();

        Undo.RecordObject(lib, "Sound Library 자동 채우기");
        int filled = 0;
        List<string> missing = new List<string>();
        foreach (FieldInfo f in typeof(SoundLibrary).GetFields(BindingFlags.Public | BindingFlags.Instance))
        {
            SoundLibrary.FileAttribute attr = f.GetCustomAttribute<SoundLibrary.FileAttribute>();
            if (attr == null) continue;
            string key = Key(attr.name);

            if (f.FieldType == typeof(AudioClip))
            {
                if (f.GetValue(lib) as AudioClip != null) continue;   // 이미 넣어 둔 칸
                // 같은 이름 → 없으면 "이름_무엇" (예: SFX_Cast_Success_앞에거만)
                AudioClip found = clips.TryGetValue(key, out List<AudioClip> list) ? list[0]
                    : clips.Where(kv => kv.Key.StartsWith(key + "_")).OrderBy(kv => kv.Key).Select(kv => kv.Value[0]).FirstOrDefault();
                if (found != null) { f.SetValue(lib, found); filled++; }
                else missing.Add(attr.name);
            }
            else if (f.FieldType == typeof(AudioClip[]))
            {
                // "이름" 또는 "이름_무엇" (예: SFX_Cast_Fire_1, SFX_Cast_Water_ice)
                //   이미 들어 있는 것은 그대로 두고, 새로 추가된 파일만 뒤에 붙임
                List<AudioClip> current = ((f.GetValue(lib) as AudioClip[]) ?? new AudioClip[0]).Where(c => c != null).ToList();
                List<AudioClip> found = clips.Where(kv => kv.Key == key || kv.Key.StartsWith(key + "_"))
                                             .OrderBy(kv => kv.Key)
                                             .Select(kv => kv.Value[0]).ToList();
                int before = current.Count;
                foreach (AudioClip c in found) if (!current.Contains(c)) current.Add(c);
                if (current.Count > before) { f.SetValue(lib, current.ToArray()); filled += current.Count - before; }
                if (current.Count == 0) missing.Add(attr.name + "_*");
            }
        }
        EditorUtility.SetDirty(lib);
        AssetDatabase.SaveAssets();
        Selection.activeObject = lib;
        EditorGUIUtility.PingObject(lib);

        // 어디에도 연결되지 않은 BGM_/AMB_/SFX_ 파일 (SpellData의 Cast·Hit Sound에 넣은 것은 사용 중으로 침)
        HashSet<AudioClip> used = new HashSet<AudioClip>();
        foreach (FieldInfo f in typeof(SoundLibrary).GetFields(BindingFlags.Public | BindingFlags.Instance))
        {
            if (f.GetValue(lib) is AudioClip one && one != null) used.Add(one);
            else if (f.GetValue(lib) is AudioClip[] many) foreach (AudioClip c in many) if (c != null) used.Add(c);
        }
        foreach (string guid in AssetDatabase.FindAssets("t:SpellData"))
        {
            SpellData s = AssetDatabase.LoadAssetAtPath<SpellData>(AssetDatabase.GUIDToAssetPath(guid));
            if (s == null) continue;
            if (s.castSound != null) used.Add(s.castSound);
            if (s.hitSound != null) used.Add(s.hitSound);
        }
        List<string> unused = clips.Values.Select(l => l[0])
            .Where(c => !used.Contains(c) && (c.name.StartsWith("SFX_") || c.name.StartsWith("BGM_") || c.name.StartsWith("AMB_")))
            .Select(c => c.name).OrderBy(n => n).ToList();

        StringBuilder sb = new StringBuilder();
        sb.AppendLine($"새로 채운 칸·파일: {filled}개 (이미 들어 있는 칸은 그대로 둠)");
        if (missing.Count > 0)
        {
            sb.AppendLine($"\n못 찾은 파일 ({missing.Count}개, 없어도 되는 것은 무시):");
            foreach (string m in missing) sb.AppendLine("  " + m);
        }
        if (unused.Count > 0)
        {
            sb.AppendLine($"\n아직 어디에도 연결되지 않은 파일 ({unused.Count}개):");
            foreach (string u in unused) sb.AppendLine("  " + u);
            sb.AppendLine("→ 스펠 전용이면 SpellData의 Cast/Hit Sound에, 아니면 라이브러리 칸에 직접 끌어다 넣기");
        }
        Debug.Log("[Sound] 자동 채우기\n" + sb);
        EditorUtility.DisplayDialog("사운드 라이브러리", sb.ToString(), "확인");
    }

    [MenuItem("Tools/Sound/오디오 가져오기 설정 맞추기")]
    public static void FixImportSettings()
    {
        int changed = 0;
        foreach (string guid in AssetDatabase.FindAssets("t:AudioClip", new[] { "Assets" }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            string name = Path.GetFileNameWithoutExtension(path);
            bool music = name.StartsWith("BGM_") || name.StartsWith("AMB_");
            bool sfx = name.StartsWith("SFX_");
            if (!music && !sfx) continue;

            AudioImporter imp = AssetImporter.GetAtPath(path) as AudioImporter;
            if (imp == null) continue;
            AudioImporterSampleSettings s = imp.defaultSampleSettings;
            s.loadType = music ? AudioClipLoadType.Streaming : AudioClipLoadType.DecompressOnLoad;
            s.compressionFormat = AudioCompressionFormat.Vorbis;
            s.quality = music ? 0.6f : 0.8f;
            imp.defaultSampleSettings = s;
            imp.loadInBackground = music;
            imp.SaveAndReimport();
            changed++;
        }
        Debug.Log($"[Sound] 가져오기 설정 맞춤: {changed}개 (BGM_/AMB_ = Streaming, SFX_ = Decompress On Load)");
        EditorUtility.DisplayDialog("오디오 가져오기 설정", $"{changed}개 파일을 맞췄습니다.", "확인");
    }

    [MenuItem("Tools/Sound/사운드 라이브러리 열기")]
    public static void Open()
    {
        SoundLibrary lib = FindOrCreate();
        Selection.activeObject = lib;
        EditorGUIUtility.PingObject(lib);
    }

    private static SoundLibrary FindOrCreate()
    {
        SoundLibrary lib = AssetDatabase.LoadAssetAtPath<SoundLibrary>(LibraryPath);
        if (lib != null) return lib;
        if (!AssetDatabase.IsValidFolder("Assets/Resources")) AssetDatabase.CreateFolder("Assets", "Resources");
        lib = ScriptableObject.CreateInstance<SoundLibrary>();
        AssetDatabase.CreateAsset(lib, LibraryPath);
        AssetDatabase.SaveAssets();
        Debug.Log("[Sound] " + LibraryPath + " 를 만들었습니다.");
        return lib;
    }

    // 파일 이름(대소문자·앞뒤 공백 무시) → 오디오 목록
    private static Dictionary<string, List<AudioClip>> AllClips()
    {
        Dictionary<string, List<AudioClip>> map = new Dictionary<string, List<AudioClip>>();
        foreach (string guid in AssetDatabase.FindAssets("t:AudioClip", new[] { "Assets" }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            AudioClip clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
            if (clip == null) continue;
            string key = Key(Path.GetFileNameWithoutExtension(path));
            if (!map.TryGetValue(key, out List<AudioClip> list)) map[key] = list = new List<AudioClip>();
            list.Add(clip);
        }
        return map;
    }

    private static string Key(string name) => name.Trim().Normalize(NormalizationForm.FormC).ToLowerInvariant();
}