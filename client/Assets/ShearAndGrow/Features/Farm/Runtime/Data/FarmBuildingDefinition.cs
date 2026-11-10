using UnityEngine;

namespace ShearAndGrow.Farm
{
    public enum BuildingCategory { Building = 0, Decoration = 1 }

    /// <summary>Which functional interaction a completed instance offers (decoupled from the visual).</summary>
    public enum BuildingInteraction { None = 0, FarmerHouse = 1, Barn = 2, WoolProcessing = 3, Trader = 4, TrimmerWorkshop = 5, FeedingStation = 6 }

    /// <summary>One upgrade step of a building (target level, gate, price, duration, what it gives).</summary>
    [System.Serializable]
    public struct BuildingLevel
    {
        [Min(2)] public int Level;
        [Min(1)] public int RequiredFarmerHouseLevel;
        [Min(0)] public int Cost;
        [Min(0f)] public float Seconds;
        public string Benefit;
    }

    /// <summary>Simple neighbour auto-connection for segment decorations.</summary>
    public enum ConnectMode { None = 0, Fence = 1, Path = 2 }

    /// <summary>
    /// Static description of a building / decoration type: catalog, economy, construction, placement and the visual
    /// prefab. Pure data; the save stores only the <see cref="Id"/>. Swapping <see cref="VisualPrefab"/> (3D → 2.5D
    /// sprite prefab) keeps saves, grid and economy intact. Add a new type = new asset + add it to the catalog.
    /// </summary>
    [CreateAssetMenu(menuName = "Shear & Grow/Farm/Building Definition", fileName = "FarmBuilding")]
    public sealed class FarmBuildingDefinition : ScriptableObject
    {
        [Header("Identity")]
        [Tooltip("Stable type id stored in saves. Never rename once shipped.")]
        public string Id = "building";
        public string DisplayName = "Building";
        [TextArea] public string Description = "";
        public FarmBuildingKind Kind = FarmBuildingKind.Decoration;
        public BuildingCategory Category = BuildingCategory.Building;
        public BuildingInteraction Interaction = BuildingInteraction.None;

        [Header("Placement")]
        [Tooltip("Footprint in farm grid cells (x = width, y = depth).")]
        public Vector2Int Footprint = new Vector2Int(2, 2);
        [Tooltip("Player may move this instance in Edit mode.")]
        public bool Movable = true;
        [Tooltip("Decoration can be put into the inventory and placed again for free.")]
        public bool Storable;
        public ConnectMode Connect = ConnectMode.None;

        [Header("Catalog & economy")]
        public bool ShowInCatalog = true;
        public bool Purchasable = true;
        [Min(0)] public int Price = 0;
        [Tooltip("0 = unlimited instances.")]
        [Min(0)] public int MaxInstances = 1;
        [Min(1)] public int RequiredFarmerHouseLevel = 1;
        public Sprite Icon;

        [Header("Construction")]
        [Tooltip("Seconds of construction after purchase. 0 = appears instantly (decorations).")]
        [Min(0f)] public float ConstructionSeconds = 0f;

        [Header("Visual (3D prefab today, sprite prefab later)")]
        public GameObject VisualPrefab;

        [Header("Levels (prepared for Farmer House progression)")]
        [Min(1)] public int StartLevel = 1;
        [Min(1)] public int MaxLevel = 1;
        [Tooltip("Upgrade steps to levels 2..MaxLevel (the Farmer House uses FarmProgressionConfig instead).")]
        public BuildingLevel[] UpgradeLevels = new BuildingLevel[0];
        [Tooltip("Functional building that must be built (completed) before the Farmer House can reach the next level. Decorations never count.")]
        public bool MandatoryForProgression = true;

        /// <summary>Upgrade step that leads to <paramref name="targetLevel"/>, if configured.</summary>
        public bool TryGetUpgrade(int targetLevel, out BuildingLevel step)
        {
            if (UpgradeLevels != null)
                foreach (var l in UpgradeLevels) if (l.Level == targetLevel) { step = l; return true; }
            step = default;
            return false;
        }

        public bool IsDecoration => Category == BuildingCategory.Decoration;
        public bool NeedsConstruction => ConstructionSeconds > 0.01f;
        public bool IsUnlocked(int farmerHouseLevel) => farmerHouseLevel >= RequiredFarmerHouseLevel;
        public Vector2Int SafeFootprint => new Vector2Int(Mathf.Max(1, Footprint.x), Mathf.Max(1, Footprint.y));
    }
}
