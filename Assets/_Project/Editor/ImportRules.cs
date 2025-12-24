#if UNITY_EDITOR
using System;
using System.IO;
using UnityEditor;
using UnityEngine;

public sealed class ImportRules : AssetPostprocessor
{
    // ====== IDIOT-PROOF DEFAULTS ======
    // Point everywhere for sprites under _Project (you can flip this later if you add HD painted backgrounds).
    private const bool POINT_FOR_ALL_PROJECT_SPRITES = true;

    // Android defaults (safe + performant)
    private const int ANDROID_MAX_SIZE = 2048; // adjust later if needed
    private const int PC_MAX_SIZE = 4096;

    // Dev-friendly: keep sprites uncompressed in Editor for crisp preview & fast iteration.
    // Platforms will still get their overrides.
    private const TextureImporterCompression SPRITE_DEV_COMPRESSION = TextureImporterCompression.Uncompressed;

    // ====== PATH HELPERS ======
    private static string N(string path) => (path ?? "").Replace("\\", "/");

    private static bool IsUnderProject(string path)
        => N(path).IndexOf("Assets/_Project/", StringComparison.OrdinalIgnoreCase) >= 0;

    private static bool PathHas(string path, string token)
        => N(path).IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0;

    private static bool IsMaskTexture(string path)
    {
        // Strict by folder: anything under _Project/Masks is treated as DATA mask.
        return PathHas(path, "/_Project/Masks/");
    }

    private static bool IsSpriteTexture(string path)
    {
        // Our content art lives here:
        // - _Project/Art/...
        // - _Project/UI/...
        // - _Project/VFX/... (if you ever use sprites there)
        return PathHas(path, "/_Project/Art/") || PathHas(path, "/_Project/UI/") || PathHas(path, "/_Project/VFX/");
    }

    // ====== MAIN IMPORT HOOK ======
    void OnPreprocessTexture()
    {
        var importer = (TextureImporter)assetImporter;
        string path = N(importer.assetPath);

        // We only enforce rules for your project area.
        if (!IsUnderProject(path))
            return;

        // Ignore non-png/jpg/etc? TextureImporter is only for texture assets anyway.

        if (IsMaskTexture(path))
        {
            ApplyMaskRules(importer);
            return;
        }

        if (IsSpriteTexture(path))
        {
            ApplySpriteRules(importer, path);
            return;
        }

        // Otherwise: leave it alone (ex: incoming, misc, etc.)
    }

    // ====== RULES ======

    private static void ApplyMaskRules(TextureImporter ti)
    {
        // ✅ Masks = DATA STRICTE (as you defined)
        ti.textureType = TextureImporterType.Default;
        ti.spriteImportMode = SpriteImportMode.None;

        ti.isReadable = true;
        ti.sRGBTexture = false;
        ti.alphaIsTransparency = false;

        ti.filterMode = FilterMode.Point;
        ti.mipmapEnabled = false;

        ti.textureCompression = TextureImporterCompression.Uncompressed;
        ti.compressionQuality = 0;

        // NPOT = None
        ti.npotScale = TextureImporterNPOTScale.None;

        // Ensure no funky scaling
        ti.wrapMode = TextureWrapMode.Clamp;

        // Platform: keep uncompressed everywhere for deterministic pixel reads
        SetPlatformOverride(ti, "Android", true, ANDROID_MAX_SIZE, TextureImporterFormat.RGBA32);
        SetPlatformOverride(ti, "Standalone", true, PC_MAX_SIZE, TextureImporterFormat.RGBA32);
    }

    private static void ApplySpriteRules(TextureImporter ti, string path)
    {
        ti.textureType = TextureImporterType.Sprite;
        if (ti.spriteImportMode == SpriteImportMode.None)
            ti.spriteImportMode = SpriteImportMode.Single; // safe default

        // Common sprite rules
        ti.mipmapEnabled = false;
        ti.alphaIsTransparency = true;

        // sRGB ON for art sprites (normal)
        ti.sRGBTexture = true;

        // Filter
        if (POINT_FOR_ALL_PROJECT_SPRITES)
        {
            ti.filterMode = FilterMode.Point;
        }

        // Dev compression: none (crisp + fast)
        ti.textureCompression = SPRITE_DEV_COMPRESSION;
        ti.compressionQuality = 0;

        // NPOT = None (safe for pixel art; atlases will manage sizes later)
        ti.npotScale = TextureImporterNPOTScale.None;

        // Wrap for sprites usually clamp (avoids bleeding on edges)
        ti.wrapMode = TextureWrapMode.Clamp;

        // Platform overrides
        // PC: allow big, mostly automatic
        SetPlatformOverride(ti, "Standalone", true, PC_MAX_SIZE, TextureImporterFormat.Automatic);

        // Android: ASTC when possible (fallback ETC2), reasonable max size
        // Note: Unity will fallback if format not supported by device/build.
        SetPlatformOverride(ti, "Android", true, ANDROID_MAX_SIZE, TextureImporterFormat.ASTC_6x6);
    }

    private static void SetPlatformOverride(TextureImporter ti, string platform, bool overridden, int maxSize, TextureImporterFormat fmt)
    {
        var s = new TextureImporterPlatformSettings
        {
            name = platform,
            overridden = overridden,
            maxTextureSize = maxSize,
            format = fmt,
            compressionQuality = 50
        };
        ti.SetPlatformTextureSettings(s);
    }

    // ====== MENUS (IDIOT-PROOF “APPLY NOW”) ======
    [MenuItem("Tools/_Project/Import/Apply Import Rules Now (Selected Textures)")]
    private static void ApplyNow_Selected()
    {
        var objs = Selection.GetFiltered<UnityEngine.Object>(SelectionMode.Assets);
        int count = 0;

        foreach (var o in objs)
        {
            string p = AssetDatabase.GetAssetPath(o);
            p = N(p);
            if (string.IsNullOrEmpty(p)) continue;

            // If folder, reimport all textures inside
            if (AssetDatabase.IsValidFolder(p))
            {
                string[] guids = AssetDatabase.FindAssets("t:Texture2D", new[] { p });
                foreach (var g in guids)
                {
                    string ap = AssetDatabase.GUIDToAssetPath(g);
                    if (IsUnderProject(ap))
                    {
                        AssetDatabase.ImportAsset(ap, ImportAssetOptions.ForceUpdate);
                        count++;
                    }
                }
            }
            else
            {
                // single asset
                if (IsUnderProject(p))
                {
                    // Only textures
                    var ti = AssetImporter.GetAtPath(p) as TextureImporter;
                    if (ti != null)
                    {
                        AssetDatabase.ImportAsset(p, ImportAssetOptions.ForceUpdate);
                        count++;
                    }
                }
            }
        }

        Debug.Log($"[ImportRules] Reimport done (Selected). Textures reimported: {count}");
        EditorUtility.DisplayDialog("ImportRules", $"Reimport terminé.\nTextures: {count}", "OK");
    }

    [MenuItem("Tools/_Project/Import/Apply Import Rules Now (All _Project Textures)")]
    private static void ApplyNow_AllProject()
    {
        string root = "Assets/_Project";
        string[] guids = AssetDatabase.FindAssets("t:Texture2D", new[] { root });

        int count = 0;
        foreach (var g in guids)
        {
            string ap = AssetDatabase.GUIDToAssetPath(g);
            if (!IsUnderProject(ap)) continue;
            AssetDatabase.ImportAsset(ap, ImportAssetOptions.ForceUpdate);
            count++;
        }

        Debug.Log($"[ImportRules] Reimport done (All _Project). Textures reimported: {count}");
        EditorUtility.DisplayDialog("ImportRules", $"Reimport terminé.\nTextures: {count}", "OK");
    }
}
#endif
