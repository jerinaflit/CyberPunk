#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.U2D.Sprites;
using UnityEngine;

public static class SpriteSubSpritePrefixFixer
{
    private const string Prefix = "char_";

    [MenuItem("Tools/_Project/Utilities/Sprites/Audit sprite.name startswith 'char_' (Selected Folder)")]
    public static void AuditSelectedFolder()
    {
        if (!TryGetSelectedFolder(out var folder)) return;
        Audit(folder);
    }

    [MenuItem("Tools/_Project/Utilities/Sprites/FIX sub-sprite names remove 'char_' (Selected Folder)")]
    public static void FixSelectedFolder()
    {
        if (!TryGetSelectedFolder(out var folder)) return;
        Fix(folder);
    }

    // Optional: run on entire project root quickly
    [MenuItem("Tools/_Project/Utilities/Sprites/FIX sub-sprite names remove 'char_' (Whole _Project)")]
    public static void FixWholeProject()
    {
        Fix("Assets/_Project");
    }

    // ---------------------------
    // AUDIT
    // ---------------------------
    private static void Audit(string rootFolder)
    {
        var guids = AssetDatabase.FindAssets("t:Sprite", new[] { rootFolder });

        int total = 0;
        int bad = 0;

        foreach (var guid in guids)
        {
            var path = AssetDatabase.GUIDToAssetPath(guid).Replace("\\", "/");
            if (string.IsNullOrWhiteSpace(path)) continue;

            var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
            if (sprite == null) continue;

            total++;

            if (sprite.name.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase))
            {
                bad++;
                Debug.LogWarning($"[SpriteFix] BAD sprite.name '{sprite.name}' at {path}");
            }
        }

        Debug.Log($"[SpriteFix] Audit done. Folder={rootFolder} TotalSprites={total} BadSpritesStartingWithChar_={bad}");

        EditorUtility.DisplayDialog(
            "Sprite Audit",
            $"Folder:\n{rootFolder}\n\nTotal sprites: {total}\nBad sprites (name starts with '{Prefix}'): {bad}\n\nDetails in Console.",
            "OK"
        );
    }

    // ---------------------------
    // FIX (THE REAL PROBLEM)
    // ---------------------------
    private static void Fix(string rootFolder)
    {
        var textureGuids = AssetDatabase.FindAssets("t:Texture2D", new[] { rootFolder });

        int texturesScanned = 0;
        int texturesWithSheet = 0;
        int texturesChanged = 0;
        int subSpritesRenamed = 0;
        int errors = 0;

        // ✅ Collect paths to reimport AFTER StopAssetEditing
        var toReimport = new List<string>(256);

        AssetDatabase.StartAssetEditing();
        try
        {
            foreach (var guid in textureGuids)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid).Replace("\\", "/");
                if (string.IsNullOrWhiteSpace(path)) continue;

                // You can loosen this if you use other formats.
                if (!path.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
                    continue;

                texturesScanned++;

                var importer = AssetImporter.GetAtPath(path) as TextureImporter;
                if (importer == null) continue;

                if (importer.spriteImportMode != SpriteImportMode.Multiple)
                    continue;

                var dataProvider = GetSpriteDataProvider(importer);
                var spriteRects = dataProvider.GetSpriteRects();
                if (spriteRects == null || spriteRects.Length == 0)
                    continue;

                texturesWithSheet++;

                bool anyChange = false;

                for (int i = 0; i < spriteRects.Length; i++)
                {
                    var name = spriteRects[i].name;
                    if (string.IsNullOrWhiteSpace(name))
                        continue;

                    if (name.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase))
                    {
                        spriteRects[i].name = name.Substring(Prefix.Length);
                        anyChange = true;
                        subSpritesRenamed++;
                    }
                }

                if (anyChange)
                {
                    dataProvider.SetSpriteRects(spriteRects);
                    dataProvider.Apply();

                    // Mark importer dirty (safe)
                    EditorUtility.SetDirty(importer);

                    // ✅ Reimport later (deterministic)
                    toReimport.Add(path);

                    texturesChanged++;
                }
            }
        }
        catch (Exception ex)
        {
            errors++;
            Debug.LogError($"[SpriteFix] Exception: {ex}");
        }
        finally
        {
            AssetDatabase.StopAssetEditing();
        }

        // ✅ Deterministic reimport OUTSIDE batch
        int reimported = 0;
        foreach (var p in toReimport.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            AssetDatabase.ImportAsset(p, ImportAssetOptions.ForceUpdate);
            reimported++;
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log(
            $"[SpriteFix] Done. Folder={rootFolder}\n" +
            $"TexturesScanned={texturesScanned}\n" +
            $"TexturesWithSpriteSheet={texturesWithSheet}\n" +
            $"TexturesChanged={texturesChanged}\n" +
            $"SubSpritesRenamed={subSpritesRenamed}\n" +
            $"Reimported={reimported}\n" +
            $"Errors={errors}"
        );

        EditorUtility.DisplayDialog(
            "Sprite Sub-Sprite Fix",
            $"Terminé ✅\n\nFolder:\n{rootFolder}\n\n" +
            $"Textures scanned: {texturesScanned}\n" +
            $"Textures with spritesheet: {texturesWithSheet}\n" +
            $"Textures changed: {texturesChanged}\n" +
            $"Sub-sprites renamed: {subSpritesRenamed}\n" +
            $"Reimported: {reimported}\n" +
            $"Errors: {errors}\n\n" +
            $"Ensuite: relance l'Audit.",
            "OK"
        );
    }

    private static SpriteDataProviderFactories _spriteDataProviderFactories;

    private static ISpriteEditorDataProvider GetSpriteDataProvider(TextureImporter importer)
    {
        if (_spriteDataProviderFactories == null)
        {
            _spriteDataProviderFactories = new SpriteDataProviderFactories();
            _spriteDataProviderFactories.Init();
        }

        var dataProvider = _spriteDataProviderFactories.GetSpriteEditorDataProviderFromObject(importer);
        dataProvider.InitSpriteEditorDataProvider();
        return dataProvider;
    }

    // ---------------------------
    // UI helpers
    // ---------------------------
    private static bool TryGetSelectedFolder(out string folderPath)
    {
        folderPath = null;

        var obj = Selection.activeObject;
        if (obj == null)
        {
            Debug.LogError("[SpriteFix] Select a folder in the Project window first.");
            return false;
        }

        folderPath = AssetDatabase.GetAssetPath(obj).Replace("\\", "/");
        if (!AssetDatabase.IsValidFolder(folderPath))
        {
            Debug.LogError($"[SpriteFix] Selected object is not a folder: {folderPath}");
            return false;
        }

        return true;
    }
}
#endif
