#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEngine;

public static class ProjectBootstrap
{
    const string Root = "Assets/_Project";

    [MenuItem("Tools/Project/Bootstrap Folders", priority = 10)]
    public static void Bootstrap()
    {
        try
        {
            // Root
            EnsureFolder("Assets", "_Project");

            // Core
            EnsurePath($"{Root}/Scenes");
            EnsurePath($"{Root}/Scripts/Runtime");
            EnsurePath($"{Root}/Scripts/Editor");
            EnsurePath($"{Root}/Prefabs");
            EnsurePath($"{Root}/Animations");
            EnsurePath($"{Root}/Data");

            // Art
            EnsurePath($"{Root}/Art/Characters/Hero");
            EnsurePath($"{Root}/Art/Characters/NPC");
            EnsurePath($"{Root}/Art/Environments");
            EnsurePath($"{Root}/Art/Props");
            EnsurePath($"{Root}/Art/UI/Icons");
            EnsurePath($"{Root}/Art/UI/Cursors");
            EnsurePath($"{Root}/Art/UI/Bubbles");
            EnsurePath($"{Root}/Art/UI/Panels");
            EnsurePath($"{Root}/Art/VFX");

            // Masks (gameplay)
            EnsurePath($"{Root}/Art/Masks/Hotspots");
            EnsurePath($"{Root}/Art/Masks/Walkmasks");
            EnsurePath($"{Root}/Art/Masks/Occluders");
            EnsurePath($"{Root}/Art/Masks/Triggers");

            // Audio
            EnsurePath($"{Root}/Audio/SFX");
            EnsurePath($"{Root}/Audio/VO");
            EnsurePath($"{Root}/Audio/Music");
            EnsurePath($"{Root}/Audio/Ambience");

            // Video / fonts / materials / shaders
            EnsurePath($"{Root}/Video");
            EnsurePath($"{Root}/Fonts");
            EnsurePath($"{Root}/Materials");
            EnsurePath($"{Root}/Shaders");

            // Optional: a simple “Incoming” landing zone for quick drops
            EnsurePath($"{Root}/_Incoming/Art");
            EnsurePath($"{Root}/_Incoming/Audio");
            EnsurePath($"{Root}/_Incoming/Data");

            AssetDatabase.Refresh();

            EditorUtility.DisplayDialog(
                "Project Bootstrap",
                "✅ Folder structure created.\n\nNothing was moved.\nSafe to run again anytime.",
                "OK"
            );
        }
        catch (Exception ex)
        {
            Debug.LogError(ex);
            EditorUtility.DisplayDialog("Project Bootstrap - Error", ex.Message, "OK");
        }
    }

    // --- helpers ---
    static void EnsurePath(string fullPath)
    {
        if (AssetDatabase.IsValidFolder(fullPath)) return;

        var parts = fullPath.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
        string current = parts[0]; // Assets
        for (int i = 1; i < parts.Length; i++)
        {
            string next = parts[i];
            string candidate = $"{current}/{next}";
            if (!AssetDatabase.IsValidFolder(candidate))
                AssetDatabase.CreateFolder(current, next);
            current = candidate;
        }
    }

    static void EnsureFolder(string parent, string name)
    {
        string candidate = $"{parent}/{name}";
        if (AssetDatabase.IsValidFolder(candidate)) return;
        if (!AssetDatabase.IsValidFolder(parent))
            EnsurePath(parent);
        AssetDatabase.CreateFolder(parent, name);
    }
}
#endif
