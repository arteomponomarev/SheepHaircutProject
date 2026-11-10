using System.Collections.Generic;
using UnityEngine;

namespace ShearAndGrow.Processing
{
    /// <summary>
    /// Builds a heap of overlapping cartoon wool puffs on a soft mound (object space, table top at y = 0).
    /// Vertex contract for ShearAndGrow/ProcessWool:
    ///   position/normal = unit-sphere direction around the puff centre (final shape is computed in the shader),
    ///   uv0 = footprint UV of the puff centre (mask lookup → the whole puff deforms consistently),
    ///   uv1 = (radius, seed, ambient occlusion, layer), uv2 = puff centre, uv3 = wet-clump centre.
    /// </summary>
    public static class WoolHeapBuilder
    {
        public sealed class Result
        {
            public Mesh Mesh;
            public HeapHeightfield Field;
            public int PuffCount;
        }

        struct Puff { public Vector3 C; public float R; public float Seed; public float AO; public float Layer; public int Cluster; }

        public static Result Build(string name, Vector2 halfSize, float height, int puffCount, Vector2 radiusRange, int seed, int segU, int segV, int fieldRes)
        {
            var rng = new System.Random(seed);
            float R(float a, float b) => a + (float)rng.NextDouble() * (b - a);
            // mound shape (slightly irregular ellipse)
            float ax = halfSize.x * 0.82f, az = halfSize.y * 0.82f;
            float Mound(float x, float z)
            {
                float d = (x * x) / (ax * ax) + (z * z) / (az * az);
                if (d >= 1f) return 0f;
                float wobble = 1f + 0.08f * Mathf.Sin(x * 23f + 1.3f) * Mathf.Cos(z * 19f);
                return height * Mathf.Pow(1f - d, 0.62f) * wobble;
            }

            var puffs = new List<Puff>(puffCount);
            int outer = Mathf.RoundToInt(puffCount * 0.72f);
            int inner = puffCount - outer;
            // outer visible layer: Fibonacci-like spiral over the ellipse for even coverage, jittered
            for (int k = 0; k < outer; k++)
            {
                float t = (k + 0.5f) / outer;
                float rr = Mathf.Sqrt(t) * 1.04f;
                float ang = k * 2.39996323f + R(-0.25f, 0.25f);
                float x = Mathf.Cos(ang) * rr * ax + R(-0.008f, 0.008f);
                float z = Mathf.Sin(ang) * rr * az + R(-0.008f, 0.008f);
                float edge = Mathf.Clamp01(rr);
                float r = Mathf.Lerp(radiusRange.y, radiusRange.x, edge * 0.8f) * R(0.82f, 1.15f);
                float top = Mound(x, z);
                float y = Mathf.Max(r * 0.62f, top - r * 0.38f);
                puffs.Add(new Puff { C = new Vector3(x, y, z), R = r, Seed = (float)rng.NextDouble(), AO = 1f, Layer = 0f });
            }
            // inner filler: larger, darker, lower — no holes between the visible puffs
            for (int k = 0; k < inner; k++)
            {
                float t = (k + 0.5f) / inner;
                float rr = Mathf.Sqrt(t) * 0.92f;
                float ang = k * 2.39996323f + 1.1f;
                float x = Mathf.Cos(ang) * rr * ax, z = Mathf.Sin(ang) * rr * az;
                float r = radiusRange.y * R(1.0f, 1.3f);
                float y = Mathf.Max(r * 0.55f, Mound(x, z) - r * 0.95f);
                puffs.Add(new Puff { C = new Vector3(x, y, z), R = r, Seed = (float)rng.NextDouble(), AO = 0.72f, Layer = 1f });
            }

            // wet clumps: group neighbouring puffs on a coarse grid (cell ~ 2.2 puff radii)
            float cellSize = radiusRange.y * 2.4f;
            var clusterSum = new Dictionary<long, Vector4>();
            for (int k = 0; k < puffs.Count; k++)
            {
                var p = puffs[k];
                long key = ((long)Mathf.FloorToInt((p.C.x + 7f) / cellSize) << 32) ^ ((long)Mathf.FloorToInt((p.C.z + 7f) / cellSize) << 8) ^ (long)Mathf.FloorToInt(p.C.y / cellSize);
                clusterSum.TryGetValue(key, out var s);
                clusterSum[key] = s + new Vector4(p.C.x, p.C.y, p.C.z, 1f);
                p.Cluster = (int)(key & 0x7fffffff) ^ (int)(key >> 32);
                puffs[k] = p;
            }

            var field = new HeapHeightfield(fieldRes, halfSize);
            int vPer = (segU + 1) * (segV + 1);
            var verts = new List<Vector3>(puffs.Count * vPer);
            var norms = new List<Vector3>(puffs.Count * vPer);
            var uv0 = new List<Vector2>(puffs.Count * vPer);
            var uv1 = new List<Vector4>(puffs.Count * vPer);
            var uv2 = new List<Vector3>(puffs.Count * vPer);
            var uv3 = new List<Vector3>(puffs.Count * vPer);
            var tris = new List<int>(puffs.Count * segU * segV * 6);
            for (int k = 0; k < puffs.Count; k++)
            {
                var p = puffs[k];
                long key = ((long)Mathf.FloorToInt((p.C.x + 7f) / cellSize) << 32) ^ ((long)Mathf.FloorToInt((p.C.z + 7f) / cellSize) << 8) ^ (long)Mathf.FloorToInt(p.C.y / cellSize);
                Vector4 cs = clusterSum[key];
                Vector3 clusterC = new Vector3(cs.x, cs.y, cs.z) / cs.w;
                field.AddSphere(p.C, p.R * 1.02f);
                Vector2 fuv = field.ToUV(p.C.x, p.C.z);
                int start = verts.Count;
                for (int j = 0; j <= segV; j++)
                {
                    float th = j / (float)segV * Mathf.PI;
                    for (int i = 0; i <= segU; i++)
                    {
                        float ph = i / (float)segU * Mathf.PI * 2f;
                        var dir = new Vector3(Mathf.Sin(th) * Mathf.Cos(ph), Mathf.Cos(th), Mathf.Sin(th) * Mathf.Sin(ph));
                        verts.Add(p.C + dir * p.R);
                        norms.Add(dir);
                        uv0.Add(fuv);
                        uv1.Add(new Vector4(p.R, p.Seed, p.AO, p.Layer));
                        uv2.Add(p.C);
                        uv3.Add(clusterC);
                    }
                }
                int row = segU + 1;
                for (int j = 0; j < segV; j++)
                    for (int i = 0; i < segU; i++)
                    {
                        int a = start + j * row + i, b = a + 1, c = a + row, d = c + 1;
                        tris.Add(a); tris.Add(b); tris.Add(c);
                        tris.Add(b); tris.Add(d); tris.Add(c);
                    }
            }

            var mesh = new Mesh { name = name, indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            mesh.SetVertices(verts);
            mesh.SetNormals(norms);
            mesh.SetUVs(0, uv0);
            mesh.SetUVs(1, uv1);
            mesh.SetUVs(2, uv2);
            mesh.SetUVs(3, uv3);
            mesh.SetTriangles(tris, 0);
            var b0 = new Bounds(new Vector3(0f, height * 0.6f, 0f), new Vector3(halfSize.x * 2.4f, height * 1.8f, halfSize.y * 2.4f));
            mesh.bounds = b0; // shader displacement stays inside these bounds
            return new Result { Mesh = mesh, Field = field, PuffCount = puffs.Count };
        }
    }
}
