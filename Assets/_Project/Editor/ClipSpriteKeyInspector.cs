#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

public static class ClipSpriteKeyInspector
{
    [MenuItem("Tools/_Project/DEBUG/Inspect Selected Clip Sprite Keys")]
    public static void Inspect()
    {
        var clip = Selection.activeObject as AnimationClip;
        if (clip == null)
        {
            Debug.LogError("[ClipInspect] Sélectionne un .anim dans Project puis relance.");
            return;
        }

        var bindings = AnimationUtility.GetObjectReferenceCurveBindings(clip);
        bool found = false;

        foreach (var b in bindings)
        {
            if (b.type == typeof(SpriteRenderer) && b.propertyName == "m_Sprite")
            {
                found = true;
                var curve = AnimationUtility.GetObjectReferenceCurve(clip, b);

                int n = curve != null ? curve.Length : 0;
                Debug.Log($"[ClipInspect] Clip={clip.name} Binding path='{b.path}' Keys={n}");

                if (curve == null || n == 0)
                {
                    Debug.LogWarning("[ClipInspect] Courbe m_Sprite vide.");
                    return;
                }

                // Print first 5 and last 2
                int head = Mathf.Min(5, n);
                for (int i = 0; i < head; i++)
                {
                    var v = curve[i].value as Sprite;
                    Debug.Log($"[ClipInspect] Key[{i}] t={curve[i].time:0.000} sprite={(v ? v.name : "NULL")}");
                }

                if (n > 7)
                {
                    Debug.Log("[ClipInspect] ...");
                }

                for (int i = Mathf.Max(n - 2, head); i < n; i++)
                {
                    var v = curve[i].value as Sprite;
                    Debug.Log($"[ClipInspect] Key[{i}] t={curve[i].time:0.000} sprite={(v ? v.name : "NULL")}");
                }

                return;
            }
        }

        if (!found)
            Debug.LogWarning("[ClipInspect] Aucun binding SpriteRenderer.m_Sprite trouvé dans ce clip.");
    }
}
#endif
