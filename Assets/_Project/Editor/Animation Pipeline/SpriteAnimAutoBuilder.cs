#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace Project.AnimationPipeline
{
    public static class SpriteAnimAutoBuilder
    {
        private const string SettingsAssetName = "AnimPipelineSettings";

        // ---------------- MENUS ----------------

        [MenuItem("Tools/_Project/Animation Pipeline/1) Create/Select Settings", priority = 1)]
        public static void CreateOrSelectSettings()
        {
            var settings = FindSettings();
            if (settings != null)
            {
                Selection.activeObject = settings;
                EditorGUIUtility.PingObject(settings);
                return;
            }

            EnsureFolder("Assets/_Project/Settings");
            var assetPath = "Assets/_Project/Settings/AnimPipelineSettings.asset";

            settings = ScriptableObject.CreateInstance<AnimPipelineSettings>();
            AssetDatabase.CreateAsset(settings, assetPath);
            AssetDatabase.SaveAssets();

            Selection.activeObject = settings;
            EditorGUIUtility.PingObject(settings);

            Debug.Log($"[AnimPipeline] Settings created: {assetPath}");
        }

        [MenuItem("Tools/_Project/Animation Pipeline/2) Build All (Alphabet Default)", priority = 2)]
        public static void BuildAll()
        {
            if (EditorApplication.isPlaying)
            {
                EditorUtility.DisplayDialog("Anim Pipeline", "Stop Play Mode avant de builder.", "OK");
                return;
            }

            var settings = RequireSettings();
            if (settings == null) return;

            if (settings.spriteRootFolders == null || settings.spriteRootFolders.Length == 0)
            {
                Debug.LogError("[AnimPipeline] Settings spriteRootFolders is empty.");
                return;
            }

            EnsureFolder(settings.outputRootFolder);

            var sprites = LoadAllSprites(settings.spriteRootFolders);
            if (sprites.Count == 0)
            {
                Debug.LogWarning("[AnimPipeline] No sprites found in the configured folders.");
                return;
            }

            // Group: Character -> AnimName -> sprites
            var groups = GroupSpritesFlexible(sprites);

            int clipsTouched = 0;
            int controllersTouched = 0;
            var report = new List<string>();

            AssetDatabase.StartAssetEditing();
            try
            {
                foreach (var character in groups.Keys.OrderBy(k => k, StringComparer.OrdinalIgnoreCase))
                {
                    var charGroup = groups[character];

                    // Build clips for each animName
                    var clipByAnim = new Dictionary<string, AnimationClip>(StringComparer.OrdinalIgnoreCase);

                    foreach (var animName in charGroup.Keys.OrderBy(k => k, StringComparer.OrdinalIgnoreCase))
                    {
                        var orderedSprites = charGroup[animName]
                            .OrderBy(sp => ExtractFrameIndexSafe(sp.name))
                            .ThenBy(sp => sp.name, StringComparer.OrdinalIgnoreCase)
                            .ToArray();

                        if (orderedSprites.Length == 0) continue;

                        var clip = CreateOrUpdateClip_Bulletproof(settings, character, animName, orderedSprites);
                        if (clip != null)
                        {
                            clipByAnim[animName] = clip;
                            clipsTouched++;
                        }
                    }

                    if (clipByAnim.Count > 0)
                    {
                        var controller = CreateOrUpdateAnimator_AlphabetDefault(settings, character, clipByAnim);
                        if (controller != null)
                        {
                            controllersTouched++;
                            report.Add($"- {character}: Clips={clipByAnim.Count} Controller=OK");
                        }
                    }
                    else
                    {
                        report.Add($"- {character}: Clips=0 (no valid sprites matched naming)");
                    }
                }
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
            }

            Debug.Log($"[AnimPipeline] Done. Clips created/updated: {clipsTouched}, Controllers created/updated: {controllersTouched}\n" +
                      string.Join("\n", report));

            EditorUtility.DisplayDialog(
                "Anim Pipeline",
                $"Terminé ✅\n\nClips: {clipsTouched}\nControllers: {controllersTouched}\n\nDétails dans la Console.",
                "OK"
            );
        }

        [MenuItem("Tools/_Project/Animation Pipeline/3) Assign Controller to Selected GameObjects", priority = 3)]
        public static void AssignToSelected()
        {
            var settings = RequireSettings();
            if (settings == null) return;

            var selected = Selection.gameObjects;
            if (selected == null || selected.Length == 0)
            {
                Debug.LogWarning("[AnimPipeline] Select one or more GameObjects in the Hierarchy first.");
                return;
            }

            int assigned = 0;

            foreach (var go in selected)
            {
                var spriteRenderer = go.GetComponent<SpriteRenderer>();
                if (spriteRenderer == null)
                {
                    Debug.LogWarning($"[AnimPipeline] '{go.name}' has no SpriteRenderer. Skipped.");
                    continue;
                }

                var animator = go.GetComponent<Animator>();
                if (animator == null) animator = go.AddComponent<Animator>();

                var controllerPath = Path.Combine(settings.outputRootFolder, go.name, $"{go.name}.controller").Replace("\\", "/");
                var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(controllerPath);
                if (controller == null)
                {
                    Debug.LogError($"[AnimPipeline] Controller not found for '{go.name}'. Expected: {controllerPath}. Build first.");
                    continue;
                }

                Undo.RecordObject(animator, "Assign Animator Controller");
                animator.runtimeAnimatorController = controller;
                EditorUtility.SetDirty(animator);

                assigned++;
            }

            Debug.Log($"[AnimPipeline] Assigned controllers: {assigned}");
        }

        // ---------------- CORE ----------------

        private static AnimPipelineSettings RequireSettings()
        {
            var s = FindSettings();
            if (s == null)
            {
                Debug.LogError("[AnimPipeline] Settings not found. Use: Tools/_Project/Animation Pipeline/1) Create/Select Settings");
                return null;
            }
            return s;
        }

        private static AnimPipelineSettings FindSettings()
        {
            var guids = AssetDatabase.FindAssets($"t:{nameof(AnimPipelineSettings)} {SettingsAssetName}");
            foreach (var guid in guids)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var s = AssetDatabase.LoadAssetAtPath<AnimPipelineSettings>(path);
                if (s != null) return s;
            }

            guids = AssetDatabase.FindAssets($"t:{nameof(AnimPipelineSettings)}");
            if (guids.Length == 0) return null;
            return AssetDatabase.LoadAssetAtPath<AnimPipelineSettings>(AssetDatabase.GUIDToAssetPath(guids[0]));
        }

        private static List<Sprite> LoadAllSprites(string[] rootFolders)
        {
            var list = new List<Sprite>();
            var folders = rootFolders.Where(f => !string.IsNullOrWhiteSpace(f)).ToArray();

            foreach (var folder in folders)
            {
                if (!AssetDatabase.IsValidFolder(folder))
                {
                    Debug.LogWarning($"[AnimPipeline] Folder not found: {folder}");
                    continue;
                }

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

        // Naming: Character_AnimName_###  (### optional)
        private static Dictionary<string, Dictionary<string, List<Sprite>>> GroupSpritesFlexible(List<Sprite> sprites)
        {
            var result = new Dictionary<string, Dictionary<string, List<Sprite>>>(StringComparer.OrdinalIgnoreCase);

            foreach (var s in sprites)
            {
                if (s == null) continue;

                if (!TryParseFlexible(s.name, out var character, out var animName))
                    continue;

                if (!result.TryGetValue(character, out var animDict))
                {
                    animDict = new Dictionary<string, List<Sprite>>(StringComparer.OrdinalIgnoreCase);
                    result[character] = animDict;
                }

                if (!animDict.TryGetValue(animName, out var list))
                {
                    list = new List<Sprite>();
                    animDict[animName] = list;
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

            // detect trailing digits
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

        private static int ExtractFrameIndexSafe(string spriteName)
        {
            int i = spriteName.Length - 1;
            while (i >= 0 && char.IsDigit(spriteName[i])) i--;
            var digits = spriteName.Substring(i + 1);
            if (int.TryParse(digits, out var n)) return n;
            return 0;
        }

        private static AnimationClip CreateOrUpdateClip_Bulletproof(AnimPipelineSettings settings, string character, string animName, Sprite[] orderedSprites)
        {
            var folder = Path.Combine(settings.outputRootFolder, character).Replace("\\", "/");
            EnsureFolder(folder);

            var safeAnim = SanitizeFilePart(animName);
            var clipPath = Path.Combine(folder, $"{character}_{safeAnim}.anim").Replace("\\", "/");

            // Strategy anti-clips “fantômes” :
            // 1) si existe: clear curves + rewrite
            // 2) vérifie keycount; si 0 -> delete + recreate once
            AnimationClip clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(clipPath);
            bool isNew = false;

            if (clip == null)
            {
                clip = new AnimationClip();
                isNew = true;
            }

            WriteSpriteKeys(clip, orderedSprites, settings.defaultFrameRate);
            SetLoop(clip, settings.loopAllClips);
            EditorUtility.SetDirty(clip);

            if (isNew)
            {
                AssetDatabase.CreateAsset(clip, clipPath);
            }

            // Hard verify
            int keys = CountSpriteKeys(clip);
            if (keys <= 0)
            {
                // Force recreate once
                AssetDatabase.DeleteAsset(clipPath);

                var newClip = new AnimationClip();
                WriteSpriteKeys(newClip, orderedSprites, settings.defaultFrameRate);
                SetLoop(newClip, settings.loopAllClips);
                EditorUtility.SetDirty(newClip);

                AssetDatabase.CreateAsset(newClip, clipPath);

                int keys2 = CountSpriteKeys(newClip);
                if (keys2 <= 0)
                {
                    Debug.LogError($"[AnimPipeline] BUG: Clip '{clipPath}' has 0 m_Sprite keys even after recreate.");
                    return null;
                }
                return newClip;
            }

            return clip;
        }

        private static void WriteSpriteKeys(AnimationClip clip, Sprite[] sprites, float fps)
        {
            // clear existing curves
            foreach (var b in AnimationUtility.GetObjectReferenceCurveBindings(clip))
                AnimationUtility.SetObjectReferenceCurve(clip, b, null);

            clip.frameRate = fps;

            var binding = new EditorCurveBinding
            {
                type = typeof(SpriteRenderer),
                path = "",
                propertyName = "m_Sprite"
            };

            float dt = 1f / fps;
            var keys = new ObjectReferenceKeyframe[sprites.Length];

            for (int i = 0; i < sprites.Length; i++)
            {
                keys[i] = new ObjectReferenceKeyframe
                {
                    time = i * dt,
                    value = sprites[i]
                };
            }

            AnimationUtility.SetObjectReferenceCurve(clip, binding, keys);
        }

        private static void SetLoop(AnimationClip clip, bool loop)
        {
            var s = AnimationUtility.GetAnimationClipSettings(clip);
            s.loopTime = loop;
            AnimationUtility.SetAnimationClipSettings(clip, s);
        }

        private static int CountSpriteKeys(AnimationClip clip)
        {
            var bindings = AnimationUtility.GetObjectReferenceCurveBindings(clip);
            foreach (var b in bindings)
            {
                if (b.type == typeof(SpriteRenderer) && b.propertyName == "m_Sprite")
                {
                    var curve = AnimationUtility.GetObjectReferenceCurve(clip, b);
                    return curve != null ? curve.Length : 0;
                }
            }
            return 0;
        }

        private static AnimatorController CreateOrUpdateAnimator_AlphabetDefault(AnimPipelineSettings settings, string character, Dictionary<string, AnimationClip> clipByAnim)
        {
            var folder = Path.Combine(settings.outputRootFolder, character).Replace("\\", "/");
            EnsureFolder(folder);

            var controllerPath = Path.Combine(folder, $"{character}.controller").Replace("\\", "/");
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(controllerPath);

            bool isNew = false;
            if (controller == null)
            {
                controller = AnimatorController.CreateAnimatorControllerAtPath(controllerPath);
                isNew = true;
            }

            // Ensure int param
            if (!controller.parameters.Any(p => p.type == AnimatorControllerParameterType.Int && p.name == settings.animIdInt))
                controller.AddParameter(settings.animIdInt, AnimatorControllerParameterType.Int);

            var sm = controller.layers[0].stateMachine;

            // Ensure states for each anim
            var orderedAnimNames = clipByAnim.Keys
                .OrderBy(k => k, StringComparer.OrdinalIgnoreCase)
                .ToList();

            string defaultAnim = orderedAnimNames[0]; // ✅ ALPHABET DEFAULT (no assumptions)

            var existingStates = sm.states
                .Where(s => s.state != null)
                .ToDictionary(s => s.state.name, s => s.state, StringComparer.OrdinalIgnoreCase);

            float x = 250f;
            foreach (var animName in orderedAnimNames)
            {
                if (!existingStates.TryGetValue(animName, out var st))
                {
                    st = sm.AddState(animName, new Vector3(x, 100, 0));
                    existingStates[animName] = st;
                    x += 250f;
                }

                st.motion = clipByAnim[animName];
            }

            sm.defaultState = existingStates[defaultAnim];

            // Cleanup previous AnyState transitions that are ours
            CleanupAnyStateTransitions(sm, settings.animIdInt);

            // AnyState -> each anim where AnimId == hash(animName)
            foreach (var animName in orderedAnimNames)
            {
                var st = existingStates[animName];
                int animId = Animator.StringToHash(animName.ToLowerInvariant());

                var t = sm.AddAnyStateTransition(st);
                t.hasExitTime = false;
                t.hasFixedDuration = true;
                t.duration = 0.05f;
                t.canTransitionToSelf = false;
                t.conditions = Array.Empty<AnimatorCondition>();
                t.AddCondition(AnimatorConditionMode.Equals, animId, settings.animIdInt);
            }

            // Set default value for AnimId param
            var ps = controller.parameters;
            for (int i = 0; i < ps.Length; i++)
            {
                if (ps[i].type == AnimatorControllerParameterType.Int && ps[i].name == settings.animIdInt)
                    ps[i].defaultInt = Animator.StringToHash(defaultAnim.ToLowerInvariant());
            }
            controller.parameters = ps;

            EditorUtility.SetDirty(controller);

            if (isNew)
                Debug.Log($"[AnimPipeline] Created controller: {controllerPath}");
            else
                Debug.Log($"[AnimPipeline] Updated controller: {controllerPath}");

            return controller;
        }

        private static void CleanupAnyStateTransitions(AnimatorStateMachine sm, string animIdParam)
        {
            var transitions = sm.anyStateTransitions;
            for (int i = transitions.Length - 1; i >= 0; i--)
            {
                var tr = transitions[i];
                if (tr == null) continue;

                var c = tr.conditions;
                if (c != null && c.Length == 1 && string.Equals(c[0].parameter, animIdParam, StringComparison.OrdinalIgnoreCase))
                    sm.RemoveAnyStateTransition(tr);
            }
        }

        private static string SanitizeFilePart(string s)
        {
            var invalid = Path.GetInvalidFileNameChars();
            var cleaned = new string(s.Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray());
            return cleaned.Replace(' ', '_');
        }

        private static void EnsureFolder(string folderPath)
        {
            folderPath = folderPath.Replace("\\", "/");
            if (AssetDatabase.IsValidFolder(folderPath)) return;

            var parts = folderPath.Split('/');
            if (parts.Length < 2) return;

            string current = parts[0];
            for (int i = 1; i < parts.Length; i++)
            {
                var next = $"{current}/{parts[i]}";
                if (!AssetDatabase.IsValidFolder(next))
                    AssetDatabase.CreateFolder(current, parts[i]);
                current = next;
            }
        }
    }
}
#endif
