#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

public static class ProjectValidator
{
    private const string ROOT = "Assets/_Project";
    private const string CHARACTERS_ROOT = "Assets/_Project/Art/Characters";

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

        // Auto-fix textures: simplest + most robust = reimport all textures under _Project.
        // ImportRules.cs will apply correct settings.
        int texturesFixed = AutoFixTextures();

        // Auto-fix controllers: fill missing Motions deterministically (no lambdas)
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
            else if (p.Contains("/_project/art/") || p.Contains("/_project/ui/"))
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

            if (!path.ToLowerInvariant().Contains("/_generated/"))
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

            // expected: .../Animations/Characters/Hero1/Hero1.controller
            string hero = Path.GetFileName(Path.GetDirectoryName(path));
            string artRoot = $"{CHARACTERS_ROOT}/{hero}".Replace("\\", "/");

            foreach (var layer in controller.layers)
            {
                if (layer.stateMachine == null) continue;

                var states = CollectStates(layer.stateMachine);
                foreach (var state in states)
                {
                    if (state.motion != null) continue;

                    var clip = FindClip(artRoot, state.name);
                    if (clip == null) continue;

                    state.motion = clip;
                    statesFixed++;
                    changed = true;

                    Debug.Log($"[AutoFix] {hero} → {state.name} assigned {clip.name}");
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

    private static AnimationClip FindClip(string heroRoot, string state)
    {
        if (!AssetDatabase.IsValidFolder(heroRoot)) return null;

        string folder = string.Equals(state, "Walk", StringComparison.OrdinalIgnoreCase) ? "WalkRight" : state;
        string expected = $"{heroRoot}/{folder}/_Generated/{folder}.anim".Replace("\\", "/");

        var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(expected);
        if (clip != null && CountSpriteKeys(clip) > 0)
            return clip;

        return null;
    }

    // Collect all AnimatorState objects under a state machine (including sub-state machines)
    private static List<AnimatorState> CollectStates(AnimatorStateMachine sm)
    {
        var result = new List<AnimatorState>();
        if (sm == null) return result;

        foreach (var child in sm.states)
        {
            if (child.state != null)
                result.Add(child.state);
        }

        foreach (var sub in sm.stateMachines)
        {
            if (sub.stateMachine != null)
                result.AddRange(CollectStates(sub.stateMachine));
        }

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
