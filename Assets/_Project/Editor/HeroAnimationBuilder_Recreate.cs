#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

public static class HeroAnimationBuilder_Recreate
{
    private const float DefaultFps = 12f;

    [MenuItem("Tools/_Project/Hero/Build Clip (Force Recreate, Fix Empty Clips)")]
    public static void BuildForceRecreate()
    {
        if (EditorApplication.isPlaying)
        {
            EditorUtility.DisplayDialog("Hero Builder", "Stop Play Mode avant de générer les clips.", "OK");
            return;
        }

        var folders = GetSelectedFolderPathsRobust();
        if (folders.Count == 0)
        {
            EditorUtility.DisplayDialog(
                "Hero Builder",
                "Sélectionne 1+ DOSSIERS dans Project (Idle1, Idle2, WalkRight...).",
                "OK"
            );
            return;
        }

        bool includeSubfolders = EditorUtility.DisplayDialog(
            "Hero Builder",
            "Inclure les sous-dossiers ?\n\nOui si tu as WalkRight/Frames etc.",
            "Oui",
            "Non"
        );

        int made = 0;
        var report = new List<string>();

        foreach (var folder in folders)
        {
            var sprites = FindSprites(folder, includeSubfolders)
                .Distinct()
                .OrderBy(s => NaturalSortKey(s.name), StringComparer.OrdinalIgnoreCase)
                .ThenBy(s => s.name, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            if (sprites.Length == 0)
            {
                int textures = CountTextures(folder, includeSubfolders);
                string line = $"- {folder}: Sprites=0 (Textures={textures}) -> RIEN";
                report.Add(line);
                Debug.LogWarning("[HeroBuilder] " + line);
                continue;
            }

            string outFolder = folder + "/_Generated";
            EnsureFolder(outFolder);

            string folderName = Path.GetFileName(folder);
            string clipPath = $"{outFolder}/{SanitizeName(folderName)}.anim";

            // 🔥 Force: delete then recreate (évite les clips “fantômes” vides)
            if (AssetDatabase.LoadAssetAtPath<AnimationClip>(clipPath) != null)
            {
                AssetDatabase.DeleteAsset(clipPath);
            }

            var clip = new AnimationClip();
            clip.frameRate = DefaultFps;

            WriteSpriteKeys(clip, sprites, DefaultFps);

            SetLoop(clip, true);

            AssetDatabase.CreateAsset(clip, clipPath);
            EditorUtility.SetDirty(clip);

            // ✅ Vérif hard: est-ce que la courbe existe vraiment ?
            int keyCount = GetSpriteKeyCount(clip);
            if (keyCount <= 0)
            {
                string bad = $"- {folder}: ❌ Clip créé MAIS 0 keyframe (BUG) -> {clipPath}";
                report.Add(bad);
                Debug.LogError("[HeroBuilder] " + bad);
            }
            else
            {
                string ok = $"- {folder}: ✅ Keyframes={keyCount} -> {clipPath}";
                report.Add(ok);
                Debug.Log("[HeroBuilder] " + ok);
                made++;
            }
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        EditorUtility.DisplayDialog(
            "Hero Builder",
            $"Terminé.\nClips OK: {made}\n\nDétails (Console):\n{string.Join("\n", report)}",
            "OK"
        );
    }

    // -------------------- Core: write sprite keys --------------------

    private static void WriteSpriteKeys(AnimationClip clip, Sprite[] sprites, float fps)
    {
        var binding = new EditorCurveBinding
        {
            type = typeof(SpriteRenderer),
            path = "",
            propertyName = "m_Sprite"
        };

        float dt = 1f / fps;
        var keys = new ObjectReferenceKeyframe[sprites.Length];

        for (int i = 0; i < sprites.Length; i++)
        {
            keys[i] = new ObjectReferenceKeyframe
            {
                time = i * dt,
                value = sprites[i]
            };
        }

        AnimationUtility.SetObjectReferenceCurve(clip, binding, keys);
    }

    private static int GetSpriteKeyCount(AnimationClip clip)
    {
        var bindings = AnimationUtility.GetObjectReferenceCurveBindings(clip);
        foreach (var b in bindings)
        {
            if (b.type == typeof(SpriteRenderer) && b.propertyName == "m_Sprite")
            {
                var curve = AnimationUtility.GetObjectReferenceCurve(clip, b);
                return curve != null ? curve.Length : 0;
            }
        }
        return 0;
    }

    private static void SetLoop(AnimationClip clip, bool loop)
    {
        var settings = AnimationUtility.GetAnimationClipSettings(clip);
        settings.loopTime = loop;
        AnimationUtility.SetAnimationClipSettings(clip, settings);
    }

    // -------------------- Selection / scan --------------------

    private static List<string> GetSelectedFolderPathsRobust()
    {
        var folderPaths = new List<string>();

        var objs = Selection.GetFiltered<UnityEngine.Object>(SelectionMode.Assets);
        foreach (var obj in objs)
        {
            string path = AssetDatabase.GetAssetPath(obj);
            if (!string.IsNullOrEmpty(path) && AssetDatabase.IsValidFolder(path))
                folderPaths.Add(path);
        }

        if (folderPaths.Count == 0)
        {
            foreach (var guid in Selection.assetGUIDs)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (!string.IsNullOrEmpty(path) && AssetDatabase.IsValidFolder(path))
                    folderPaths.Add(path);
            }
        }

        return folderPaths.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static IEnumerable<Sprite> FindSprites(string folderPath, bool includeSubfolders)
    {
        string[] guids = AssetDatabase.FindAssets("t:Sprite", new[] { folderPath });

        foreach (var guid in guids)
        {
            string assetPath = AssetDatabase.GUIDToAssetPath(guid);

            if (!includeSubfolders)
            {
                string dir = Path.GetDirectoryName(assetPath)?.Replace("\\", "/");
                if (!string.Equals(dir, folderPath, StringComparison.OrdinalIgnoreCase))
                    continue;
            }

            foreach (var s in AssetDatabase.LoadAllAssetsAtPath(assetPath).OfType<Sprite>())
                yield return s;
        }
    }

    private static int CountTextures(string folderPath, bool includeSubfolders)
    {
        string[] guids = AssetDatabase.FindAssets("t:Texture2D", new[] { folderPath });
        int count = 0;

        foreach (var guid in guids)
        {
            string assetPath = AssetDatabase.GUIDToAssetPath(guid);

            if (!includeSubfolders)
            {
                string dir = Path.GetDirectoryName(assetPath)?.Replace("\\", "/");
                if (!string.Equals(dir, folderPath, StringComparison.OrdinalIgnoreCase))
                    continue;
            }

            count++;
        }

        return count;
    }

    // -------------------- Utils --------------------

    private static void EnsureFolder(string fullFolder)
    {
        if (AssetDatabase.IsValidFolder(fullFolder)) return;

        string[] parts = fullFolder.Split('/');
        string current = parts[0]; // Assets
        for (int i = 1; i < parts.Length; i++)
        {
            string next = current + "/" + parts[i];
            if (!AssetDatabase.IsValidFolder(next))
                AssetDatabase.CreateFolder(current, parts[i]);
            current = next;
        }
    }

    private static string SanitizeName(string s)
    {
        if (string.IsNullOrEmpty(s)) return "Clip";
        foreach (char c in Path.GetInvalidFileNameChars())
            s = s.Replace(c, '_');
        return s;
    }

    private static string NaturalSortKey(string input)
    {
        if (string.IsNullOrEmpty(input)) return "";
        return Regex.Replace(input, @"\d+", m => m.Value.PadLeft(10, '0'));
    }
}
#endif
