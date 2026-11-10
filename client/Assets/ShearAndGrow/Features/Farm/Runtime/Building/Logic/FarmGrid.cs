using System;
using System.Collections.Generic;
using UnityEngine;

namespace ShearAndGrow.Farm
{
    /// <summary>Why a placement is (not) allowed. Ordered roughly by how the UI explains it.</summary>
    public enum PlacementResult
    {
        Ok = 0, OutOfBounds, Reserved, Occupied, Locked, MaxInstances, ConstructionBusy, NotEnoughCoins,
        NotMovable, UnderConstruction, NotPurchasable, Unknown, Busy, MaxLevel, RequirementsNotMet,
    }

    /// <summary>
    /// Logical building grid (pure data, no GameObjects). Each cell stores the slot of the instance occupying it
    /// (0 = free) and whether it is permanently reserved (road, trader lane, out-of-lawn). Rebuilt only on load and
    /// updated incrementally on mutations; never scanned per frame.
    /// </summary>
    public sealed class FarmGrid
    {
        public readonly int Width, Height;
        readonly int[] _cells;           // slot+1 of the occupying instance, 0 = free
        readonly bool[] _reserved;
        readonly List<string> _slotIds = new List<string>();      // slot → instanceId (null = free slot)
        readonly Dictionary<string, int> _slotOf = new Dictionary<string, int>();
        readonly Dictionary<string, RectInt> _rectOf = new Dictionary<string, RectInt>();
        readonly Dictionary<string, string> _typeOf = new Dictionary<string, string>();

        /// <summary>Incremented on every occupancy change (views refresh lazily, e.g. the grid overlay).</summary>
        public int Version { get; private set; }

        public FarmGrid(int width, int height, IEnumerable<RectInt> reserved)
        {
            Width = Mathf.Max(1, width); Height = Mathf.Max(1, height);
            _cells = new int[Width * Height];
            _reserved = new bool[Width * Height];
            if (reserved != null)
                foreach (var r in reserved)
                    for (int y = r.yMin; y < r.yMax; y++)
                        for (int x = r.xMin; x < r.xMax; x++)
                            if (InBounds(x, y)) _reserved[x + y * Width] = true;
        }

        public bool InBounds(int x, int y) => x >= 0 && y >= 0 && x < Width && y < Height;
        public bool IsReserved(int x, int y) => InBounds(x, y) && _reserved[x + y * Width];

        /// <summary>Instance occupying a cell (null = free / reserved / outside).</summary>
        public string OccupantAt(int x, int y)
        {
            if (!InBounds(x, y)) return null;
            int s = _cells[x + y * Width];
            return s > 0 ? _slotIds[s - 1] : null;
        }

        public string TypeAt(int x, int y)
        {
            var id = OccupantAt(x, y);
            return id != null && _typeOf.TryGetValue(id, out var t) ? t : null;
        }

        public bool TryGetRect(string instanceId, out RectInt rect) => _rectOf.TryGetValue(instanceId, out rect);

        public static RectInt RectOf(Vector2Int cell, Vector2Int footprint) => new RectInt(cell.x, cell.y, Mathf.Max(1, footprint.x), Mathf.Max(1, footprint.y));

        /// <summary>Spatial check only (bounds, reserved, overlap). <paramref name="ignoreInstanceId"/> = the instance being moved.</summary>
        public PlacementResult CheckArea(RectInt r, string ignoreInstanceId, out Vector2Int firstBlocked)
        {
            firstBlocked = new Vector2Int(-1, -1);
            if (r.xMin < 0 || r.yMin < 0 || r.xMax > Width || r.yMax > Height) { firstBlocked = r.position; return PlacementResult.OutOfBounds; }
            int ignore = ignoreInstanceId != null && _slotOf.TryGetValue(ignoreInstanceId, out var s) ? s + 1 : -1;
            for (int y = r.yMin; y < r.yMax; y++)
                for (int x = r.xMin; x < r.xMax; x++)
                {
                    int i = x + y * Width;
                    if (_reserved[i]) { firstBlocked = new Vector2Int(x, y); return PlacementResult.Reserved; }
                    int c = _cells[i];
                    if (c != 0 && c != ignore) { firstBlocked = new Vector2Int(x, y); return PlacementResult.Occupied; }
                }
            return PlacementResult.Ok;
        }

        public void Occupy(string instanceId, string typeId, RectInt r)
        {
            if (string.IsNullOrEmpty(instanceId)) throw new ArgumentException("instance id");
            Release(instanceId);
            int slot = _slotIds.IndexOf(null);
            if (slot < 0) { slot = _slotIds.Count; _slotIds.Add(instanceId); } else _slotIds[slot] = instanceId;
            _slotOf[instanceId] = slot;
            _rectOf[instanceId] = r;
            _typeOf[instanceId] = typeId;
            for (int y = r.yMin; y < r.yMax; y++)
                for (int x = r.xMin; x < r.xMax; x++)
                    if (InBounds(x, y)) _cells[x + y * Width] = slot + 1;
            Version++;
        }

        public void Release(string instanceId)
        {
            if (instanceId == null || !_slotOf.TryGetValue(instanceId, out int slot)) return;
            var r = _rectOf[instanceId];
            for (int y = r.yMin; y < r.yMax; y++)
                for (int x = r.xMin; x < r.xMax; x++)
                    if (InBounds(x, y) && _cells[x + y * Width] == slot + 1) _cells[x + y * Width] = 0;
            _slotIds[slot] = null;
            _slotOf.Remove(instanceId);
            _rectOf.Remove(instanceId);
            _typeOf.Remove(instanceId);
            Version++;
        }

        public void Clear()
        {
            Array.Clear(_cells, 0, _cells.Length);
            _slotIds.Clear(); _slotOf.Clear(); _rectOf.Clear(); _typeOf.Clear();
            Version++;
        }

        /// <summary>4-bit mask (1 = +y/north, 2 = +x/east, 4 = −y/south, 8 = −x/west) of same-type neighbours.</summary>
        public int NeighbourMask(Vector2Int cell, string typeId)
        {
            int m = 0;
            if (TypeAt(cell.x, cell.y + 1) == typeId) m |= 1;
            if (TypeAt(cell.x + 1, cell.y) == typeId) m |= 2;
            if (TypeAt(cell.x, cell.y - 1) == typeId) m |= 4;
            if (TypeAt(cell.x - 1, cell.y) == typeId) m |= 8;
            return m;
        }

        /// <summary>Fills a W×H byte map: 0 free, 1 reserved, 2 occupied (grid overlay texture).</summary>
        public void WriteOccupancy(byte[] into)
        {
            for (int i = 0; i < _cells.Length && i < into.Length; i++)
                into[i] = _reserved[i] ? (byte)1 : _cells[i] != 0 ? (byte)2 : (byte)0;
        }
    }
}