using System.Collections.Generic;
using UnityEngine;

namespace ShearAndGrow.Farm
{
    /// <summary>All building and decoration types known to the game (catalog display order).</summary>
    [CreateAssetMenu(menuName = "Shear & Grow/Farm/Building Catalog", fileName = "BuildingCatalog")]
    public sealed class BuildingCatalog : ScriptableObject
    {
        public FarmBuildingDefinition[] Items = new FarmBuildingDefinition[0];

        Dictionary<string, FarmBuildingDefinition> _byId;

        public FarmBuildingDefinition Find(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            if (_byId == null || _byId.Count != CountValid())
            {
                _byId = new Dictionary<string, FarmBuildingDefinition>();
                foreach (var d in Items) if (d != null && !string.IsNullOrEmpty(d.Id)) _byId[d.Id] = d;
            }
            _byId.TryGetValue(id, out var r);
            return r;
        }

        int CountValid() { int n = 0; foreach (var d in Items) if (d != null && !string.IsNullOrEmpty(d.Id)) n++; return n; }

        public void ByCategory(BuildingCategory cat, List<FarmBuildingDefinition> into)
        {
            into.Clear();
            foreach (var d in Items) if (d != null && d.Category == cat && d.ShowInCatalog) into.Add(d);
        }

        void OnValidate() => _byId = null;
    }
}