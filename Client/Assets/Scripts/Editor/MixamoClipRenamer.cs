using System.IO;
using UnityEditor;
using UnityEngine;

// Mixamo FBX 안의 애니메이션 클립 이름(전부 "mixamo.com")을 파일 이름으로 한 번에 바꿔 줌
// 사용: Project 창에서 FBX 여러 개 선택 → 상단 메뉴 Tools → Mixamo → 선택한 FBX 정리
//   - 클립 이름 = 파일 이름 (예: Attack_1H_1.fbx → 클립 "Attack_1H_1")
//   - 반복 설정: 이름에 Idle / Sitting이 있으면 Loop Time 켬, 나머지는 끔
//   - 제자리 고정: 동작 중 몸이 미끄러지거나 돌아가지 않게 Root Transform을 Bake Into Pose
// ※ 이 파일은 반드시 'Editor' 폴더 안에 두기 (예: Assets/Scripts/Editor/MixamoClipRenamer.cs)
public static class MixamoClipRenamer
{
    [MenuItem("Tools/Mixamo/선택한 FBX 정리 (클립 이름 = 파일 이름)")]
    private static void RenameSelected()
    {
        int done = 0;
        foreach (Object obj in Selection.objects)
        {
            string path = AssetDatabase.GetAssetPath(obj);
            if (string.IsNullOrEmpty(path) || !path.ToLowerInvariant().EndsWith(".fbx")) continue;

            ModelImporter importer = AssetImporter.GetAtPath(path) as ModelImporter;
            if (importer == null) continue;

            ModelImporterClipAnimation[] clips = importer.clipAnimations;
            if (clips == null || clips.Length == 0) clips = importer.defaultClipAnimations;
            if (clips == null || clips.Length == 0) continue; // 애니메이션 없는 FBX

            string fileName = Path.GetFileNameWithoutExtension(path);
            string lower = fileName.ToLowerInvariant();
            bool loop = lower.Contains("idle") || lower.Contains("sitting");

            for (int i = 0; i < clips.Length; i++)
            {
                clips[i].name = clips.Length == 1 ? fileName : $"{fileName}_{i + 1}";
                clips[i].loopTime = loop;
                // 제자리 고정 (Bake Into Pose)
                clips[i].lockRootRotation = true;
                clips[i].lockRootHeightY = true;
                clips[i].lockRootPositionXZ = true;
                clips[i].keepOriginalOrientation = true;
                clips[i].keepOriginalPositionY = true;
                clips[i].keepOriginalPositionXZ = true;
            }

            importer.clipAnimations = clips;
            importer.SaveAndReimport();
            done++;
            Debug.Log($"[Mixamo] {fileName}: 클립 이름 정리 완료 (반복 {(loop ? "켬" : "끔")})");
        }
        Debug.Log($"[Mixamo] FBX {done}개 정리 완료");
    }

    [MenuItem("Tools/Mixamo/선택한 FBX 정리 (클립 이름 = 파일 이름)", true)]
    private static bool Validate()
    {
        foreach (Object obj in Selection.objects)
        {
            string path = AssetDatabase.GetAssetPath(obj);
            if (!string.IsNullOrEmpty(path) && path.ToLowerInvariant().EndsWith(".fbx")) return true;
        }
        return false;
    }
}