#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

public static class ProjectValidator
{
    private const string ROOT = "Assets/_Project";
    private const string ANIM_OUT = "Assets/_Project/Animations/Characters";

    // =========================
    // MENUS
    // =========================

    [MenuItem("Tools/_Project/Validate/Validate Project (Full Scan)")]
    public static void ValidateProject()
    {
        if (EditorApplication.isPlaying)
        {
            EditorUtility.DisplayDialog("Project Validator", "Stop Play Mode first.", "OK");
            return;
        }

        var issues = new List<string>();
        int textures = 0;
        int clips = 0;
        int controllers = 0;

        ValidateTextures(ref textures, issues);
        ValidateClips(ref clips, issues);
        ValidateControllers(ref controllers, issues);

        PrintSummary(textures, clips, controllers, issues, "");
    }

    [MenuItem("Tools/_Project/Validate/Validate + Auto-Fix (Safe)")]
    public static void ValidateAndAutoFix()
    {
        if (EditorApplication.isPlaying)
        {
            EditorUtility.DisplayDialog("Project Validator", "Stop Play Mode first.", "OK");
            return;
        }

        int texturesFixed = AutoFixTextures();
        int controllersFixed = AutoFixControllers(out int statesFixed, out int controllersScanned);

        var issues = new List<string>();
        int textures = 0;
        int clips = 0;
        int controllers = 0;

        ValidateTextures(ref textures, issues);
        ValidateClips(ref clips, issues);
        ValidateControllers(ref controllers, issues);

        string header =
            "AUTO-FIX SUMMARY\n" +
            $"Textures reimported : {texturesFixed}\n" +
            $"Controllers scanned : {controllersScanned}\n" +
            $"Controllers fixed   : {controllersFixed}\n" +
            $"States fixed        : {statesFixed}\n";

        PrintSummary(textures, clips, controllers, issues, header);
    }

    // =========================
    // TEXTURES
    // =========================

    private static void ValidateTextures(ref int checkedCount, List<string> issues)
    {
        foreach (string guid in AssetDatabase.FindAssets("t:Texture2D", new[] { ROOT }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid).Replace("\\", "/");
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null) continue;

            checkedCount++;

            string p = path.ToLowerInvariant();

            if (p.Contains("/_project/masks/"))
            {
                Require(issues, path, importer.textureType == TextureImporterType.Default, "Mask must be TextureType Default");
                Require(issues, path, importer.isReadable, "Mask must be Read/Write enabled");
                Require(issues, path, !importer.sRGBTexture, "Mask must have sRGB OFF");
                Require(issues, path, importer.filterMode == FilterMode.Point, "Mask must use Point filter");
                Require(issues, path, !importer.mipmapEnabled, "Mask must have mipmaps OFF");
            }
            else if (p.Contains("/_project/art/") || p.Contains("/_project/ui/") || p.Contains("/_project/vfx/"))
            {
                Require(issues, path, importer.textureType == TextureImporterType.Sprite, "Sprite must be TextureType Sprite");
                Require(issues, path, !importer.mipmapEnabled, "Sprite must have mipmaps OFF");
                Require(issues, path, importer.filterMode == FilterMode.Point, "Sprite must use Point filter");
            }
        }
    }

    private static int AutoFixTextures()
    {
        int count = 0;
        foreach (string guid in AssetDatabase.FindAssets("t:Texture2D", new[] { ROOT }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid).Replace("\\", "/");
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            count++;
        }
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        return count;
    }

    // =========================
    // CLIPS
    // =========================

    private static void ValidateClips(ref int checkedCount, List<string> issues)
    {
        foreach (string guid in AssetDatabase.FindAssets("t:AnimationClip", new[] { ROOT }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid).Replace("\\", "/");
            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
            if (clip == null) continue;

            checkedCount++;

            // Validate generated clips from BOTH pipelines:
            bool isOldGen = path.ToLowerInvariant().Contains("/_generated/");
            bool isNewGen = path.Replace("\\", "/").StartsWith(ANIM_OUT, StringComparison.OrdinalIgnoreCase);

            if (!isOldGen && !isNewGen)
                continue;

            if (CountSpriteKeys(clip) == 0)
                issues.Add($"[Clip] {path} has NO sprite keyframes");
        }
    }

    private static int CountSpriteKeys(AnimationClip clip)
    {
        foreach (var binding in AnimationUtility.GetObjectReferenceCurveBindings(clip))
        {
            if (binding.type == typeof(SpriteRenderer) && binding.propertyName == "m_Sprite")
            {
                var curve = AnimationUtility.GetObjectReferenceCurve(clip, binding);
                return curve?.Length ?? 0;
            }
        }
        return 0;
    }

    // =========================
    // CONTROLLERS
    // =========================

    private static void ValidateControllers(ref int checkedCount, List<string> issues)
    {
        foreach (string guid in AssetDatabase.FindAssets("t:AnimatorController", new[] { ROOT }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid).Replace("\\", "/");
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(path);
            if (controller == null) continue;

            checkedCount++;

            foreach (var layer in controller.layers)
            {
                if (layer.stateMachine == null) continue;

                var states = CollectStates(layer.stateMachine);
                foreach (var state in states)
                {
                    if (state.motion == null)
                        issues.Add($"[Controller] {path} → State '{state.name}' has NO Motion");
                }
            }
        }
    }

    private static int AutoFixControllers(out int statesFixed, out int scanned)
    {
        statesFixed = 0;
        scanned = 0;
        int controllersFixed = 0;

        foreach (string guid in AssetDatabase.FindAssets("t:AnimatorController", new[] { ROOT }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid).Replace("\\", "/");
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(path);
            if (controller == null) continue;

            scanned++;

            bool changed = false;

            // If controller is in our new output structure:
            // Assets/_Project/Animations/Characters/<Character>/<Character>.controller
            string folder = Path.GetDirectoryName(path)?.Replace("\\", "/") ?? "";
            string character = Path.GetFileName(folder);

            bool looksNewPipeline = folder.StartsWith(ANIM_OUT, StringComparison.OrdinalIgnoreCase);

            foreach (var layer in controller.layers)
            {
                if (layer.stateMachine == null) continue;

                var states = CollectStates(layer.stateMachine);
                foreach (var state in states)
                {
                    if (state.motion != null) continue;

                    AnimationClip clip = null;

                    if (looksNewPipeline && !string.IsNullOrEmpty(character))
                    {
                        // Clip expected: <folder>/<Character>_<StateName>.anim
                        string safeState = SanitizeFilePart(state.name);
                        string expected = $"{folder}/{character}_{safeState}.anim".Replace("\\", "/");
                        clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(expected);
                        if (clip != null && CountSpriteKeys(clip) == 0) clip = null;
                    }

                    if (clip == null) continue;

                    state.motion = clip;
                    statesFixed++;
                    changed = true;

                    Debug.Log($"[AutoFix] {character} → State '{state.name}' assigned '{clip.name}'");
                }
            }

            if (changed)
            {
                controllersFixed++;
                EditorUtility.SetDirty(controller);
            }
        }

        if (controllersFixed > 0)
        {
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }

        return controllersFixed;
    }

    private static string SanitizeFilePart(string s)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string((s ?? "").Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray());
        return cleaned.Replace(' ', '_');
    }

    private static List<AnimatorState> CollectStates(AnimatorStateMachine sm)
    {
        var result = new List<AnimatorState>();
        if (sm == null) return result;

        foreach (var child in sm.states)
            if (child.state != null)
                result.Add(child.state);

        foreach (var sub in sm.stateMachines)
            if (sub.stateMachine != null)
                result.AddRange(CollectStates(sub.stateMachine));

        return result;
    }

    // =========================
    // UTIL
    // =========================

    private static void Require(List<string> issues, string path, bool condition, string message)
    {
        if (!condition)
            issues.Add($"[Import] {path} → {message}");
    }

    private static void PrintSummary(int textures, int clips, int controllers, List<string> issues, string header)
    {
        if (!string.IsNullOrEmpty(header))
            Debug.Log(header);

        Debug.Log($"Checked → Textures:{textures} Clips:{clips} Controllers:{controllers}");

        if (issues.Count == 0)
        {
            EditorUtility.DisplayDialog("Project Validator", "✅ ALL GOOD", "OK");
            return;
        }

        foreach (var i in issues)
            Debug.LogWarning(i);

        EditorUtility.DisplayDialog("Project Validator", $"⚠ Issues found: {issues.Count}\nSee Console.", "OK");
    }
}
#endif
