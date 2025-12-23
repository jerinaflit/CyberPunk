#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

public static class ProjectOrganizer
{
    const string Root = "Assets/_Project";

    // On ne touche pas à ces dossiers (sécurité)
    static readonly string[] ProtectedRoots =
    {
        "Assets/_Project",
        "Assets/Editor",
        "Assets/Plugins",
        "Assets/StreamingAssets",
        "Assets/Settings",
        "Assets/TextMesh Pro",
        "Assets/AddressableAssetsData"
    };

    [MenuItem("Tools/Project/Organize (Dry Run)", priority = 1)]
    public static void DryRun() => OrganizeInternal(dryRun: true);

    [MenuItem("Tools/Project/Organize (Execute)", priority = 2)]
    public static void Execute() => OrganizeInternal(dryRun: false);

    static void OrganizeInternal(bool dryRun)
    {
        EnsureFolderStructure();

        // Collect all assets under Assets/ (excluding folders and excluded roots)
        var all = AssetDatabase.GetAllAssetPaths()
            .Where(p => p.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
            .Where(p => !AssetDatabase.IsValidFolder(p))
            .Where(p => !IsUnderAny(p, ProtectedRoots))
            .ToList();

        // IMPORTANT: ne pas déplacer .meta, ni rien sous Packages (AssetDatabase ne les liste pas en Assets/ normalement)
        // IMPORTANT: ne pas déplacer les .asset (URP, settings, etc.) par défaut -> trop risqué.
        var plan = new List<MoveOp>();
        var skipped = new List<string>();

        foreach (var path in all)
        {
            string ext = Path.GetExtension(path).ToLowerInvariant();
            string file = Path.GetFileName(path);
            string lower = file.ToLowerInvariant();

            // Skip: .asset (URP assets, settings, etc.) => on laisse en place volontairement
            if (ext == ".asset")
            {
                skipped.Add($"{path}  (skip .asset)");
                continue;
            }

            // Skip: packages-like or unity internal stuff we really shouldn't move
            if (lower.EndsWith(".asmdef") || lower.EndsWith(".asmref"))
            {
                skipped.Add($"{path}  (skip asmdef/asmref)");
                continue;
            }

            // Decide destination
            string destFolder = GetDestinationFolder(path, ext, lower);
            if (string.IsNullOrEmpty(destFolder))
            {
                skipped.Add($"{path}  (no rule)");
                continue;
            }

            // Ensure dest folder exists
            EnsurePath(destFolder);

            string destPath = $"{destFolder}/{file}";
            destPath = MakeUniquePathIfNeeded(destPath);

            // If already in place (rare), skip
            if (string.Equals(path, destPath, StringComparison.OrdinalIgnoreCase))
                continue;

            plan.Add(new MoveOp(path, destPath));
        }

        // Write report (always)
        string reportPath = $"{Root}/Logs/organize_report_{DateTime.Now:yyyyMMdd_HHmmss}.txt";
        EnsurePath($"{Root}/Logs");
        WriteReport(reportPath, dryRun, plan, skipped);

        // Dry run just prints
        if (dryRun)
        {
            Debug.Log($"[ProjectOrganizer] DRY RUN: {plan.Count} moves planned. Skipped: {skipped.Count}. Report: {reportPath}");
            return;
        }

        // Confirm
        bool ok = EditorUtility.DisplayDialog(
            "Project Organizer",
            $"This will MOVE {plan.Count} assets into {Root}.\n\n" +
            $"Skipped: {skipped.Count} (notably .asset files are NOT moved).\n\n" +
            $"A report was written to:\n{reportPath}\n\nProceed?",
            "Yes, move",
            "Cancel"
        );

        if (!ok)
        {
            Debug.Log("[ProjectOrganizer] Cancelled by user.");
            return;
        }

        // Execute moves (batch)
        int moved = 0;
        int errors = 0;

        AssetDatabase.StartAssetEditing();
        try
        {
            foreach (var op in plan)
            {
                string err = AssetDatabase.MoveAsset(op.From, op.To);
                if (string.IsNullOrEmpty(err))
                {
                    moved++;
                }
                else
                {
                    errors++;
                    Debug.LogError($"[ProjectOrganizer] Move failed: {op.From} -> {op.To}\n{err}");
                }
            }
        }
        finally
        {
            AssetDatabase.StopAssetEditing();
            AssetDatabase.Refresh();
        }

        Debug.Log($"[ProjectOrganizer] DONE. Moved: {moved}/{plan.Count}. Errors: {errors}. Report: {reportPath}");
        EditorUtility.DisplayDialog("Project Organizer", $"Done.\nMoved: {moved}/{plan.Count}\nErrors: {errors}\n\nReport:\n{reportPath}", "OK");
    }

    // ---------------- Rules ----------------

    static string GetDestinationFolder(string path, string ext, string lowerFileName)
    {
        // Scenes
        if (ext == ".unity") return $"{Root}/Scenes";

        // Scripts
        if (ext == ".cs")
        {
            // If file looks like editor script (name contains editor) OR currently under any Editor folder (handled by protected roots)
            if (lowerFileName.Contains("editor")) return $"{Root}/Scripts/Editor";
            return $"{Root}/Scripts/Runtime";
        }

        // Prefabs
        if (ext == ".prefab") return $"{Root}/Prefabs";

        // Animations / controllers
        if (ext == ".anim" || ext == ".controller" || ext == ".overridecontroller")
            return $"{Root}/Animations";

        // Images
        if (ext == ".png" || ext == ".jpg" || ext == ".jpeg" || ext == ".tga" || ext == ".psd" || ext == ".webp")
        {
            // Masks (hotspot/walkmask/etc.)
            if (lowerFileName.Contains("hot") || lowerFileName.Contains("walk") || lowerFileName.Contains("mask") ||
                lowerFileName.Contains("occ") || lowerFileName.Contains("trigger"))
            {
                // finer split
                if (lowerFileName.Contains("hot")) return $"{Root}/Art/Masks/Hotspots";
                if (lowerFileName.Contains("walk")) return $"{Root}/Art/Masks/Walkmasks";
                if (lowerFileName.Contains("occ")) return $"{Root}/Art/Masks/Occluders";
                if (lowerFileName.Contains("trigger")) return $"{Root}/Art/Masks/Triggers";
                return $"{Root}/Art/Masks";
            }

            // UI
            if (lowerFileName.StartsWith("ui_") || lowerFileName.Contains("icon") || lowerFileName.Contains("cursor") || lowerFileName.Contains("bubble"))
                return $"{Root}/Art/UI";

            // Characters
            if (lowerFileName.Contains("hero") || lowerFileName.Contains("npc") || lowerFileName.Contains("char"))
                return $"{Root}/Art/Characters";

            // Default: environments/props
            return $"{Root}/Art";
        }

        // Audio
        if (ext == ".wav" || ext == ".mp3" || ext == ".ogg" || ext == ".aiff" || ext == ".aif")
        {
            if (lowerFileName.StartsWith("vo_") || lowerFileName.Contains("voice") || lowerFileName.Contains("dialog"))
                return $"{Root}/Audio/VO";
            if (lowerFileName.StartsWith("music_") || lowerFileName.Contains("music"))
                return $"{Root}/Audio/Music";
            if (lowerFileName.StartsWith("amb_") || lowerFileName.Contains("ambience") || lowerFileName.Contains("ambient"))
                return $"{Root}/Audio/Ambience";
            return $"{Root}/Audio/SFX";
        }

        // Video
        if (ext == ".mp4" || ext == ".mov" || ext == ".webm" || ext == ".m4v")
            return $"{Root}/Video";

        // Fonts
        if (ext == ".ttf" || ext == ".otf")
            return $"{Root}/Fonts";

        // Materials / Shaders
        if (ext == ".mat") return $"{Root}/Materials";
        if (ext == ".shader" || ext == ".shadergraph" || ext == ".compute") return $"{Root}/Shaders";

        // Data (JSON, txt, csv, etc.)
        if (ext == ".json" || ext == ".csv" || ext == ".txt" || ext == ".tsv" || ext == ".yaml" || ext == ".yml")
            return $"{Root}/Data";

        return null;
    }

    // ---------------- Folder structure ----------------

    static void EnsureFolderStructure()
    {
        EnsureFolder("Assets", "_Project");

        EnsurePath($"{Root}/Scenes");
        EnsurePath($"{Root}/Scripts/Runtime");
        EnsurePath($"{Root}/Scripts/Editor");
        EnsurePath($"{Root}/Prefabs");
        EnsurePath($"{Root}/Animations");
        EnsurePath($"{Root}/Data");
        EnsurePath($"{Root}/Logs");

        EnsurePath($"{Root}/Art");
        EnsurePath($"{Root}/Art/Characters");
        EnsurePath($"{Root}/Art/Environments");
        EnsurePath($"{Root}/Art/Props");
        EnsurePath($"{Root}/Art/UI");
        EnsurePath($"{Root}/Art/VFX");

        EnsurePath($"{Root}/Art/Masks");
        EnsurePath($"{Root}/Art/Masks/Hotspots");
        EnsurePath($"{Root}/Art/Masks/Walkmasks");
        EnsurePath($"{Root}/Art/Masks/Occluders");
        EnsurePath($"{Root}/Art/Masks/Triggers");

        EnsurePath($"{Root}/Audio/SFX");
        EnsurePath($"{Root}/Audio/VO");
        EnsurePath($"{Root}/Audio/Music");
        EnsurePath($"{Root}/Audio/Ambience");

        EnsurePath($"{Root}/Video");
        EnsurePath($"{Root}/Fonts");
        EnsurePath($"{Root}/Materials");
        EnsurePath($"{Root}/Shaders");

        AssetDatabase.Refresh();
    }

    // ---------------- Utilities ----------------

    static bool IsUnderAny(string path, IEnumerable<string> roots)
    {
        foreach (var r in roots)
        {
            if (path.StartsWith(r + "/", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(path, r, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    static void EnsurePath(string fullPath)
    {
        if (AssetDatabase.IsValidFolder(fullPath)) return;

        var parts = fullPath.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return;

        string current = parts[0]; // "Assets"
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

    static string MakeUniquePathIfNeeded(string destPath)
    {
        if (!File.Exists(destPath)) return destPath;
        return AssetDatabase.GenerateUniqueAssetPath(destPath);
    }

    static void WriteReport(string reportPath, bool dryRun, List<MoveOp> plan, List<string> skipped)
    {
        using var sw = new StreamWriter(reportPath, false);
        sw.WriteLine($"ProjectOrganizer Report");
        sw.WriteLine($"Mode: {(dryRun ? "DRY RUN" : "EXECUTE")}");
        sw.WriteLine($"Date: {DateTime.Now}");
        sw.WriteLine();

        sw.WriteLine($"Planned moves: {plan.Count}");
        foreach (var op in plan)
            sw.WriteLine($"MOVE: {op.From} -> {op.To}");

        sw.WriteLine();
        sw.WriteLine($"Skipped: {skipped.Count}");
        foreach (var s in skipped)
            sw.WriteLine($"SKIP: {s}");
    }

    readonly struct MoveOp
    {
        public readonly string From;
        public readonly string To;
        public MoveOp(string from, string to) { From = from; To = to; }
    }
}
#endif
