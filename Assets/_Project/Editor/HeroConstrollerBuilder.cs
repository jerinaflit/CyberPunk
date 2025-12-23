#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

public static class HeroControllerBuilder
{
    [MenuItem("Tools/_Project/Hero/Build Controller From Selected Folders (State Int)")]
    public static void BuildControllerFromSelection()
    {
        if (EditorApplication.isPlaying)
        {
            EditorUtility.DisplayDialog("Hero Controller Builder", "Stop Play Mode avant de builder.", "OK");
            return;
        }

        var selectedAnimFolders = GetSelectedFolderPathsRobust();
        if (selectedAnimFolders.Count == 0)
        {
            EditorUtility.DisplayDialog(
                "Hero Controller Builder",
                "Sélectionne des dossiers d'anim (Idle1, Idle2, WalkRight, Turn) dans Project.",
                "OK"
            );
            return;
        }

        string heroFolder = GetCommonParentFolder(selectedAnimFolders);
        if (string.IsNullOrEmpty(heroFolder) || !AssetDatabase.IsValidFolder(heroFolder))
        {
            EditorUtility.DisplayDialog(
                "Hero Controller Builder",
                "Impossible de déterminer le dossier Hero (parent commun).\nSélectionne uniquement des dossiers sous le même HeroX/.",
                "OK"
            );
            return;
        }

        string heroName = Path.GetFileName(heroFolder);
        if (string.IsNullOrWhiteSpace(heroName))
        {
            EditorUtility.DisplayDialog("Hero Controller Builder", "Nom de hero vide (path invalide).", "OK");
            return;
        }

        // --- Load clips from each selected folder: <Folder>/_Generated/<Folder>.anim ---
        var idleClips = new List<AnimationClip>();
        AnimationClip walkClip = null;
        AnimationClip turnClip = null;

        foreach (var folder in selectedAnimFolders)
        {
            string folderName = Path.GetFileName(folder);
            string clipPath = $"{folder}/_Generated/{folderName}.anim";
            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(clipPath);

            if (clip == null)
            {
                Debug.LogWarning($"[HeroControllerBuilder] Clip manquant: {clipPath}");
                continue;
            }

            // ✅ Hard validation: must have sprite keys
            int spriteKeys = CountSpriteKeys(clip);
            if (spriteKeys <= 0)
            {
                Debug.LogWarning($"[HeroControllerBuilder] Clip '{clip.name}' a 0 keyframe m_Sprite: {clipPath} (regénère tes clips)");
                continue;
            }

            string n = folderName.ToLowerInvariant();
            if (n.Contains("idle")) idleClips.Add(clip);
            else if (n.Contains("walk")) walkClip = clip;
            else if (n.Contains("turn")) turnClip = clip;
        }

        idleClips = idleClips.OrderBy(c => c.name, StringComparer.OrdinalIgnoreCase).ToList();

        if (idleClips.Count == 0 || walkClip == null)
        {
            EditorUtility.DisplayDialog(
                "Hero Controller Builder",
                "Clips requis manquants ou invalides.\n\n" +
                $"- Idle valides: {idleClips.Count}\n" +
                $"- Walk valide: {(walkClip != null)}\n\n" +
                "Assure-toi que tu as regénéré les clips et qu'ils contiennent des keyframes sprite.",
                "OK"
            );
            return;
        }

        // Output controller
        string outFolder = $"Assets/_Project/Animations/Characters/{heroName}";
        EnsureFolder(outFolder);

        string controllerPath = $"{outFolder}/{heroName}.controller";

        // Create or load controller
        AnimatorController controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(controllerPath);
        if (controller == null)
            controller = AnimatorController.CreateAnimatorControllerAtPath(controllerPath);

        var sm = controller.layers[0].stateMachine;

        // Deterministic reset
        sm.states = Array.Empty<ChildAnimatorState>();
        sm.stateMachines = Array.Empty<ChildAnimatorStateMachine>();
        sm.anyStateTransitions = Array.Empty<AnimatorStateTransition>();
        sm.entryTransitions = Array.Empty<AnimatorTransition>();

        EnsureIntParameter(controller, "State");

        // --- Idle sub-state machine ---
        var idleSSM = sm.AddStateMachine("IdleSSM");
        var idle1 = idleSSM.AddState("Idle1");
        idle1.motion = idleClips[0]; // ✅ assign
        idleSSM.defaultState = idle1;

        if (idleClips.Count >= 2)
        {
            var idle2 = idleSSM.AddState("Idle2");
            idle2.motion = idleClips[1]; // ✅ assign

            var t12 = idle1.AddTransition(idle2);
            t12.hasExitTime = true;
            t12.exitTime = 1f;
            t12.hasFixedDuration = true;
            t12.duration = 0.05f;

            var t21 = idle2.AddTransition(idle1);
            t21.hasExitTime = true;
            t21.exitTime = 1f;
            t21.hasFixedDuration = true;
            t21.duration = 0.05f;
        }

        // --- Walk ---
        var walkState = sm.AddState("Walk");
        walkState.motion = walkClip; // ✅ assign

        // --- Turn optional ---
        AnimatorState turnState = null;
        if (turnClip != null)
        {
            turnState = sm.AddState("Turn");
            turnState.motion = turnClip; // ✅ assign

            var turnToIdle = turnState.AddTransition(idle1);
            turnToIdle.hasExitTime = true;
            turnToIdle.exitTime = 1f;
            turnToIdle.hasFixedDuration = true;
            turnToIdle.duration = 0.05f;
        }

        // Entry transitions
        var entryToIdle = sm.AddEntryTransition(idle1);
        entryToIdle.conditions = Array.Empty<AnimatorCondition>();
        entryToIdle.AddCondition(AnimatorConditionMode.Equals, 0f, "State");

        var entryToWalk = sm.AddEntryTransition(walkState);
        entryToWalk.conditions = Array.Empty<AnimatorCondition>();
        entryToWalk.AddCondition(AnimatorConditionMode.Equals, 1f, "State");

        if (turnState != null)
        {
            var entryToTurn = sm.AddEntryTransition(turnState);
            entryToTurn.conditions = Array.Empty<AnimatorCondition>();
            entryToTurn.AddCondition(AnimatorConditionMode.Equals, 2f, "State");
        }

        // AnyState transitions (optional, but nice)
        var anyToIdle = sm.AddAnyStateTransition(idle1);
        anyToIdle.hasExitTime = false;
        anyToIdle.hasFixedDuration = true;
        anyToIdle.duration = 0.05f;
        anyToIdle.conditions = Array.Empty<AnimatorCondition>();
        anyToIdle.AddCondition(AnimatorConditionMode.Equals, 0f, "State");

        var anyToWalk = sm.AddAnyStateTransition(walkState);
        anyToWalk.hasExitTime = false;
        anyToWalk.hasFixedDuration = true;
        anyToWalk.duration = 0.05f;
        anyToWalk.conditions = Array.Empty<AnimatorCondition>();
        anyToWalk.AddCondition(AnimatorConditionMode.Equals, 1f, "State");

        if (turnState != null)
        {
            var anyToTurn = sm.AddAnyStateTransition(turnState);
            anyToTurn.hasExitTime = false;
            anyToTurn.hasFixedDuration = true;
            anyToTurn.duration = 0.05f;
            anyToTurn.conditions = Array.Empty<AnimatorCondition>();
            anyToTurn.AddCondition(AnimatorConditionMode.Equals, 2f, "State");
        }

        EditorUtility.SetDirty(controller);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log($"[HeroControllerBuilder] OK controller: {controllerPath}");
        EditorGUIUtility.PingObject(controller);

        EditorUtility.DisplayDialog(
            "Hero Controller Builder",
            $"OK.\n\nController:\n{controllerPath}\n\n" +
            $"Idle clips: {idleClips.Count}\nWalk: OK\nTurn: {(turnClip != null ? "OK" : "absent")}\n\n" +
            "Param int 'State': 0=Idle, 1=Walk, 2=Turn",
            "Parfait"
        );
    }

    // ---- Helpers ----

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

    private static List<string> GetSelectedFolderPathsRobust()
    {
        var folderPaths = new List<string>();

        var objs = Selection.GetFiltered<UnityEngine.Object>(SelectionMode.Assets);
        foreach (var obj in objs)
        {
            string path = AssetDatabase.GetAssetPath(obj);
            if (!string.IsNullOrEmpty(path) && AssetDatabase.IsValidFolder(path))
                folderPaths.Add(path);
        }

        if (folderPaths.Count == 0)
        {
            foreach (var guid in Selection.assetGUIDs)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (!string.IsNullOrEmpty(path) && AssetDatabase.IsValidFolder(path))
                    folderPaths.Add(path);
            }
        }

        return folderPaths.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static string GetCommonParentFolder(List<string> folders)
    {
        var parents = folders
            .Select(f => Path.GetDirectoryName(f)?.Replace("\\", "/"))
            .Where(p => !string.IsNullOrEmpty(p))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (parents.Count == 1) return parents[0];

        string common = parents[0];
        for (int i = 1; i < parents.Count; i++)
            common = CommonPrefix(common, parents[i]);

        common = common.Replace("\\", "/").TrimEnd('/');
        while (!string.IsNullOrEmpty(common) && !AssetDatabase.IsValidFolder(common))
            common = Path.GetDirectoryName(common)?.Replace("\\", "/");

        return common;
    }

    private static string CommonPrefix(string a, string b)
    {
        int len = Math.Min(a.Length, b.Length);
        int i = 0;
        for (; i < len; i++)
        {
            if (a[i] != b[i]) break;
        }
        return a.Substring(0, i);
    }

    private static void EnsureIntParameter(AnimatorController controller, string paramName)
    {
        if (!controller.parameters.Any(p => p.name == paramName && p.type == AnimatorControllerParameterType.Int))
            controller.AddParameter(paramName, AnimatorControllerParameterType.Int);
    }

    private static void EnsureFolder(string fullFolder)
    {
        if (AssetDatabase.IsValidFolder(fullFolder)) return;

        string[] parts = fullFolder.Split('/');
        string current = parts[0]; // Assets
        for (int i = 1; i < parts.Length; i++)
        {
            string next = current + "/" + parts[i];
            if (!AssetDatabase.IsValidFolder(next))
                AssetDatabase.CreateFolder(current, parts[i]);
            current = next;
        }
    }
}
#endif
