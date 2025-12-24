#if UNITY_EDITOR
using System;
using System.IO;
using UnityEditor;
using UnityEngine;

public static class SpriteNameAuditAndFix
{
    private const string Prefix = "char_";

    [MenuItem("Tools/_Project/Utilities/Audit Sprite Names (Selected Folder)")]
    public static void AuditSelectedFolder()
    {
        if (!TryGetSelectedFolder(out var folder)) return;
        Audit(folder);
    }

    [MenuItem("Tools/_Project/Utilities/Fix Sprite Names Remove 'char_' (Selected Folder)")]
    public static void FixSelectedFolder()
    {
        if (!TryGetSelectedFolder(out var folder)) return;
        Fix(folder);
    }

    private static bool TryGetSelectedFolder(out string folderPath)
    {
        folderPath = null;
        var obj = Selection.activeObject;
        if (obj == null)
        {
            Debug.LogError("[SpriteAudit] Select a folder in the Project window first.");
            return false;
        }

        folderPath = AssetDatabase.GetAssetPath(obj).Replace("\\", "/");
        if (!AssetDatabase.IsValidFolder(folderPath))
        {
            Debug.LogError($"[SpriteAudit] Selected object is not a folder: {folderPath}");
            return false;
        }
        return true;
    }

    private static void Audit(string rootFolder)
    {
        var guids = AssetDatabase.FindAssets("t:Sprite", new[] { rootFolder });

        int total = 0;
        int bad = 0;

        foreach (var guid in guids)
        {
            var path = AssetDatabase.GUIDToAssetPath(guid).Replace("\\", "/");
            var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
            if (sprite == null) continue;

            total++;

            // sprite.name is the truth for the builder
            if (sprite.name.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase))
            {
                bad++;
                Debug.LogWarning($"[SpriteAudit] BAD sprite.name '{sprite.name}' at {path}");
            }
        }

        Debug.Log($"[SpriteAudit] Audit done. Folder={rootFolder} TotalSprites={total} BadSpritesStartingWithChar_={bad}");

        EditorUtility.DisplayDialog(
            "Sprite Audit",
            $"Folder:\n{rootFolder}\n\nTotal sprites: {total}\nBad sprites (name starts with 'char_'): {bad}\n\nSee Console for details.",
            "OK"
        );
    }

    private static void Fix(string rootFolder)
    {
        // We fix by renaming the SOURCE TEXTURE asset (file) to force Unity to regenerate sub-asset names.
        // This is robust: it updates GUID safely.
        var textureGuids = AssetDatabase.FindAssets("t:Texture2D", new[] { rootFolder });

        int textures = 0;
        int renamedTextures = 0;
        int reimported = 0;
        int errors = 0;

        AssetDatabase.StartAssetEditing();
        try
        {
            foreach (var guid in textureGuids)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid).Replace("\\", "/");
                if (string.IsNullOrWhiteSpace(path) || !path.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
                    continue;

                textures++;

                // If file already fixed, still reimport (to refresh sprite subasset names if needed)
                var fileName = Path.GetFileNameWithoutExtension(path);

                // If file name starts with char_, rename file asset
                if (fileName.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase))
                {
                    var newName = fileName.Substring(Prefix.Length);
                    if (string.IsNullOrWhiteSpace(newName))
                    {
                        errors++;
                        Debug.LogError($"[SpriteAudit] Cannot rename (empty result): {path}");
                        continue;
                    }

                    string err = AssetDatabase.RenameAsset(path, newName);
                    if (!string.IsNullOrEmpty(err))
                    {
                        errors++;
                        Debug.LogError($"[SpriteAudit] Rename failed: {path} -> {newName} | {err}");
                        continue;
                    }

                    renamedTextures++;
                }

                // Force reimport to refresh sprite name metadata
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
                reimported++;
            }
        }
        finally
        {
            AssetDatabase.StopAssetEditing();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }

        Debug.Log($"[SpriteAudit] Fix done. Folder={rootFolder} TexturesScanned={textures} RenamedTextures={renamedTextures} Reimported={reimported} Errors={errors}");

        EditorUtility.DisplayDialog(
            "Sprite Fix",
            $"Terminé ✅\n\nFolder:\n{rootFolder}\n\nTextures scanned: {textures}\nRenamed textures: {renamedTextures}\nReimported: {reimported}\nErrors: {errors}\n\nNow run Audit again.",
            "OK"
        );
    }
}
#endif
