using UnityEngine;

namespace ShearAndGrow.Farm
{
    /// <summary>
    /// Farm progression balance: Farmer House levels (cost / time / unlock text), barn capacity per level, sheep
    /// purchase, feeding. Levels 3–8 are added here as data later — no code changes needed. Which buildings unlock
    /// at a level comes from each definition's <see cref="FarmBuildingDefinition.RequiredFarmerHouseLevel"/>.
    /// </summary>
    [CreateAssetMenu(menuName = "Shear & Grow/Farm/Farm Progression Config", fileName = "FarmProgressionConfig")]
    public sealed class FarmProgressionConfig : ScriptableObject
    {
        [System.Serializable]
        public struct HouseLevel
        {
            [Tooltip("Target level of this upgrade step.")]
            [Min(2)] public int Level;
            [Min(0)] public int UpgradeCost;
            [Min(0f)] public float UpgradeSeconds;
            [TextArea] public string Unlocks;
        }

        [Header("Farmer House (target levels 2..8)")]
        public HouseLevel[] HouseLevels = { new HouseLevel { Level = 2, UpgradeCost = 300, UpgradeSeconds = 20f, Unlocks = "Trimmer Workshop, Feeding Station, Barn Lv 2" } };

        [Header("Barn & flock")]
        [Tooltip("Sheep capacity for barn level 1, 2, ...")]
        public int[] BarnCapacityByLevel = { 2, 4 };
        [Min(0)] public int SheepPrice = 200;
        public string[] SheepNames = { "Bella", "Mia", "Pip", "Luna", "Toffee", "Willow", "Maple", "Biscuit", "Hazel", "Poppy" };
        [Tooltip("At most this many sheep get a 3D model in the pen (all are always listed in the barn).")]
        [Min(1)] public int MaxVisibleSheep = 6;

        [Header("Feeding")]
        [Min(0)] public int FeedPrice = 20;
        [Min(1f)] public float FeedBoostMultiplier = 2f;
        [Tooltip("Boost duration in wool-growth clock seconds (game time by default).")]
        [Min(1f)] public float FeedBoostSeconds = 180f;

        public bool TryGetHouseLevel(int target, out HouseLevel l)
        {
            if (HouseLevels != null) foreach (var h in HouseLevels) if (h.Level == target) { l = h; return true; }
            l = default;
            return false;
        }

        public int MaxHouseLevel { get { int m = 1; if (HouseLevels != null) foreach (var h in HouseLevels) m = Mathf.Max(m, h.Level); return m; } }

        public int BarnCapacity(int barnLevel)
        {
            if (BarnCapacityByLevel == null || BarnCapacityByLevel.Length == 0) return 2;
            return BarnCapacityByLevel[Mathf.Clamp(barnLevel - 1, 0, BarnCapacityByLevel.Length - 1)];
        }
    }
}