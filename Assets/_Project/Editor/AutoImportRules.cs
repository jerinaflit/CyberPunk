#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEngine;

public class AutoImportRules : AssetPostprocessor
{
    const int DefaultPPU = 100;
    const int MaxSize = 4096;

    void OnPreprocessTexture()
    {
        string path = assetPath.Replace('\\', '/');

        // On ne touche qu’à notre espace projet
        if (!path.StartsWith("Assets/_Project/", StringComparison.OrdinalIgnoreCase))
            return;

        var ti = (TextureImporter)assetImporter;

        bool isMask =
            path.Contains("/Art/Masks/", StringComparison.OrdinalIgnoreCase) ||
            path.EndsWith("_hot.png", StringComparison.OrdinalIgnoreCase) ||
            path.EndsWith("_walk.png", StringComparison.OrdinalIgnoreCase) ||
            path.Contains("_mask", StringComparison.OrdinalIgnoreCase) ||
            path.Contains("_occ", StringComparison.OrdinalIgnoreCase) ||
            path.Contains("_trigger", StringComparison.OrdinalIgnoreCase);

        bool isUI = path.Contains("/Art/UI/", StringComparison.OrdinalIgnoreCase);
        bool isCharacter = path.Contains("/Art/Characters/", StringComparison.OrdinalIgnoreCase);

        // Base safe defaults
        ti.mipmapEnabled = false;
        ti.textureCompression = TextureImporterCompression.Uncompressed;
        ti.maxTextureSize = MaxSize;
        ti.npotScale = TextureImporterNPOTScale.None;

        // Pixel look (on préfère Point quasi partout dans Art)
        bool isPixelArt = path.Contains("/Art/", StringComparison.OrdinalIgnoreCase);
        if (isPixelArt)
            ti.filterMode = FilterMode.Point;

        if (isMask)
        {
            // ✅ MASK = DATA STRICTE
            ti.textureType = TextureImporterType.Default;
            ti.spriteImportMode = SpriteImportMode.None;

            ti.isReadable = true;      // obligatoire pour GetPixel/GetPixels
            ti.sRGBTexture = false;    // pas de conversion gamma -> couleurs exactes

            // IMPORTANT: on évite tout “help” visuel qui peut toucher les pixels
            ti.alphaIsTransparency = false;
        }
        else
        {
            // ✅ ART/UI/FRAMES = SPRITES
            ti.textureType = TextureImporterType.Sprite;
            ti.spriteImportMode = SpriteImportMode.Single;
            ti.spritePixelsPerUnit = DefaultPPU;

            ti.isReadable = false;   // par défaut
            ti.sRGBTexture = true;   // art en sRGB

            // Là c’est utile: réduit les halos sur sprites
            ti.alphaIsTransparency = true;
        }
    }

    void OnPreprocessAudio()
    {
        string path = assetPath.Replace('\\', '/');
        if (!path.StartsWith("Assets/_Project/Audio/", StringComparison.OrdinalIgnoreCase))
            return;

        var ai = (AudioImporter)assetImporter;

        var s = ai.defaultSampleSettings;
        s.compressionFormat = AudioCompressionFormat.Vorbis;
        s.quality = 0.8f;

        // SFX: décompressé (réactif)
        s.loadType = AudioClipLoadType.DecompressOnLoad;

        // VO / Music / Ambience: streaming (Android friendly)
        if (path.Contains("/VO/", StringComparison.OrdinalIgnoreCase) ||
            path.Contains("/Music/", StringComparison.OrdinalIgnoreCase) ||
            path.Contains("/Ambience/", StringComparison.OrdinalIgnoreCase))
        {
            s.loadType = AudioClipLoadType.Streaming;
            s.quality = 0.75f;
        }

        ai.defaultSampleSettings = s;
    }
}
#endif
