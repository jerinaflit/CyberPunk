#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Project.AnimationPipeline
{
    public static class AnimPipeline_ReportCharacters
    {
        [MenuItem("Tools/_Project/Animation Pipeline/0) Report Detected Characters", priority = 0)]
        public static void Report()
        {
            var settings = FindSettings();
            if (settings == null)
            {
                Debug.LogError("[AnimPipeline] Settings not found. Use: Tools/_Project/Animation Pipeline/1) Create/Select Settings");
                return;
            }

            var sprites = LoadAllSprites(settings.spriteRootFolders);
            var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            foreach (var s in sprites)
            {
                if (s == null) continue;
                if (!TryParseCharacter(s.name, out var character)) continue;

                if (!map.ContainsKey(character)) map[character] = 0;
                map[character]++;
            }

            if (map.Count == 0)
            {
                Debug.LogWarning("[AnimPipeline] Report: no characters detected. (Naming doesn't match Character_AnimName...)");
                return;
            }

            var lines = map
                .OrderByDescending(kv => kv.Value)
                .ThenBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase)
                .Select(kv => $"{kv.Key}  ->  {kv.Value} sprites");

            Debug.Log("[AnimPipeline] Detected Characters:\n" + string.Join("\n", lines));
            EditorUtility.DisplayDialog("Anim Pipeline Report", $"Characters detected: {map.Count}\n\nSee Console for full list.", "OK");
        }

        private static bool TryParseCharacter(string spriteName, out string character)
        {
            character = null;
            if (string.IsNullOrWhiteSpace(spriteName)) return false;

            int firstUnderscore = spriteName.IndexOf('_');
            if (firstUnderscore <= 0) return false;

            character = spriteName.Substring(0, firstUnderscore).Trim();
            return !string.IsNullOrWhiteSpace(character);
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

        private static AnimPipelineSettings FindSettings()
        {
            var guids = AssetDatabase.FindAssets($"t:{nameof(AnimPipelineSettings)}");
            if (guids.Length == 0) return null;
            return AssetDatabase.LoadAssetAtPath<AnimPipelineSettings>(AssetDatabase.GUIDToAssetPath(guids[0]));
        }
    }
}
#endif
