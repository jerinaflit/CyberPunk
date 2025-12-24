#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEditor.U2D.Sprites;
using UnityEngine;

public class SpriteNameSanitizerPostprocessor : AssetPostprocessor
{
    private const string Prefix = "char_";

    private static SpriteDataProviderFactories _spriteDataProviderFactories;

    void OnPreprocessTexture()
    {
        if (!assetPath.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
            return;

        var importer = (TextureImporter)assetImporter;
        if (importer == null)
            return;

        // Only sprites
        if (importer.textureType != TextureImporterType.Sprite)
            return;

        // 🔴 THIS is the critical part:
        // Unity uses the SOURCE NAME when slicing sub-sprites.
        var fileName = System.IO.Path.GetFileNameWithoutExtension(assetPath);

        if (!fileName.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase))
            return;

        var cleanName = fileName.Substring(Prefix.Length);

        // TextureImporter has no 'spriteName' property; to affect sub-sprite names (Multiple mode),
        // rewrite the spritesheet metadata names before import.
        if (importer.spriteImportMode == SpriteImportMode.Multiple)
        {
            // Prefer Unity 6 Sprite Editor provider API.
            try
            {
                var dataProvider = GetSpriteDataProvider(importer);
                var spriteRects = dataProvider.GetSpriteRects();
                if (spriteRects != null && spriteRects.Length > 0)
                {
                    var changed = false;

                    for (int i = 0; i < spriteRects.Length; i++)
                    {
                        var original = spriteRects[i].name ?? string.Empty;

                        if (original.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase))
                        {
                            spriteRects[i].name = original.Substring(Prefix.Length);
                            changed = true;
                        }
                        else if (original.StartsWith(fileName, StringComparison.OrdinalIgnoreCase))
                        {
                            spriteRects[i].name = cleanName + original.Substring(fileName.Length);
                            changed = true;
                        }
                    }

                    if (changed)
                    {
                        dataProvider.SetSpriteRects(spriteRects);
                        dataProvider.Apply();
                    }

                    return;
                }
            }
            catch
            {
                // Ignore and fallback below.
            }

            // Fallback: older metadata API (suppressed warnings). Useful if rects aren't available during preprocess.
#pragma warning disable CS0618
            var sheet = importer.spritesheet;
#pragma warning restore CS0618
            if (sheet == null || sheet.Length == 0)
                return;

            var fallbackChanged = false;
            for (int i = 0; i < sheet.Length; i++)
            {
                var meta = sheet[i];
                var original = meta.name ?? string.Empty;

                if (original.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase))
                {
                    meta.name = original.Substring(Prefix.Length);
                    fallbackChanged = true;
                }
                else if (original.StartsWith(fileName, StringComparison.OrdinalIgnoreCase))
                {
                    meta.name = cleanName + original.Substring(fileName.Length);
                    fallbackChanged = true;
                }

                sheet[i] = meta;
            }

            if (fallbackChanged)
            {
#pragma warning disable CS0618
                importer.spritesheet = sheet;
#pragma warning restore CS0618
            }
        }
    }

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
