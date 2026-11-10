using UnityEngine;

namespace ShearAndGrow.Shearing
{
    [CreateAssetMenu(menuName = "Shear & Grow/Shearing/Shearing Feel Config", fileName = "ShearingFeelConfig")]
    public sealed class ShearingFeelConfig : ScriptableObject
    {
        [Header("Cut")]
        [Tooltip("Brush radius in sheep body object space (metres at scale 1).")]
        public float BrushRadius = 0.085f;
        [Tooltip("Stamp spacing as a fraction of brush radius (spec: 0.25–0.40).")]
        [Range(0.1f, 0.6f)] public float StampSpacing = 0.3f;
        [Tooltip("Max object-space distance between consecutive hits to still be the same stroke segment.")]
        public float MaxSegmentJoinDistance = 0.45f;
        [Tooltip("Progress fraction at which Finish becomes available.")]
        [Range(0.5f, 1f)] public float CompletionThreshold = 0.95f;

        [Header("Mask")]
        [Tooltip("CPU authoritative mask width (longitude). Height is half of this.")]
        public int MaskWidth = 512;
        [Tooltip("Surface/UV version. Bump whenever the body parametrisation changes so stale saves are not applied.")]
        public int WoolSurfaceVersion = 1;

        [Header("Wool volume")]
        public float WoolThickness = 0.2f;
        [Range(8, 48)] public int ShellCount = 24;

        [Header("Hint / UX")]
        public string HintText = "Swipe over the wool to shear";

        [Header("Determinism")]
        public int SessionSeed = 1337;
    }
}
