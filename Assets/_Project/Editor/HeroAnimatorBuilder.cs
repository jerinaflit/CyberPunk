#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

public static class HeroAnimationBuilder
{
    private const float DefaultFps = 12f;

    [MenuItem("Tools/_Project/Hero/Build Clip From Selected Folder(s)")]
    public static void BuildClipPerFolder()
    {
        Debug.Log("[HeroAnimationBuilder] BuildClipPerFolder()");

        var folderPaths = GetSelectedFolderPathsRobust();
        if (folderPaths.Count == 0)
        {
            EditorUtility.DisplayDialog(
                "Hero Builder",
                "Sélectionne 1 ou plusieurs DOSSIERS dans la fenêtre Project (Idle1, Idle2, WalkRight, Turn...).",
                "OK"
            );
            Debug.LogWarning("[HeroAnimationBuilder] No folders selected.");
            return;
        }

        bool includeSubfolders = EditorUtility.DisplayDialog(
            "Hero Builder",
            "Chercher aussi dans les sous-dossiers ?\n\n" +
            "- Oui : inclut WalkRight/Frames etc.\n" +
            "- Non : uniquement les fichiers directement dans le dossier (anti-mélange).",
            "Oui",
            "Non"
        );

        int totalClips = 0;
        var report = new List<string>();

        foreach (var folder in folderPaths)
        {
            // Trouve les sprites
            var sprites = FindSprites(folder, includeSubfolders).ToList();

            if (sprites.Count == 0)
            {
                int texCount = CountTextures(folder, includeSubfolders);
                string line = $"- {folder}: 0 Sprite (Textures={texCount}) -> RIEN généré";
                report.Add(line);
                Debug.LogWarning("[HeroAnimationBuilder] " + line);
                continue;
            }

            // Tri "naturel" : frame_2 avant frame_10
            sprites = sprites
                .OrderBy(s => NaturalSortKey(s.name), StringComparer.OrdinalIgnoreCase)
                .ThenBy(s => s.name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            string outFolder = folder + "/_Generated";
            EnsureFolder(outFolder);

            string folderName = Path.GetFileName(folder);
            string clipPath = $"{outFolder}/{SanitizeName(folderName)}.anim";

            // Crée/overwrite clip
            var clip = CreateOrOverwriteClip(clipPath);
            FillSpriteClip(clip, sprites.ToArray(), DefaultFps, loop: true);

            EditorUtility.SetDirty(clip);

            totalClips++;
            string okLine = $"- {folder}: Sprites={sprites.Count} -> {clipPath}";
            report.Add(okLine);
            Debug.Log("[HeroAnimationBuilder] " + okLine);
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        EditorUtility.DisplayDialog(
            "Hero Builder",
            $"Terminé.\nClips générés: {totalClips}\n\nDétails (Console):\n{string.Join("\n", report)}",
            "OK"
        );
    }

    // -------------------------
    // Clip creation (NON VIDE)
    // -------------------------
    private static AnimationClip CreateOrOverwriteClip(string clipPath)
    {
        var existing = AssetDatabase.LoadAssetAtPath<AnimationClip>(clipPath);
        if (existing != null) return existing;

        var clip = new AnimationClip();
        AssetDatabase.CreateAsset(clip, clipPath);
        return clip;
    }

    private static void FillSpriteClip(AnimationClip clip, Sprite[] sprites, float fps, bool loop)
    {
        if (clip == null) throw new ArgumentNullException(nameof(clip));

        // Clear previous curves
        foreach (var b in AnimationUtility.GetObjectReferenceCurveBindings(clip))
            AnimationUtility.SetObjectReferenceCurve(clip, b, null);
        foreach (var b in AnimationUtility.GetCurveBindings(clip))
            AnimationUtility.SetEditorCurve(clip, b, null);

        if (sprites == null || sprites.Length == 0)
        {
            Debug.LogError("[HeroAnimationBuilder] FillSpriteClip called with 0 sprites.");
            return;
        }

        clip.frameRate = fps;

        // ✅ Binding correct pour SpriteRenderer
        var binding = new EditorCurveBinding
        {
            type = typeof(SpriteRenderer),
            path = "",
            propertyName = "m_Sprite"
        };

        var keys = new ObjectReferenceKeyframe[sprites.Length];
        float dt = 1f / fps;

        for (int i = 0; i < sprites.Length; i++)
        {
            keys[i] = new ObjectReferenceKeyframe
            {
                time = i * dt,
                value = sprites[i]
            };
        }

        // ✅ LIGNE CRITIQUE: écrit réellement la courbe (sinon clip vide)
        AnimationUtility.SetObjectReferenceCurve(clip, binding, keys);

        // Loop settings
        var settings = AnimationUtility.GetAnimationClipSettings(clip);
        settings.loopTime = loop;
        AnimationUtility.SetAnimationClipSettings(clip, settings);

        EditorUtility.SetDirty(clip);
    }

    // -------------------------
    // Sprite scanning
    // -------------------------
    private static IEnumerable<Sprite> FindSprites(string folderPath, bool includeSubfolders)
    {
        // Find all sprite assets under folder
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

            // Support spritesheet: load all sprites at same assetPath
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

    // -------------------------
    // Selection robust
    // -------------------------
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

    // -------------------------
    // Utils
    // -------------------------
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

    // "Natural sort" key (frame_2 < frame_10)
    private static string NaturalSortKey(string input)
    {
        if (string.IsNullOrEmpty(input)) return "";
        return Regex.Replace(input, @"\d+", m => m.Value.PadLeft(10, '0'));
    }
}
#endif
