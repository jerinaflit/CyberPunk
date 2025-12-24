#if UNITY_EDITOR
using System;
using System.IO;
using UnityEditor;
using UnityEngine;

public static class BatchRename_RemovePrefix
{
    private const string PrefixToRemove = "char_";

    [MenuItem("Tools/_Project/Utilities/Remove Prefix 'char_' (Selected Folder)")]
    public static void RemovePrefix_SelectedFolder()
    {
        var obj = Selection.activeObject;
        if (obj == null)
        {
            Debug.LogError("[Rename] No folder selected. Select a folder in the Project window first.");
            return;
        }

        var folderPath = AssetDatabase.GetAssetPath(obj).Replace("\\", "/");
        if (!AssetDatabase.IsValidFolder(folderPath))
        {
            Debug.LogError($"[Rename] Selected object is not a folder: {folderPath}");
            return;
        }

        RemovePrefix_InFolder(folderPath);
    }

    [MenuItem("Tools/_Project/Utilities/Remove Prefix 'char_' (Selected Folder)", validate = true)]
    private static bool Validate_RemovePrefix_SelectedFolder()
    {
        var obj = Selection.activeObject;
        if (obj == null) return false;
        var path = AssetDatabase.GetAssetPath(obj).Replace("\\", "/");
        return AssetDatabase.IsValidFolder(path);
    }

    private static void RemovePrefix_InFolder(string rootFolder)
    {
        // Find all assets in folder (includes subfolders)
        var guids = AssetDatabase.FindAssets("", new[] { rootFolder });
        int renamed = 0;
        int skipped = 0;
        int errors = 0;

        AssetDatabase.StartAssetEditing();
        try
        {
            foreach (var guid in guids)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid).Replace("\\", "/");
                if (string.IsNullOrWhiteSpace(path)) { skipped++; continue; }

                // We rename based on the asset file name (without extension)
                var fileName = Path.GetFileNameWithoutExtension(path);
                if (string.IsNullOrWhiteSpace(fileName)) { skipped++; continue; }

                if (!fileName.StartsWith(PrefixToRemove, StringComparison.OrdinalIgnoreCase))
                {
                    skipped++;
                    continue;
                }

                var newName = fileName.Substring(PrefixToRemove.Length);

                // Safety: avoid empty name
                if (string.IsNullOrWhiteSpace(newName))
                {
                    Debug.LogWarning($"[Rename] Skipped (would be empty): {path}");
                    skipped++;
                    continue;
                }

                // Safety: if name already matches, skip
                if (string.Equals(fileName, newName, StringComparison.OrdinalIgnoreCase))
                {
                    skipped++;
                    continue;
                }

                // RenameAsset expects name WITHOUT extension
                string renameError = AssetDatabase.RenameAsset(path, newName);
                if (!string.IsNullOrEmpty(renameError))
                {
                    errors++;
                    Debug.LogError($"[Rename] Failed: {path} -> {newName} | {renameError}");
                    continue;
                }

                renamed++;
            }
        }
        finally
        {
            AssetDatabase.StopAssetEditing();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }

        Debug.Log($"[Rename] Done. Renamed: {renamed}, Skipped: {skipped}, Errors: {errors}\nRoot: {rootFolder}");

        EditorUtility.DisplayDialog(
            "Batch Rename",
            $"Terminé ✅\n\nRoot:\n{rootFolder}\n\nRenamed: {renamed}\nSkipped: {skipped}\nErrors: {errors}",
            "OK"
        );
    }
}
#endif
