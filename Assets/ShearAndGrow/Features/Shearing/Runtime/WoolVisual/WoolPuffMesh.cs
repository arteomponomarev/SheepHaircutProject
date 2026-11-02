using System.Collections.Generic;
using UnityEngine;

namespace ShearAndGrow.Shearing
{
    /// <summary>
    /// Builds the reference "popcorn" fleece: soft, slightly lumpy round puffs.
    /// Vertex data contract (read by ShearAndGrow/WoolShell baked path):
    ///   uv0 = body parametrisation of the puff centre (whole puff is cut together),
    ///   uv1 = (-1, seed) marks baked geometry, seed varies micro fuzz per puff,
    ///   color.r = ambient occlusion (darker where the puff faces the body / neighbours).
    /// </summary>
    public sealed class WoolPuffMeshBuilder
    {
        public const int SegU = 14, SegV = 10;
        readonly List<Vector3> _v = new List<Vector3>();
        readonly List<Vector3> _n = new List<Vector3>();
        readonly List<Vector2> _uv = new List<Vector2>();
        readonly List<Vector2> _uv1 = new List<Vector2>();
        readonly List<Color> _c = new List<Color>();
        readonly List<int> _t = new List<int>();

        public int VertexCount => _v.Count;

        /// <param name="aoAxis">Direction towards the outside (puff side facing it stays bright).</param>
        public void AddPuff(Vector3 centre, float radius, float lumpiness, Vector3 aoAxis, float aoStrength,
            Vector2 maskUV, float seed, float brightness = 1f)
        {
            int start = _v.Count;
            var rng = new System.Random(Mathf.FloorToInt(seed * 100000f));
            // 3–5 sub-lobes give the cauliflower / popcorn silhouette of the reference.
            int lobes = 3 + rng.Next(0, 3);
            var lobeDir = new Vector3[lobes];
            var lobeAmp = new float[lobes];
            for (int i = 0; i < lobes; i++)
            {
                lobeDir[i] = new Vector3((float)rng.NextDouble() * 2f - 1f, (float)rng.NextDouble() * 2f - 1f, (float)rng.NextDouble() * 2f - 1f).normalized;
                lobeAmp[i] = lumpiness * (0.5f + (float)rng.NextDouble() * 0.5f);
            }
            Vector3 axis = aoAxis.sqrMagnitude > 1e-6f ? aoAxis.normalized : Vector3.up;
            for (int j = 0; j <= SegV; j++)
            {
                float theta = j / (float)SegV * Mathf.PI;
                for (int i = 0; i <= SegU; i++)
                {
                    float phi = i / (float)SegU * Mathf.PI * 2f;
                    var dir = new Vector3(Mathf.Sin(theta) * Mathf.Cos(phi), Mathf.Cos(theta), Mathf.Sin(theta) * Mathf.Sin(phi));
                    float r = 1f;
                    for (int l = 0; l < lobes; l++)
                    {
                        float d = Mathf.Max(0f, Vector3.Dot(dir, lobeDir[l]));
                        r += lobeAmp[l] * d * d * d - lobeAmp[l] * 0.18f;
                    }
                    _v.Add(centre + dir * radius * r);
                    _n.Add(dir);
                    _uv.Add(maskUV);
                    _uv1.Add(new Vector2(-1f, seed));
                    float facing = Vector3.Dot(dir, axis) * 0.5f + 0.5f;
                    float ao = Mathf.Lerp(1f - aoStrength, 1f, Mathf.SmoothStep(0f, 1f, facing)) * brightness;
                    _c.Add(new Color(ao, ao, ao, 1f));
                }
            }
            int row = SegU + 1;
            for (int j = 0; j < SegV; j++)
                for (int i = 0; i < SegU; i++)
                {
                    int a = start + j * row + i, b = a + 1, c = a + row, d = c + 1;
                    _t.Add(a); _t.Add(b); _t.Add(c);
                    _t.Add(b); _t.Add(d); _t.Add(c);
                }
            // Smooth normals from the lumpy shape (keeps the soft rounded read).
        }

        public Mesh ToMesh(string name)
        {
            var mesh = new Mesh { name = name, indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            mesh.SetVertices(_v);
            mesh.SetUVs(0, _uv);
            mesh.SetUVs(1, _uv1);
            mesh.SetColors(_c);
            mesh.SetTriangles(_t, 0);
            mesh.RecalculateNormals();
            // Average poles/seams per puff so the rounded shading has no visible seam lines.
            var normals = mesh.normals;
            int perPuff = (SegU + 1) * (SegV + 1);
            for (int p = 0; p + perPuff <= normals.Length; p += perPuff)
            {
                for (int j = 0; j <= SegV; j++)
                {
                    int a = p + j * (SegU + 1), b = a + SegU;
                    var avg = (normals[a] + normals[b]).normalized;
                    normals[a] = avg; normals[b] = avg;
                }
                Vector3 top = Vector3.zero, bottom = Vector3.zero;
                for (int i = 0; i <= SegU; i++) { top += normals[p + i]; bottom += normals[p + SegV * (SegU + 1) + i]; }
                top.Normalize(); bottom.Normalize();
                for (int i = 0; i <= SegU; i++) { normals[p + i] = top; normals[p + SegV * (SegU + 1) + i] = bottom; }
            }
            mesh.normals = normals;
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>The whole sheep coat: an outer layer of puffs plus a smaller, darker filler layer (no bald gaps).</summary>
        public static Mesh BuildCoat(SheepBodySurface surface, float thickness, WoolStylePreset style)
        {
            var b = new WoolPuffMeshBuilder();
            var rng = new System.Random(5821);
            int outer = style.LockCount;
            int inner = Mathf.RoundToInt(outer * 0.55f);
            for (int layer = 0; layer < 2; layer++)
            {
                int count = layer == 0 ? outer : inner;
                for (int k = 0; k < count; k++)
                {
                    float v = Mathf.Acos(1f - 2f * (k + 0.5f) / count) / Mathf.PI;
                    v = Mathf.Clamp01(v + ((float)rng.NextDouble() - 0.5f) * 0.02f);
                    float u = Mathf.Repeat(k * 0.618033989f + (layer * 0.31f) + ((float)rng.NextDouble() - 0.5f) * 0.02f, 1f);
                    if (v > 0.965f) continue; // chest pole sits under the head
                    Vector3 basePoint = surface.GetPoint(u, v);
                    Vector3 n = surface.GetNormal(u, v);
                    float hMul = 0.35f + 0.65f * surface.WoolHeightMultiplier(u, v);
                    float size = Mathf.Lerp(0.78f, 1.22f, (float)rng.NextDouble());
                    float radius = style.LockRadius * size * (layer == 0 ? 1f : 0.85f) * Mathf.Lerp(0.8f, 1f, hMul);
                    float height = thickness * hMul * (layer == 0 ? 0.62f : 0.38f);
                    b.AddPuff(basePoint + n * height, radius, style.Roughness, n, layer == 0 ? 0.3f : 0.42f,
                        new Vector2(u, v), (float)rng.NextDouble(), layer == 0 ? 1f : 0.92f);
                }
            }
            return b.ToMesh(style.Look + "_PuffCoat");
        }

        /// <summary>Decorative cluster (topknot, tail) or a dropped clump: several puffs in a soft round group.</summary>
        public static Mesh BuildCluster(string name, int puffs, float clusterRadius, float puffRadius, float lumpiness, Vector3 aspect, int seed, bool singleBigCore)
        {
            var b = new WoolPuffMeshBuilder();
            var rng = new System.Random(seed);
            if (singleBigCore)
                b.AddPuff(Vector3.zero, puffRadius * 1.35f, lumpiness, Vector3.up, 0.25f, new Vector2(0.5f, 0.5f), (float)rng.NextDouble());
            for (int i = 0; i < puffs; i++)
            {
                var dir = new Vector3((float)rng.NextDouble() * 2f - 1f, (float)rng.NextDouble() * 2f - 1f, (float)rng.NextDouble() * 2f - 1f);
                if (dir.sqrMagnitude < 1e-4f) dir = Vector3.up;
                dir.Normalize();
                float dist = clusterRadius * Mathf.Lerp(0.35f, 1f, Mathf.Sqrt((float)rng.NextDouble()));
                Vector3 c = Vector3.Scale(dir * dist, aspect);
                float r = puffRadius * Mathf.Lerp(0.75f, 1.2f, (float)rng.NextDouble());
                b.AddPuff(c, r, lumpiness, c.sqrMagnitude > 1e-6f ? c : Vector3.up, 0.35f, new Vector2(0.5f, 0.5f), (float)rng.NextDouble());
            }
            return b.ToMesh(name);
        }
    }
}
