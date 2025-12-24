#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Project.AnimationPipeline
{
    [InitializeOnLoad]
    public static class AnimPipelineProjectValidator
    {
        static AnimPipelineProjectValidator()
        {
            EditorApplication.delayCall += Validate;
        }

        [MenuItem("Tools/_Project/Animation Pipeline/4) Validate Pipeline Output", priority = 4)]
        public static void Validate()
        {
            var settings = FindSettings();
            if (settings == null) return;

            var sprites = LoadAllSprites(settings.spriteRootFolders);
            var groups = GroupSpritesFlexible(sprites);

            var issues = new List<string>();

            foreach (var character in groups.Keys.OrderBy(k => k, StringComparer.OrdinalIgnoreCase))
            {
                string outFolder = Path.Combine(settings.outputRootFolder, character).Replace("\\", "/");

                string controllerPath = Path.Combine(outFolder, $"{character}.controller").Replace("\\", "/");
                if (AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(controllerPath) == null)
                    issues.Add($"Missing controller for '{character}' => {controllerPath}");

                foreach (var animName in groups[character].Keys.OrderBy(k => k, StringComparer.OrdinalIgnoreCase))
                {
                    string safeAnim = SanitizeFilePart(animName);
                    string clipPath = Path.Combine(outFolder, $"{character}_{safeAnim}.anim").Replace("\\", "/");

                    var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(clipPath);
                    if (clip == null)
                    {
                        issues.Add($"Missing clip '{animName}' for '{character}' => {clipPath}");
                        continue;
                    }

                    if (CountSpriteKeys(clip) == 0)
                        issues.Add($"Clip has 0 sprite keys (broken) => {clipPath}");
                }
            }

            if (issues.Count == 0)
            {
                Debug.Log("[AnimPipeline] Validation OK.");
                return;
            }

            // format demandé : 1 phrase diagnostic + correction exacte
            Debug.LogError(
                $"[AnimPipeline] Validation FAILED: {issues.Count} issue(s).\n" +
                "Fix: run Tools/_Project/Animation Pipeline/2) Build All (Alphabet Default)\n" +
                "Details:\n- " + string.Join("\n- ", issues)
            );
        }

        private static AnimPipelineSettings FindSettings()
        {
            var guids = AssetDatabase.FindAssets($"t:{nameof(AnimPipelineSettings)}");
            if (guids.Length == 0) return null;
            return AssetDatabase.LoadAssetAtPath<AnimPipelineSettings>(AssetDatabase.GUIDToAssetPath(guids[0]));
        }

        private static List<Sprite> LoadAllSprites(string[] rootFolders)
        {
            var list = new List<Sprite>();
            if (rootFolders == null) return list;

            foreach (var folder in rootFolders.Where(f => !string.IsNullOrWhiteSpace(f)))
            {
                if (!AssetDatabase.IsValidFolder(folder)) continue;

                var guids = AssetDatabase.FindAssets("t:Sprite", new[] { folder });
                foreach (var guid in guids)
                {
                    var path = AssetDatabase.GUIDToAssetPath(guid);
                    var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
                    if (sprite != null) list.Add(sprite);
                }
            }
            return list;
        }

        private static Dictionary<string, Dictionary<string, List<Sprite>>> GroupSpritesFlexible(List<Sprite> sprites)
        {
            var result = new Dictionary<string, Dictionary<string, List<Sprite>>>(StringComparer.OrdinalIgnoreCase);

            foreach (var s in sprites)
            {
                if (s == null) continue;
                if (!TryParseFlexible(s.name, out var character, out var animName)) continue;

                if (!result.TryGetValue(character, out var dict))
                {
                    dict = new Dictionary<string, List<Sprite>>(StringComparer.OrdinalIgnoreCase);
                    result[character] = dict;
                }

                if (!dict.TryGetValue(animName, out var list))
                {
                    list = new List<Sprite>();
                    dict[animName] = list;
                }

                list.Add(s);
            }

            return result;
        }

        private static bool TryParseFlexible(string spriteName, out string character, out string animName)
        {
            character = null;
            animName = null;

            int firstUnderscore = spriteName.IndexOf('_');
            if (firstUnderscore <= 0) return false;

            character = spriteName.Substring(0, firstUnderscore).Trim();
            if (string.IsNullOrWhiteSpace(character)) return false;

            int lastDigit = spriteName.Length - 1;
            while (lastDigit >= 0 && char.IsDigit(spriteName[lastDigit])) lastDigit--;
            bool hasTrailingDigits = lastDigit < spriteName.Length - 1;

            if (hasTrailingDigits)
            {
                int underscoreBeforeDigits = spriteName.LastIndexOf('_', lastDigit);
                if (underscoreBeforeDigits > firstUnderscore)
                    animName = spriteName.Substring(firstUnderscore + 1, underscoreBeforeDigits - (firstUnderscore + 1));
                else
                    animName = spriteName.Substring(firstUnderscore + 1);
            }
            else
            {
                animName = spriteName.Substring(firstUnderscore + 1);
            }

            animName = (animName ?? "").Trim();
            return !string.IsNullOrWhiteSpace(animName);
        }

        private static int CountSpriteKeys(AnimationClip clip)
        {
            foreach (var b in AnimationUtility.GetObjectReferenceCurveBindings(clip))
            {
                if (b.type == typeof(SpriteRenderer) && b.propertyName == "m_Sprite")
                {
                    var curve = AnimationUtility.GetObjectReferenceCurve(clip, b);
                    return curve != null ? curve.Length : 0;
                }
            }
            return 0;
        }

        private static string SanitizeFilePart(string s)
        {
            var invalid = Path.GetInvalidFileNameChars();
            var cleaned = new string(s.Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray());
            return cleaned.Replace(' ', '_');
        }
    }
}
#endif
