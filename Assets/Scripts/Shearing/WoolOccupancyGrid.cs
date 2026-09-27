using System;
using UnityEngine;

namespace ShearAndGrow
{
    /// <summary>CPU-only UV cell coverage. Each cell can contribute once per reset.</summary>
    public sealed class WoolOccupancyGrid
    {
        private readonly bool[] removed;
        private readonly bool[] eligible;
        public int Resolution { get; }
        public int TotalCells { get; }
        public int RemovedCells { get; private set; }
        public float Removed01 => (float)RemovedCells / TotalCells;

        public WoolOccupancyGrid(int resolution, bool[] coveredUvCells = null)
        {
            if (resolution < 16 || resolution > 512)
                throw new ArgumentOutOfRangeException(nameof(resolution));
            Resolution = resolution;
            removed = new bool[resolution * resolution];
            if (coveredUvCells == null) TotalCells = removed.Length;
            else
            {
                if (coveredUvCells.Length != removed.Length) throw new ArgumentException("UV coverage must match the grid resolution.");
                eligible = (bool[])coveredUvCells.Clone();
                for (int i = 0; i < eligible.Length; i++) if (eligible[i]) TotalCells++;
                if (TotalCells == 0) throw new ArgumentException("Wool mesh has no covered UV cells.");
            }
        }

        // Cell centres sample the same circular stamps as the visual mask. Only visit its bounds.
        public int RemoveCircle(Vector2 uv, float radius, bool wrapU)
            => RemoveCircle(uv, radius, wrapU, out _);

        public int RemoveCircle(Vector2 uv, float radius, bool wrapU, out Vector2 firstRemovedUv)
        {
            firstRemovedUv = default;
            if (!Finite(uv.x) || !Finite(uv.y) || !Finite(radius) || radius <= 0f) return 0;
            radius = Mathf.Min(radius, 0.25f);
            uv.x = wrapU ? Mathf.Repeat(uv.x, 1f) : Mathf.Clamp01(uv.x);
            uv.y = Mathf.Clamp01(uv.y);
            int minX = Mathf.CeilToInt((uv.x - radius) * Resolution - 0.5f);
            int maxX = Mathf.FloorToInt((uv.x + radius) * Resolution - 0.5f);
            int minY = Mathf.Max(0, Mathf.CeilToInt((uv.y - radius) * Resolution - 0.5f));
            int maxY = Mathf.Min(Resolution - 1, Mathf.FloorToInt((uv.y + radius) * Resolution - 0.5f));
            if (!wrapU) { minX = Mathf.Max(0, minX); maxX = Mathf.Min(Resolution - 1, maxX); }
            int added = 0;
            float radiusSquared = radius * radius;
            for (int y = minY; y <= maxY; y++)
            {
                float dy = (y + 0.5f) / Resolution - uv.y;
                for (int x = minX; x <= maxX; x++)
                {
                    float dx = (x + 0.5f) / Resolution - uv.x;
                    if (dx * dx + dy * dy > radiusSquared) continue;
                    int column = wrapU ? (x % Resolution + Resolution) % Resolution : x;
                    int index = y * Resolution + column;
                    if (removed[index] || (eligible != null && !eligible[index])) continue;
                    removed[index] = true;
                    if (added == 0) firstRemovedUv = new Vector2((column + 0.5f) / Resolution, (y + 0.5f) / Resolution);
                    added++;
                }
            }
            RemovedCells += added;
            return added;
        }

        public void Reset()
        {
            Array.Clear(removed, 0, removed.Length);
            RemovedCells = 0;
        }

        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
