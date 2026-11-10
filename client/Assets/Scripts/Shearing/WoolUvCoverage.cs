using System;
using UnityEngine;

namespace ShearAndGrow
{
    /// <summary>Rasterizes UV triangles once to exclude unused texture space from wool accounting.</summary>
    public static class WoolUvCoverage
    {
        public static bool[] Build(Mesh mesh, int resolution)
        {
            if (mesh == null || !mesh.isReadable) throw new ArgumentException("Wool coverage requires a readable mesh.");
            if (resolution < 16 || resolution > 512) throw new ArgumentOutOfRangeException(nameof(resolution));
            var uv = mesh.uv; var triangles = mesh.triangles;
            if (uv.Length != mesh.vertexCount) throw new ArgumentException("Wool mesh requires UV0.");
            var covered = new bool[resolution * resolution];
            for (int i = 0; i < triangles.Length; i += 3)
            {
                Vector2 a = uv[triangles[i]], b = uv[triangles[i + 1]], c = uv[triangles[i + 2]];
                Vector2 ab = b - a, ac = c - a;
                float determinant = ab.x * ac.y - ab.y * ac.x;
                if (Mathf.Abs(determinant) < 0.0000001f) continue;
                int minX = Mathf.Max(0, Mathf.CeilToInt(Mathf.Min(a.x, Mathf.Min(b.x, c.x)) * resolution - 0.5f));
                int maxX = Mathf.Min(resolution - 1, Mathf.FloorToInt(Mathf.Max(a.x, Mathf.Max(b.x, c.x)) * resolution - 0.5f));
                int minY = Mathf.Max(0, Mathf.CeilToInt(Mathf.Min(a.y, Mathf.Min(b.y, c.y)) * resolution - 0.5f));
                int maxY = Mathf.Min(resolution - 1, Mathf.FloorToInt(Mathf.Max(a.y, Mathf.Max(b.y, c.y)) * resolution - 0.5f));
                for (int y = minY; y <= maxY; y++)
                    for (int x = minX; x <= maxX; x++)
                    {
                        Vector2 offset = new Vector2((x + 0.5f) / resolution, (y + 0.5f) / resolution) - a;
                        float v = (offset.x * ac.y - offset.y * ac.x) / determinant;
                        float w = (ab.x * offset.y - ab.y * offset.x) / determinant;
                        if (v >= -0.00001f && w >= -0.00001f && v + w <= 1.00001f) covered[y * resolution + x] = true;
                    }
            }
            return covered;
        }
    }
}
