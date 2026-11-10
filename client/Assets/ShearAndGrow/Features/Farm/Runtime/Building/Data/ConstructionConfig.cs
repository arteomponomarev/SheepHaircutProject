using UnityEngine;

namespace ShearAndGrow.Farm
{
    /// <summary>Tunables of the construction system (rules, stage timing, builder NPC, ticking).</summary>
    [CreateAssetMenu(menuName = "Shear & Grow/Farm/Construction Config", fileName = "ConstructionConfig")]
    public sealed class ConstructionConfig : ScriptableObject
    {
        [Header("Rules")]
        [Tooltip("Functional buildings that may be under construction at the same time (decor never counts).")]
        [Min(1)] public int MaxConcurrentConstructions = 1;

        [Header("Visual stages (progress 0..1 where the next stage starts)")]
        [Range(0f, 1f)] public float WallsStart = 0.18f;
        [Range(0f, 1f)] public float RoofStart = 0.62f;
        [Range(0f, 1f)] public float DetailsStart = 0.9f;

        [Header("Builder NPC (visual only)")]
        [Min(0.1f)] public float BuilderWalkSpeed = 3.2f;
        public Vector2 BuilderRepositionSeconds = new Vector2(3f, 6f);
        [Min(0.1f)] public float HammerSwingSeconds = 0.55f;

        [Header("Runtime")]
        [Tooltip("Seconds between construction ticks while something is being built.")]
        [Min(0.05f)] public float TickInterval = 0.25f;
        [Tooltip("Device clock moved back by more than this (s) is logged as a rollback.")]
        [Min(0f)] public float ClockRollbackLogSeconds = 300f;

        /// <summary>Visual stage for a progress value: 0 foundation, 1 walls, 2 roof, 3 details, 4 completed.</summary>
        public int StageOf(float progress, out float stageT)
        {
            progress = Mathf.Clamp01(progress);
            if (progress >= 1f) { stageT = 1f; return 4; }
            if (progress >= DetailsStart) { stageT = Mathf.InverseLerp(DetailsStart, 1f, progress); return 3; }
            if (progress >= RoofStart) { stageT = Mathf.InverseLerp(RoofStart, DetailsStart, progress); return 2; }
            if (progress >= WallsStart) { stageT = Mathf.InverseLerp(WallsStart, RoofStart, progress); return 1; }
            stageT = WallsStart > 0f ? progress / WallsStart : 1f;
            return 0;
        }
    }
}