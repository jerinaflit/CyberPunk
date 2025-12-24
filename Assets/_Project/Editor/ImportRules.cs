#if UNITY_EDITOR
using System;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;

public sealed class ImportRules : AssetPostprocessor
{
    // ====== IDIOT-PROOF DEFAULTS ======
    private const bool POINT_FOR_ALL_PROJECT_SPRITES = true;

    private const int ANDROID_MAX_SIZE = 2048;
    private const int PC_MAX_SIZE = 4096;

    private const TextureImporterCompression SPRITE_DEV_COMPRESSION = TextureImporterCompression.Uncompressed;

    // ====== CHARACTER FRAMES RULES ======
    // If your character frames are individual PNG files, they MUST be Single sprites (not Multiple).
    // This prevents hidden SpriteSheet metadata names and jitter.
    private static readonly Vector2 CHARACTER_PIVOT = new Vector2(0.5f, 0.5f); // Center (safe default)

    // ====== PATH HELPERS ======
    private static string N(string path) => (path ?? "").Replace("\\", "/");

    private static bool IsUnderProject(string path)
        => N(path).IndexOf("Assets/_Project/", StringComparison.OrdinalIgnoreCase) >= 0;

    private static bool PathHas(string path, string token)
        => N(path).IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0;

    private static bool IsMaskTexture(string path)
    {
        // Masks live under Art/Masks in this project
        return PathHas(path, "/_Project/Art/Masks/") || PathHas(path, "/_Project/Masks/");
    }

    private static bool IsSpriteTexture(string path)
    {
        return PathHas(path, "/_Project/Art/") || PathHas(path, "/_Project/UI/") || PathHas(path, "/_Project/VFX/");
    }

    private static bool IsCharacterFrameTexture(string path)
    {
        // Adjust if your characters live elsewhere.
        // IMPORTANT: we only apply the strict Single-sprite rule for character frame PNGs.
        if (!PathHas(path, "/_Project/Art/Characters/")) return false;
        if (!path.EndsWith(".png", StringComparison.OrdinalIgnoreCase)) return false;
        return true;
    }

    private static string FileNameNoExt(string path)
        => Path.GetFileNameWithoutExtension(N(path));

    private static void TrySetSpriteName(TextureImporter ti, string spriteName)
    {
        // Some Unity versions don't expose TextureImporter.spriteName; in those cases the Sprite name defaults to the filename.
        var prop = typeof(TextureImporter).GetProperty("spriteName", BindingFlags.Instance | BindingFlags.Public);
        if (prop != null && prop.CanWrite)
            prop.SetValue(ti, spriteName, null);
    }

    // ====== MAIN IMPORT HOOK ======
    private void OnPreprocessTexture()
    {
        string path = N(assetPath);
        if (!IsUnderProject(path)) return;

        var importer = (TextureImporter)assetImporter;
        if (importer == null) return;

        if (IsMaskTexture(path))
        {
            ApplyMaskRules(importer);
            return;
        }

        if (IsSpriteTexture(path))
        {
            // ✅ First, apply strict character-frame rule (prevents hidden spritesheet name/pivot issues)
            if (IsCharacterFrameTexture(path))
                ApplyCharacterFrameRules(importer, path);

            // ✅ Then apply general sprite rules (filter, compression, platform overrides, etc.)
            ApplySpriteRules(importer, path);
            return;
        }

        // Otherwise: leave it alone (incoming, misc, etc.)
    }

    // ====== RULES ======

    private static void ApplyMaskRules(TextureImporter ti)
    {
        ti.textureType = TextureImporterType.Default;
        ti.spriteImportMode = SpriteImportMode.None;

        ti.isReadable = true;
        ti.sRGBTexture = false;
        ti.alphaIsTransparency = false;

        ti.filterMode = FilterMode.Point;
        ti.mipmapEnabled = false;

        ti.textureCompression = TextureImporterCompression.Uncompressed;
        ti.compressionQuality = 0;

        ti.npotScale = TextureImporterNPOTScale.None;
        ti.wrapMode = TextureWrapMode.Clamp;

        SetPlatformOverride(ti, "Standalone", true, PC_MAX_SIZE, TextureImporterFormat.Automatic);
        SetPlatformOverride(ti, "Android", true, ANDROID_MAX_SIZE, TextureImporterFormat.ASTC_6x6);
    }

    private static void ApplyCharacterFrameRules(TextureImporter ti, string path)
    {
        // Force Sprite type
        ti.textureType = TextureImporterType.Sprite;
        
        // [MODIFIED] Relaxed rules to allow manual overrides (Multiple mode, custom pivots, etc.)

        // ti.spriteImportMode = SpriteImportMode.Single;

        // string expectedName = FileNameNoExt(path);
        // TrySetSpriteName(ti, expectedName);

        // var settings = new TextureImporterSettings();
        // ti.ReadTextureSettings(settings);
        // settings.spriteAlignment = (int)SpriteAlignment.Center;
        // settings.spritePivot = CHARACTER_PIVOT;
        // ti.SetTextureSettings(settings);

        // #pragma warning disable CS0618
        // ti.spritesheet = Array.Empty<SpriteMetaData>();
        // #pragma warning restore CS0618

        // Safe defaults for frame sprites
        ti.mipmapEnabled = false;
        ti.alphaIsTransparency = true;
        ti.wrapMode = TextureWrapMode.Clamp;
        ti.npotScale = TextureImporterNPOTScale.None;
    }

    private static void ApplySpriteRules(TextureImporter ti, string path)
    {
        ti.textureType = TextureImporterType.Sprite;

        // NOTE: Do NOT force spriteImportMode here globally,
        // because UI sheets or packed sprites might legitimately be Multiple.
        // Character frames are handled in ApplyCharacterFrameRules().

        ti.isReadable = false;

        ti.alphaIsTransparency = true;
        ti.mipmapEnabled = false;

        ti.sRGBTexture = true;

        if (POINT_FOR_ALL_PROJECT_SPRITES)
            ti.filterMode = FilterMode.Point;

        ti.textureCompression = SPRITE_DEV_COMPRESSION;
        ti.compressionQuality = 0;

        ti.npotScale = TextureImporterNPOTScale.None;
        ti.wrapMode = TextureWrapMode.Clamp;

        SetPlatformOverride(ti, "Standalone", true, PC_MAX_SIZE, TextureImporterFormat.Automatic);
        SetPlatformOverride(ti, "Android", true, ANDROID_MAX_SIZE, TextureImporterFormat.ASTC_6x6);
    }

    private static void SetPlatformOverride(TextureImporter ti, string platform, bool overridden, int maxSize, TextureImporterFormat fmt)
    {
        var settings = ti.GetPlatformTextureSettings(platform);
        settings.overridden = overridden;
        settings.maxTextureSize = maxSize;
        settings.format = fmt;
        ti.SetPlatformTextureSettings(settings);
    }

    // ====== OPTIONAL: Manual reimport utility (keep if you had it; safe) ======
    [MenuItem("Tools/_Project/Import/Reimport Textures In Selection")]
    private static void ReimportSelection()
    {
        var objs = Selection.objects;
        if (objs == null || objs.Length == 0)
        {
            Debug.LogWarning("[ImportRules] Select a folder or textures to reimport.");
            return;
        }

        AssetDatabase.StartAssetEditing();
        try
        {
            foreach (var o in objs)
            {
                string p = AssetDatabase.GetAssetPath(o);
                p = N(p);
                if (string.IsNullOrEmpty(p)) continue;

                if (AssetDatabase.IsValidFolder(p))
                {
                    string[] guids = AssetDatabase.FindAssets("t:Texture2D", new[] { p });
                    foreach (var g in guids)
                    {
                        string ap = AssetDatabase.GUIDToAssetPath(g);
                        AssetDatabase.ImportAsset(ap, ImportAssetOptions.ForceUpdate);
                    }
                }
                else
                {
                    AssetDatabase.ImportAsset(p, ImportAssetOptions.ForceUpdate);
                }
            }
        }
        finally
        {
            AssetDatabase.StopAssetEditing();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }

        Debug.Log("[ImportRules] Reimport done.");
    }
}
#endif
