#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

public class ProjectCleanupTool : EditorWindow
{
    private static readonly string ProjectRoot = "Assets/_Project";
    private static readonly string QuarantineRoot = "Assets/_Project/_LegacyRoot";

    // Root folders we want to clean from Assets/
    // Source folder name -> Destination folder path
    private readonly (string src, string dst)[] _defaultMoves = new[]
    {
        ("Art",      "Assets/_Project/Art"),
        ("Render",   "Assets/_Project/Render"),
        ("Scenes",   "Assets/_Project/Scenes"),
        ("Settings", "Assets/_Project/Settings"),

        // Safer to quarantine old root Editor scripts to avoid mixing tools unintentionally
        ("Editor",   "Assets/_Project/Editor/_LegacyRootEditor"),
    };

    private bool _dryRun = true;
    private bool _useQuarantineInstead = false;
    private Vector2 _scroll;
    private List<string> _report = new();

    [MenuItem("Tools/_Project/Cleanup/Project Cleanup Tool")]
    public static void Open()
    {
        var w = GetWindow<ProjectCleanupTool>("Project Cleanup");
        w.minSize = new Vector2(560, 520);
    }

    private void OnGUI()
    {
        EditorGUILayout.LabelField("Project Cleanup (Safe, GUID-friendly)", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "This tool moves legacy root folders (Assets/Art, Assets/Editor, Assets/Render, Assets/Scenes, Assets/Settings)\n" +
            "into Assets/_Project/... using AssetDatabase.MoveAsset so references stay intact.\n\n" +
            "Recommended flow:\n" +
            "1) Dry Run (see plan)\n" +
            "2) Execute\n" +
            "3) Verify Console (0 red errors)\n" +
            "4) Delete empty legacy folders (optional, manual).",
            MessageType.Info
        );

        _dryRun = EditorGUILayout.ToggleLeft("Dry Run (simulate only, no changes)", _dryRun);
        _useQuarantineInstead = EditorGUILayout.ToggleLeft("Quarantine mode (move everything to _LegacyRoot instead of mapped destinations)", _useQuarantineInstead);

        GUILayout.Space(8);

        if (GUILayout.Button(_dryRun ? "Run Dry Run" : "EXECUTE MOVES (Do It)", GUILayout.Height(36)))
        {
            Run();
        }

        GUILayout.Space(8);

        if (GUILayout.Button("Open _Project Folder", GUILayout.Height(26)))
        {
            var obj = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(ProjectRoot);
            if (obj) EditorGUIUtility.PingObject(obj);
        }

        GUILayout.Space(10);
        EditorGUILayout.LabelField("Report", EditorStyles.boldLabel);

        _scroll = EditorGUILayout.BeginScrollView(_scroll);
        foreach (var line in _report)
            EditorGUILayout.SelectableLabel(line, GUILayout.Height(16));
        EditorGUILayout.EndScrollView();
    }

    private void Run()
    {
        _report.Clear();

        if (EditorApplication.isPlaying)
        {
            _report.Add("ERROR: Stop Play Mode before cleanup.");
            return;
        }

        // Ensure base folders exist
        EnsureFolder(ProjectRoot);
        EnsureFolder(QuarantineRoot);

        var moves = BuildMovePlan();
        if (moves.Count == 0)
        {
            _report.Add("Nothing to move. (No legacy folders found or they are empty.)");
            return;
        }

        // Print summary
        _report.Add($"Mode: {(_dryRun ? "DRY RUN" : "EXECUTE")} | Quarantine: {_useQuarantineInstead}");
        _report.Add($"Planned moves: {moves.Count}");
        _report.Add("----");

        foreach (var m in moves)
            _report.Add($"{(m.isFolder ? "[Folder]" : "[Asset ]")} {m.srcPath}  ->  {m.dstPath}");

        if (_dryRun)
        {
            _report.Add("----");
            _report.Add("Dry run only. Toggle off Dry Run to execute.");
            return;
        }

        // Execute
        try
        {
            AssetDatabase.StartAssetEditing();

            int ok = 0, fail = 0;

            foreach (var m in moves)
            {
                EnsureFolder(Path.GetDirectoryName(m.dstPath)?.Replace("\\", "/") ?? ProjectRoot);

                string result = AssetDatabase.MoveAsset(m.srcPath, m.dstPath);
                if (string.IsNullOrEmpty(result))
                {
                    ok++;
                }
                else
                {
                    fail++;
                    _report.Add($"FAIL: {m.srcPath} -> {m.dstPath} | {result}");
                }
            }

            _report.Add("----");
            _report.Add($"DONE. Success: {ok}, Fail: {fail}");
        }
        finally
        {
            AssetDatabase.StopAssetEditing();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }

        // After move, suggest deleting empty legacy folders
        _report.Add("----");
        _report.Add("Next (manual, safe): if legacy root folders are now empty, right-click them in Project and Delete.");
        _report.Add("IMPORTANT: Delete only if empty. Do NOT delete via Windows Explorer.");
    }

    private List<MoveItem> BuildMovePlan()
    {
        var plan = new List<MoveItem>();

        foreach (var (srcName, mappedDst) in _defaultMoves)
        {
            string srcFolder = $"Assets/{srcName}";
            if (!AssetDatabase.IsValidFolder(srcFolder))
                continue;

            // List direct children (assets + subfolders) under Assets/<srcName>
            var children = GetDirectChildren(srcFolder);
            if (children.Count == 0)
                continue;

            foreach (var child in children)
            {
                // Skip meta-only weirdness
                if (string.IsNullOrEmpty(child)) continue;

                bool isFolder = AssetDatabase.IsValidFolder(child);

                string dstBase = _useQuarantineInstead ? $"{QuarantineRoot}/{srcName}" : mappedDst;
                string relative = child.Substring(srcFolder.Length).TrimStart('/'); // e.g. "Foo/Bar.asset" or "SubFolder"
                string dstPath = $"{dstBase}/{relative}".Replace("\\", "/");

                // If destination already exists, we'll move contents recursively (for folders) rather than failing.
                if (isFolder)
                {
                    plan.AddRange(BuildFolderMovePlan(child, dstPath));
                }
                else
                {
                    plan.Add(new MoveItem(child, dstPath, isFolder: false));
                }
            }
        }

        // Deduplicate (just in case)
        return plan
            .GroupBy(p => p.srcPath, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .ToList();
    }

    private List<MoveItem> BuildFolderMovePlan(string srcFolder, string dstFolder)
    {
        var items = new List<MoveItem>();

        // Ensure destination folder exists (we'll create it when executing)
        // Move all content inside folder individually to avoid "destination exists" failures.
        var allAssets = AssetDatabase.FindAssets("", new[] { srcFolder })
            .Select(AssetDatabase.GUIDToAssetPath)
            .Where(p => !string.IsNullOrEmpty(p))
            .Where(p => !AssetDatabase.IsValidFolder(p)) // assets only
            .ToList();

        // Include subfolders too (so we can delete empty later)
        var subfolders = GetAllSubfolders(srcFolder);

        // Assets first
        foreach (var assetPath in allAssets)
        {
            string relative = assetPath.Substring(srcFolder.Length).TrimStart('/');
            string dstPath = $"{dstFolder}/{relative}".Replace("\\", "/");
            items.Add(new MoveItem(assetPath, dstPath, isFolder: false));
        }

        // Then folders (optional report only; actual MoveAsset on folder is risky if dst exists)
        // We'll NOT move folder objects; moving assets is enough. Empty folders can be deleted manually afterward.
        foreach (var f in subfolders)
        {
            // report-only marker
            string rel = f.Substring(srcFolder.Length).TrimStart('/');
            string dst = $"{dstFolder}/{rel}".Replace("\\", "/");
            items.Add(new MoveItem(f, dst, isFolder: true));
        }

        return items;
    }

    private static List<string> GetDirectChildren(string folder)
    {
        // Direct children = everything whose directory is exactly 'folder'
        var results = new List<string>();

        string[] guids = AssetDatabase.FindAssets("", new[] { folder });
        foreach (var guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (string.IsNullOrEmpty(path)) continue;
            if (string.Equals(path, folder, StringComparison.OrdinalIgnoreCase)) continue;

            string dir = Path.GetDirectoryName(path)?.Replace("\\", "/") ?? "";
            if (string.Equals(dir, folder, StringComparison.OrdinalIgnoreCase))
            {
                results.Add(path);
            }
        }

        // Also include direct subfolders (FindAssets doesn't always return folder entries reliably)
        string abs = ToAbsolutePath(folder);
        if (Directory.Exists(abs))
        {
            foreach (var d in Directory.GetDirectories(abs))
            {
                string rel = ToAssetPath(d);
                if (!string.IsNullOrEmpty(rel))
                    results.Add(rel);
            }
        }

        return results
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static List<string> GetAllSubfolders(string folder)
    {
        var list = new List<string>();
        string abs = ToAbsolutePath(folder);
        if (!Directory.Exists(abs)) return list;

        foreach (var d in Directory.GetDirectories(abs, "*", SearchOption.AllDirectories))
        {
            string rel = ToAssetPath(d);
            if (!string.IsNullOrEmpty(rel))
                list.Add(rel.Replace("\\", "/"));
        }

        return list
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static void EnsureFolder(string folderPath)
    {
        if (string.IsNullOrEmpty(folderPath)) return;
        folderPath = folderPath.Replace("\\", "/");
        if (AssetDatabase.IsValidFolder(folderPath)) return;

        // Create nested folders
        string[] parts = folderPath.Split('/');
        if (parts.Length == 0) return;

        string current = parts[0]; // "Assets"
        for (int i = 1; i < parts.Length; i++)
        {
            string next = current + "/" + parts[i];
            if (!AssetDatabase.IsValidFolder(next))
                AssetDatabase.CreateFolder(current, parts[i]);
            current = next;
        }
    }

    private static string ToAbsolutePath(string assetPath)
    {
        // assetPath starts with "Assets/..."
        string projectDir = Directory.GetParent(Application.dataPath)!.FullName.Replace("\\", "/");
        return Path.Combine(projectDir, assetPath).Replace("\\", "/");
    }

    private static string ToAssetPath(string absolutePath)
    {
        string proj = Directory.GetParent(Application.dataPath)!.FullName.Replace("\\", "/");
        absolutePath = absolutePath.Replace("\\", "/");
        if (!absolutePath.StartsWith(proj, StringComparison.OrdinalIgnoreCase)) return "";
        string rel = absolutePath.Substring(proj.Length).TrimStart('/');
        return rel;
    }

    private readonly struct MoveItem
    {
        public readonly string srcPath;
        public readonly string dstPath;
        public readonly bool isFolder;

        public MoveItem(string src, string dst, bool isFolder)
        {
            srcPath = src.Replace("\\", "/");
            dstPath = dst.Replace("\\", "/");
            this.isFolder = isFolder;
        }
    }
}
#endif
