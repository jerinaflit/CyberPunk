#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

public static class ClipKeyframeProbe
{
    [MenuItem("Tools/_Project/DEBUG/Print Selected Clip Sprite Keyframes")]
    public static void Print()
    {
        var clip = Selection.activeObject as AnimationClip;
        if (clip == null)
        {
            Debug.LogError("[ClipProbe] Sélectionne un .anim dans Project puis relance.");
            return;
        }

        int found = 0;
        var bindings = AnimationUtility.GetObjectReferenceCurveBindings(clip);

        foreach (var b in bindings)
        {
            if (b.type == typeof(SpriteRenderer) && b.propertyName == "m_Sprite")
            {
                var curve = AnimationUtility.GetObjectReferenceCurve(clip, b);
                int n = curve != null ? curve.Length : 0;
                Debug.Log($"[ClipProbe] {clip.name} -> Sprite keyframes = {n}");
                found = n;
            }
        }

        if (found == 0)
            Debug.LogWarning($"[ClipProbe] {clip.name} : aucune keyframe m_Sprite trouvée.");
    }
}
#endif
