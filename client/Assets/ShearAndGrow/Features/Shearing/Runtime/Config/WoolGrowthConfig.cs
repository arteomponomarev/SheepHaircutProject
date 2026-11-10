using ShearAndGrow.Economy;
using UnityEngine;

namespace ShearAndGrow.Shearing
{
    /// <summary>Tunable wool regrowth (test values; economy balancing later).</summary>
    [CreateAssetMenu(menuName = "Shear & Grow/Shearing/Wool Growth Config", fileName = "WoolGrowthConfig")]
    public sealed class WoolGrowthConfig : ScriptableObject
    {
        [Tooltip("Master switch for wool regrowth.")]
        public bool RegrowthEnabled = true;
        [Tooltip("Seconds for a fully sheared patch to grow back to full fleece. Test value: 5–10 minutes.")]
        [Min(10f)] public float FullRegrowthDuration = 480f;
        [Tooltip("GameTime: grows only while the game runs. RealTime: also grows while the app is closed.")]
        public WoolGrowthTimeMode TimeMode = WoolGrowthTimeMode.GameTime;
        [Tooltip("How often visible sheep (farm) apply growth and refresh their coat (s).")]
        [Min(0.25f)] public float RegrowthUpdateInterval = 2f;
        [Tooltip("Speeds growth up for testing. Only applied in the Editor and development builds.")]
        [Min(0f)] public float DebugRegrowthMultiplier = 1f;
        [Tooltip("RealTime only: maximum offline time that is credited at once (s).")]
        [Min(0f)] public float MaxOfflineCatchUp = 86400f;

        /// <summary>Seconds per one length step (0..255).</summary>
        public double SecondsPerStep => FullRegrowthDuration / 255.0;

        public float EffectiveMultiplier => Debug.isDebugBuild ? Mathf.Max(0f, DebugRegrowthMultiplier) : 1f;

        public double Now => WoolGrowthClock.Now(TimeMode);
    }
}
