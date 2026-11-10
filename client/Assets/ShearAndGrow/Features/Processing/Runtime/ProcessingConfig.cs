using ShearAndGrow.Economy;
using UnityEngine;

namespace ShearAndGrow.Processing
{
    public enum ProcessingStage { Wash = 0, Dry = 1 }

    /// <summary>All tunables of one wool-processing mini-game (one asset for Wash, one for Dry).</summary>
    [CreateAssetMenu(menuName = "Shear & Grow/Processing/Processing Config", fileName = "ProcessingConfig")]
    public sealed class ProcessingConfig : ScriptableObject
    {
        [Header("Stage")]
        public ProcessingStage Stage = ProcessingStage.Wash;
        public WoolType Input = WoolType.Dirty;
        [Tooltip("Unique processed area needed to finish (0..1).")]
        [Range(0.5f, 1f)] public float CompletionThreshold = 0.9f;
        [Tooltip("A mask cell counts as processed once its value reaches this level (counted once).")]
        [Range(0.5f, 1f)] public float CellDoneLevel = 0.85f;

        [Header("Brush (tool footprint on the heap)")]
        [Tooltip("Radius of the water jet / air stream on the wool (m).")]
        public float BrushRadius = 0.062f;
        [Tooltip("Processing dose per second at the brush centre while holding still.")]
        public float DosePerSecond = 2.2f;
        [Tooltip("Minimum dose per stamp while sweeping, so fast swipes still clean/dry (lower = more passes needed).")]
        public float MinSweepDose = 0.34f;
        [Tooltip("Stamp spacing as a fraction of the brush radius (interpolation between frames).")]
        [Range(0.1f, 0.6f)] public float StampSpacing = 0.3f;

        [Header("Feedback pacing")]
        [Tooltip("Progress step that triggers a 'notable area' accent (sound + haptic).")]
        [Range(0.02f, 0.2f)] public float AccentStep = 0.08f;
        [Tooltip("Processed weight per second that maps to full feedback intensity.")]
        public float FeedbackRateForMax = 0.06f;

        [Header("Heap (mesh density per quality tier: Low, Medium, High)")]
        public int[] PuffsPerTier = { 210, 300, 400 };
        public Vector2 HeapHalfSize = new Vector2(0.30f, 0.24f);
        public float HeapHeight = 0.2f;
        public Vector2 PuffRadius = new Vector2(0.034f, 0.058f);
        public int Seed = 4711;

        [Header("Look")]
        public Color DirtyColor = new Color(0.90f, 0.79f, 0.56f);
        public Color DirtSpotColor = new Color(0.50f, 0.40f, 0.28f);
        public Color WetColor = new Color(0.60f, 0.61f, 0.62f);
        public Color DryColor = new Color(0.99f, 0.975f, 0.94f);

        [Header("Feedback")]
        public ShearAndGrow.Shearing.HapticsConfig Haptics;
        [Range(0f, 1f)] public float MasterVolume = 0.9f;
        [Range(0f, 1f)] public float LoopVolume = 0.42f;
        [Range(0f, 1f)] public float ImpactLayerVolume = 0.5f;
        [Range(0f, 1f)] public float OneShotVolume = 0.55f;
        public float LoopRampSeconds = 0.08f;

        [Header("Texts")]
        public string Title = "Wash Wool";
        public string HintText = "Hold & drag to wash";
        public string ProgressLabel = "Washed";
        public string EmptyText = "No dirty wool yet.\nShear a sheep first!";
    }
}
