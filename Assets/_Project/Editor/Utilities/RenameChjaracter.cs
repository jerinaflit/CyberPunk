#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.U2D.Sprites;
using UnityEngine;

public static class RenameCharacterPrefix_HeroToHero1
{
    private const string From = "hero_";
    private const string To = "hero1_";

    [MenuItem("Tools/_Project/Utilities/Sprites/Rename prefix 'hero_' -> 'hero1_' (Selected Folder)")]
    public static void RenameSelectedFolder()
    {
        var obj = Selection.activeObject;
        if (obj == null)
        {
            Debug.LogError("[RenameHero] Select a folder in the Project window first.");
            return;
        }

        var root = AssetDatabase.GetAssetPath(obj).Replace("\\", "/");
        if (!AssetDatabase.IsValidFolder(root))
        {
            Debug.LogError($"[RenameHero] Selected object is not a folder: {root}");
            return;
        }

        RenameInFolder(root);
    }

    private static void RenameInFolder(string rootFolder)
    {
        // 1) Rename file assets (.png) that start with hero_
        var textureGuids = AssetDatabase.FindAssets("t:Texture2D", new[] { rootFolder });

        int texturesScanned = 0;
        int filesRenamed = 0;
        int importersChanged = 0;
        int subSpritesRenamed = 0;
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

                texturesScanned++;

                // Rename file (asset) if needed
                var fileName = Path.GetFileNameWithoutExtension(path);
                if (fileName.StartsWith(From, StringComparison.OrdinalIgnoreCase))
                {
                    var newName = To + fileName.Substring(From.Length);
                    var err = AssetDatabase.RenameAsset(path, newName);
                    if (!string.IsNullOrEmpty(err))
                    {
                        errors++;
                        Debug.LogError($"[RenameHero] RenameAsset failed: {path} -> {newName} | {err}");
                        continue;
                    }
                    filesRenamed++;

                    // Update path after rename (Unity keeps same GUID, path changes)
                    path = Path.Combine(Path.GetDirectoryName(path) ?? "", newName + ".png").Replace("\\", "/");
                }

                // 2) Fix SpriteSheet metadata names (sub-sprites) if any
                var importer = AssetImporter.GetAtPath(path) as TextureImporter;
                if (importer == null) continue;

                bool anyChange = false;

                if (importer.spriteImportMode == SpriteImportMode.Multiple)
                {
                    var dataProvider = GetSpriteDataProvider(importer);
                    var spriteRects = dataProvider.GetSpriteRects();
                    if (spriteRects != null && spriteRects.Length > 0)
                    {
                        for (int i = 0; i < spriteRects.Length; i++)
                        {
                            var name = spriteRects[i].name;
                            if (string.IsNullOrWhiteSpace(name)) continue;

                            if (name.StartsWith(From, StringComparison.OrdinalIgnoreCase))
                            {
                                spriteRects[i].name = To + name.Substring(From.Length);
                                anyChange = true;
                                subSpritesRenamed++;
                            }
                        }

                        if (anyChange)
                        {
                            dataProvider.SetSpriteRects(spriteRects);
                            dataProvider.Apply();
                            EditorUtility.SetDirty(importer);
                            importersChanged++;
                            toReimport.Add(path);
                        }
                    }
                }
            }
        }
        finally
        {
            AssetDatabase.StopAssetEditing();
        }

        // Deterministic reimport outside batch
        foreach (var p in toReimport.Distinct(StringComparer.OrdinalIgnoreCase))
            AssetDatabase.ImportAsset(p, ImportAssetOptions.ForceUpdate);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log(
            $"[RenameHero] Done. Folder={rootFolder}\n" +
            $"TexturesScanned={texturesScanned}\n" +
            $"FilesRenamed={filesRenamed}\n" +
            $"ImportersChanged={importersChanged}\n" +
            $"SubSpritesRenamed={subSpritesRenamed}\n" +
            $"Errors={errors}"
        );

        EditorUtility.DisplayDialog(
            "Rename hero_ -> hero1_",
            $"Terminé ✅\n\nFolder:\n{rootFolder}\n\n" +
            $"Textures scanned: {texturesScanned}\n" +
            $"Files renamed: {filesRenamed}\n" +
            $"Importers changed: {importersChanged}\n" +
            $"Sub-sprites renamed: {subSpritesRenamed}\n" +
            $"Errors: {errors}",
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
}
#endif
