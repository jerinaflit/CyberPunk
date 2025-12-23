#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

public static class MaskTools
{
    const int MaxSize = 4096;

    [MenuItem("Tools/Project/Fix Selected Masks", priority = 20)]
    public static void FixSelectedMasks()
    {
        var guids = Selection.assetGUIDs;
        if (guids == null || guids.Length == 0)
        {
            EditorUtility.DisplayDialog("Fix Selected Masks", "Sélectionne au moins un asset (PNG mask).", "OK");
            return;
        }

        int changedCount = 0;
        int skippedCount = 0;

        AssetDatabase.StartAssetEditing();
        try
        {
            foreach (var guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (string.IsNullOrEmpty(path))
                {
                    skippedCount++;
                    continue;
                }

                var ti = AssetImporter.GetAtPath(path) as TextureImporter;
                if (ti == null)
                {
                    skippedCount++;
                    continue;
                }

                if (!LooksLikeMask(path))
                {
                    skippedCount++;
                    continue;
                }

                bool changed = ApplyMaskSettings(ti);

                if (changed)
                {
                    changedCount++;
                    AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
                }
            }
        }
        finally
        {
            AssetDatabase.StopAssetEditing();
            AssetDatabase.Refresh();
        }

        EditorUtility.DisplayDialog(
            "Fix Selected Masks",
            $"Terminé ✅\n\nMasks modifiés: {changedCount}\nIgnorés: {skippedCount}",
            "OK"
        );
    }

    static bool LooksLikeMask(string path)
    {
        string p = path.Replace('\\', '/').ToLowerInvariant();

        if (p.Contains("/art/masks/")) return true;

        if (p.EndsWith("_hot.png") || p.EndsWith("_walk.png")) return true;

        if (p.Contains("_mask") || p.Contains("_occ") || p.Contains("_trigger")) return true;

        return false;
    }

    static bool ApplyMaskSettings(TextureImporter ti)
    {
        bool changed = false;

        // ✅ MASK = DATA STRICTE
        if (ti.textureType != TextureImporterType.Default)
        {
            ti.textureType = TextureImporterType.Default;
            changed = true;
        }

        if (ti.spriteImportMode != SpriteImportMode.None)
        {
            ti.spriteImportMode = SpriteImportMode.None;
            changed = true;
        }

        if (!ti.isReadable)
        {
            ti.isReadable = true;
            changed = true;
        }

        if (ti.sRGBTexture != false)
        {
            ti.sRGBTexture = false;
            changed = true;
        }

        if (ti.mipmapEnabled != false)
        {
            ti.mipmapEnabled = false;
            changed = true;
        }

        if (ti.filterMode != FilterMode.Point)
        {
            ti.filterMode = FilterMode.Point;
            changed = true;
        }

        if (ti.npotScale != TextureImporterNPOTScale.None)
        {
            ti.npotScale = TextureImporterNPOTScale.None;
            changed = true;
        }

        if (ti.textureCompression != TextureImporterCompression.Uncompressed)
        {
            ti.textureCompression = TextureImporterCompression.Uncompressed;
            changed = true;
        }

        if (ti.maxTextureSize != MaxSize)
        {
            ti.maxTextureSize = MaxSize;
            changed = true;
        }

        // Pas de “help visuel” qui peut toucher les pixels
        if (ti.alphaIsTransparency != false)
        {
            ti.alphaIsTransparency = false;
            changed = true;
        }

        // Applique sur l'importer (important)
        if (changed)
            EditorUtility.SetDirty(ti);

        return changed;
    }
}
#endif
