using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace ShearAndGrow.Farm.Editor
{
    /// <summary>
    /// Fails the build when the building data is inconsistent or a developer farm save would ship:
    ///  • definitions: unique ids, visual prefab with a BuildingView, footprint ≥ 1, purchasable items priced;
    ///  • new-player farm (FarmLayout.StartingBuildings) inside the grid, not overlapping, not on reserved cells, and
    ///    WITHOUT the processing building (the player must build it);
    ///  • no farm save files (farm.json / instance data / purchase receipts) anywhere inside Assets.
    /// </summary>
    public sealed class FarmBuildValidator : IPreprocessBuildWithReport
    {
        public int callbackOrder => 1;
        const string LayoutPath = "Assets/ShearAndGrow/Features/Farm/Config/FarmLayout.asset";
        const string CatalogPath = "Assets/ShearAndGrow/Features/Farm/Config/BuildingCatalog.asset";

        public void OnPreprocessBuild(BuildReport report)
        {
            var problems = Validate();
            if (problems.Count > 0) throw new BuildFailedException("[Farm] Building data validation failed:\n - " + string.Join("\n - ", problems));
            Debug.Log("[Farm] Building data validation passed.");
        }

        [MenuItem("Shear & Grow/Farm/Validate Building Data")]
        public static void ValidateFromMenu()
        {
            var p = Validate();
            if (p.Count == 0) Debug.Log("[Farm] Building data validation passed.");
            else Debug.LogError("[Farm] Building data problems:\n - " + string.Join("\n - ", p));
        }

        public static List<string> Validate()
        {
            var problems = new List<string>();
            var layout = AssetDatabase.LoadAssetAtPath<FarmLayout>(LayoutPath);
            var catalog = AssetDatabase.LoadAssetAtPath<BuildingCatalog>(CatalogPath);
            if (layout == null) { problems.Add("FarmLayout missing at " + LayoutPath); return problems; }
            if (catalog == null) { problems.Add("BuildingCatalog missing at " + CatalogPath); return problems; }

            var ids = new HashSet<string>();
            foreach (var d in catalog.Items)
            {
                if (d == null) { problems.Add("Catalog has an empty entry"); continue; }
                if (string.IsNullOrEmpty(d.Id) || !ids.Add(d.Id)) problems.Add("Duplicate / empty building id: '" + d.Id + "'");
                if (d.VisualPrefab == null || d.VisualPrefab.GetComponent<BuildingView>() == null) problems.Add(d.Id + ": visual prefab missing or without BuildingView");
                if (d.Footprint.x < 1 || d.Footprint.y < 1) problems.Add(d.Id + ": footprint must be at least 1×1");
                if (d.Purchasable && d.Price <= 0) problems.Add(d.Id + ": purchasable item without a price");
                if (layout.Definition(d.Id) == null) problems.Add(d.Id + ": not listed in FarmLayout.Definitions");
            }

            var grid = new FarmGrid(layout.SizeCells.x, layout.SizeCells.y, layout.ReservedRects);
            var startIds = new HashSet<string>();
            foreach (var s in layout.StartingBuildings)
            {
                var d = catalog.Find(s.TypeId);
                if (d == null) { problems.Add("Starting building with unknown type " + s.TypeId); continue; }
                if (!startIds.Add(s.InstanceId)) problems.Add("Duplicate starting instance id " + s.InstanceId);
                if (d.Interaction == BuildingInteraction.WoolProcessing) problems.Add("New-player farm must NOT contain the processing building (" + s.InstanceId + ")");
                var r = FarmGrid.RectOf(s.Cell, d.SafeFootprint);
                var res = grid.CheckArea(r, null, out var cell);
                if (res != PlacementResult.Ok) problems.Add("Starting building " + s.InstanceId + " invalid at " + s.Cell + ": " + res + " (cell " + cell + ")");
                else grid.Occupy(s.InstanceId, s.TypeId, r);
            }

            foreach (var file in Directory.GetFiles("Assets", "*", SearchOption.AllDirectories))
            {
                string ext = Path.GetExtension(file).ToLowerInvariant();
                if (ext != ".json" && ext != ".txt" && ext != ".bytes") continue;
                string name = Path.GetFileName(file);
                bool suspicious = name.StartsWith("farm");
                if (!suspicious && new FileInfo(file).Length < 4 * 1024 * 1024)
                {
                    string text = File.ReadAllText(file);
                    suspicious = text.Contains("\"constructionEndUtcTicks\"") || text.Contains("\"pendingPurchases\"") || text.Contains("\"lastSeenUtcTicks\"");
                }
                if (suspicious) problems.Add("Farm save data inside the project (would ship): " + file);
            }
            return problems;
        }
    }
}