using UnityEngine;

namespace ShearAndGrow.Shearing
{
    /// <summary>
    /// Shearing tool upgrades (data only). The owned level lives in the economy save (<c>WoolEconomy.ToolLevel</c>).
    /// Level 1 = base tool. The multiplier scales the real brush radius of the shearing session (cut width), not a
    /// display value. Future tools / attachments are additional assets with another <see cref="ToolId"/>.
    /// </summary>
    [CreateAssetMenu(menuName = "Shear & Grow/Shearing/Tool Upgrade Config", fileName = "ToolUpgradeConfig")]
    public sealed class ToolUpgradeConfig : ScriptableObject
    {
        [System.Serializable]
        public struct Level
        {
            [Min(1)] public int Number;
            [Tooltip("Brush radius multiplier (cut width).")]
            [Range(0.5f, 2f)] public float RadiusMultiplier;
            [Min(0)] public int Cost;
            public string Description;
        }

        public string ToolId = "trimmer";
        public string DisplayName = "Trimmer";
        [Tooltip("Building type that sells these upgrades.")]
        public string RequiresBuilding = "trimmer_workshop";
        public Level[] Levels =
        {
            new Level { Number = 1, RadiusMultiplier = 1f, Cost = 0, Description = "Standard cutting head" },
            new Level { Number = 2, RadiusMultiplier = 1.22f, Cost = 150, Description = "Wide cutting head" },
        };
        [Tooltip("Safety clamp: effective brush radius never exceeds this (sheep body space, m).")]
        public float MaxEffectiveRadius = 0.12f;

        public int MaxLevel { get { int m = 1; foreach (var l in Levels) m = Mathf.Max(m, l.Number); return m; } }

        public bool TryGet(int number, out Level level)
        {
            foreach (var l in Levels) if (l.Number == number) { level = l; return true; }
            level = default;
            return false;
        }

        public float Multiplier(int level) => TryGet(Mathf.Clamp(level, 1, MaxLevel), out var l) ? l.RadiusMultiplier : 1f;

        /// <summary>Real brush radius for a tool level (clamped).</summary>
        public float EffectiveRadius(float baseRadius, int level) => Mathf.Min(baseRadius * Multiplier(level), Mathf.Max(baseRadius, MaxEffectiveRadius));
    }
}