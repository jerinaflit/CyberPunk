using UnityEngine;

namespace Project.AnimationPipeline
{
    [CreateAssetMenu(menuName = "Project/Pipeline/Animation Pipeline Settings", fileName = "AnimPipelineSettings")]
    public class AnimPipelineSettings : ScriptableObject
    {
        [Header("Scan")]
        [Tooltip("Dossiers racines à scanner (sprites).")]
        public string[] spriteRootFolders = new[] { "Assets/_Project/Art/Characters" };

        [Header("Output")]
        [Tooltip("Dossier où écrire les .anim et .controller")]
        public string outputRootFolder = "Assets/_Project/Animations/Characters";

        [Header("Clips")]
        [Min(1)]
        public int defaultFrameRate = 12;

        [Tooltip("Loop pour toutes les animations (simple, robuste).")]
        public bool loopAllClips = true;

        [Header("Animator")]
        [Tooltip("Paramètre int pilotant l'animation. Le clip est choisi via AnimId == hash(animName).")]
        public string animIdInt = "AnimId";
    }
}
