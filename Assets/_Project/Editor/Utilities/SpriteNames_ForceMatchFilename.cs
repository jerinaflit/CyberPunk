#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.U2D.Sprites;
using UnityEngine;

public static class SpriteNames_ForceMatchFilename
{
    // Optional mapping: if you still have files or sub-sprites starting with hero_
    private const string From = "hero_";
    private const string To = "hero1_";

    [MenuItem("Tools/_Project/Utilities/Sprites/FIX Sprite names to MATCH filename (Selected Folder)")]
    public static void FixSelectedFolder()
    {
        if (!TryGetSelectedFolder(out var folder)) return;
        Fix(folder);
    }

    [MenuItem("Tools/_Project/Utilities/Sprites/AUDIT Sprite names != filename (Selected Folder)")]
    public static void AuditSelectedFolder()
    {
        if (!TryGetSelectedFolder(out var folder)) return;
        Audit(folder);
    }

    private static void Audit(string rootFolder)
    {
        var textureGuids = AssetDatabase.FindAssets("t:Texture2D", new[] { rootFolder });

        int scanned = 0;
        int audited = 0;
        int skippedMulti = 0;
        int mismatched = 0;

        foreach (var guid in textureGuids)
        {
            var path = AssetDatabase.GUIDToAssetPath(guid).Replace("\\", "/");
            if (string.IsNullOrWhiteSpace(path)) continue;
            if (!path.EndsWith(".png", StringComparison.OrdinalIgnoreCase)) continue;

            scanned++;

            var expected = Path.GetFileNameWithoutExtension(path);
            expected = ApplyOptionalHeroMapping(expected);

            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null) continue;

            // Only audit cases where "sprite name should match filename":
            // - SpriteImportMode.Single
            // - SpriteImportMode.Multiple with exactly ONE sprite
            var sprites = AssetDatabase.LoadAllAssetsAtPath(path).OfType<Sprite>().ToArray();
            if (importer.spriteImportMode == SpriteImportMode.Multiple && sprites.Length != 1)
            {
                skippedMulti++;
                continue;
            }

            if (sprites.Length == 0) continue;
            audited++;

            var actual = sprites[0].name;
            if (!string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase))
            {
                mismatched++;
                Debug.LogWarning($"[SpriteNameAudit] MISMATCH: expected '{expected}' but sprite.name is '{actual}' at {path}");
            }
        }

        Debug.Log($"[SpriteNameAudit] Done. Folder={rootFolder} TexturesScanned={scanned} AuditedTextures={audited} SkippedMultiSpriteTextures={skippedMulti} MismatchedSprites={mismatched}");
        EditorUtility.DisplayDialog("Sprite Name Audit", $"Folder:\n{rootFolder}\n\nTextures scanned: {scanned}\nAudited textures: {audited}\nSkipped multi-sprite textures: {skippedMulti}\nMismatched sprites: {mismatched}\n\nDetails in Console.", "OK");
    }

    private static void Fix(string rootFolder)
    {
        var textureGuids = AssetDatabase.FindAssets("t:Texture2D", new[] { rootFolder });

        int scanned = 0;
        int changedImporters = 0;
        int renamedMetaEntries = 0;
        int forcedSingle = 0;
        int noSpriteRects = 0;
        int errors = 0;

        var toReimport = new List<string>(256);

        AssetDatabase.StartAssetEditing();
        try
        {
            foreach (var guid in textureGuids)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid).Replace("\\", "/");
                if (string.IsNullOrWhiteSpace(path)) continue;
                if (!path.EndsWith(".png", StringComparison.OrdinalIgnoreCase)) continue;

                scanned++;

                var importer = AssetImporter.GetAtPath(path) as TextureImporter;
                if (importer == null) continue;

                // Expected name from filename
                var expected = Path.GetFileNameWithoutExtension(path);
                expected = ApplyOptionalHeroMapping(expected);

                bool anyChange = false;

                // Unity 6+: use Sprite Editor data provider API (SpriteRects) for BOTH Single and Multiple.
                var dataProvider = GetSpriteDataProvider(importer);
                var spriteRects = dataProvider.GetSpriteRects();

                if (spriteRects == null || spriteRects.Length == 0)
                {
                    noSpriteRects++;

                    // If importer is Multiple but has no sprite rects, Unity can be in a weird state.
                    // Safest: switch to Single so sprite.name becomes filename deterministically.
                    if (importer.spriteImportMode == SpriteImportMode.Multiple)
                    {
                        importer.spriteImportMode = SpriteImportMode.Single;
                        anyChange = true;
                        forcedSingle++;
                    }
                }
                else
                {
                    // Rules:
                    // - If there is exactly ONE rect -> force its name to expected
                    // - Else -> rename each rect using optional mapping (do NOT force-match filename when there are many sprites)
                    if (spriteRects.Length == 1)
                    {
                        var before = spriteRects[0].name ?? "";
                        if (!string.Equals(before, expected, StringComparison.OrdinalIgnoreCase))
                        {
                            spriteRects[0].name = expected;
                            anyChange = true;
                            renamedMetaEntries++;
                        }
                    }
                    else
                    {
                        for (int i = 0; i < spriteRects.Length; i++)
                        {
                            if (string.IsNullOrWhiteSpace(spriteRects[i].name)) continue;

                            var newName = ApplyOptionalHeroMapping(spriteRects[i].name);
                            if (string.Equals(newName, "hero", StringComparison.OrdinalIgnoreCase))
                                newName = $"{expected}_{i:D3}";

                            if (!string.Equals(spriteRects[i].name, newName, StringComparison.OrdinalIgnoreCase))
                            {
                                spriteRects[i].name = newName;
                                anyChange = true;
                                renamedMetaEntries++;
                            }
                        }
                    }

                    if (anyChange)
                    {
                        dataProvider.SetSpriteRects(spriteRects);
                        dataProvider.Apply();
                    }
                }

                if (anyChange)
                {
                    EditorUtility.SetDirty(importer);
                    toReimport.Add(path);
                    changedImporters++;
                }
            }
        }
        catch (Exception ex)
        {
            errors++;
            Debug.LogError($"[SpriteNameFix] Exception: {ex}");
        }
        finally
        {
            AssetDatabase.StopAssetEditing();
        }

        // Deterministic reimport AFTER batch
        foreach (var p in toReimport.Distinct(StringComparer.OrdinalIgnoreCase))
            AssetDatabase.ImportAsset(p, ImportAssetOptions.ForceUpdate);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log(
            $"[SpriteNameFix] Done. Folder={rootFolder}\n" +
            $"TexturesScanned={scanned}\n" +
            $"ImportersChanged={changedImporters}\n" +
            $"MetaEntriesRenamed={renamedMetaEntries}\n" +
            $"ForcedSingle={forcedSingle}\n" +
            $"NoSpriteRects={noSpriteRects}\n" +
            $"Errors={errors}"
        );

        EditorUtility.DisplayDialog(
            "Sprite Name Fix",
            $"Terminé ✅\n\nFolder:\n{rootFolder}\n\n" +
            $"Textures scanned: {scanned}\n" +
            $"Importers changed: {changedImporters}\n" +
            $"Meta entries renamed: {renamedMetaEntries}\n" +
            $"Forced Single: {forcedSingle}\n" +
            $"No SpriteRects: {noSpriteRects}\n" +
            $"Errors: {errors}\n\n" +
            $"Ensuite: lance l'AUDIT puis rebuild.",
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

    private static string ApplyOptionalHeroMapping(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return name;

        // If still have "hero_" somewhere, convert to "hero1_"
        if (name.StartsWith(From, StringComparison.OrdinalIgnoreCase))
            return To + name.Substring(From.Length);

        return name;
    }

    private static bool TryGetSelectedFolder(out string folderPath)
    {
        folderPath = null;
        var obj = Selection.activeObject;
        if (obj == null)
        {
            Debug.LogError("[SpriteNameFix] Select a folder in the Project window first.");
            return false;
        }

        folderPath = AssetDatabase.GetAssetPath(obj).Replace("\\", "/");
        if (!AssetDatabase.IsValidFolder(folderPath))
        {
            Debug.LogError($"[SpriteNameFix] Selected object is not a folder: {folderPath}");
            return false;
        }
        return true;
    }
}
#endif
