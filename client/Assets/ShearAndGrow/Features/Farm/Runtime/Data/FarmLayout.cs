using System.Collections.Generic;
using UnityEngine;

namespace ShearAndGrow.Farm
{
    /// <summary>
    /// DEFAULT farm for a new player in logical grid coordinates: bounds, starting buildings and sheep.
    /// Scene objects are views of these entries (matched by building id), so the world can later be rebuilt from
    /// data, extended by construction, or rendered with 2.5D sprites.
    /// </summary>
    [CreateAssetMenu(menuName = "Shear & Grow/Farm/Farm Layout", fileName = "FarmLayout")]
    public sealed class FarmLayout : ScriptableObject
    {
        [Min(1)] public int LayoutVersion = 1;
        [Tooltip("World size of one grid cell (m).")]
        public float CellSize = 1f;
        [Tooltip("Playable farm area in cells, centred on the world origin.")]
        public Vector2Int SizeCells = new Vector2Int(24, 28);
        public FarmBuildingDefinition[] Definitions = new FarmBuildingDefinition[0];
        [Tooltip("Starting farm of a NEW player (the only source of start data; never a dev save).")]
        public List<StartingBuilding> StartingBuildings = new List<StartingBuilding>();
        [Tooltip("Cells that can never be built on (road, trader lane, borders). Cell rects in grid coordinates.")]
        public List<RectInt> ReservedRects = new List<RectInt>();
        [Tooltip("Where the builder enters / leaves the farm (grid cell, may be outside the grid).")]
        public Vector2Int GateCell = new Vector2Int(8, -1);
        [Tooltip("Pen rectangle relative to the barn footprint centre (x, z, width, depth in metres).")]
        public Rect BarnPenLocal = new Rect(-2f, -5.95f, 4f, 2.7f);
        public List<FarmSheepSpawn> Sheep = new List<FarmSheepSpawn>();

        public FarmBuildingDefinition Definition(string id)
        {
            foreach (var d in Definitions) if (d != null && d.Id == id) return d;
            return null;
        }

        public Vector3 CellToWorld(Vector2Int cell) =>
            new Vector3((cell.x - SizeCells.x * 0.5f + 0.5f) * CellSize, 0f, (cell.y - SizeCells.y * 0.5f + 0.5f) * CellSize);

        /// <summary>Continuous world → fractional cell coordinates (cell centres at .5).</summary>
        public Vector2 WorldToCellF(Vector3 world) =>
            new Vector2(world.x / CellSize + SizeCells.x * 0.5f, world.z / CellSize + SizeCells.y * 0.5f);

        public Vector2Int WorldToCell(Vector3 world)
        {
            var f = WorldToCellF(world);
            return new Vector2Int(Mathf.FloorToInt(f.x), Mathf.FloorToInt(f.y));
        }

        /// <summary>World position of the centre of a footprint whose min corner is <paramref name="cell"/>.</summary>
        public Vector3 FootprintCenterWorld(Vector2Int cell, Vector2Int footprint) =>
            new Vector3((cell.x + footprint.x * 0.5f - SizeCells.x * 0.5f) * CellSize, 0f,
                        (cell.y + footprint.y * 0.5f - SizeCells.y * 0.5f) * CellSize);

        public Bounds WorldBounds => new Bounds(Vector3.zero, new Vector3(SizeCells.x * CellSize, 1f, SizeCells.y * CellSize));
    }

    /// <summary>A building of the new-player farm, with a fixed instance id.</summary>
    [System.Serializable]
    public struct StartingBuilding
    {
        public string InstanceId;
        public string TypeId;
        public Vector2Int Cell;
    }
}
