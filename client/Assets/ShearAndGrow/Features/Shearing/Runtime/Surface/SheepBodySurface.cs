using UnityEngine;
using UnityEngine.Rendering;

namespace ShearAndGrow.Shearing
{
    /// <summary>
    /// Stable analytic parametrisation of the woolly body: u = angle around the long (X) axis,
    /// v = position from rump (-X) to chest (+X). Every mask texel has an exact object-space
    /// position, so brush stamps are evaluated by 3D distance and are seam-safe by construction (spec §5.3).
    /// </summary>
    [System.Serializable]
    public sealed class SheepBodySurface
    {
        public float RadiusX = 0.72f;
        public float RadiusY = 0.5f;
        public float RadiusZ = 0.5f;
        [Tooltip("Belly flattening (multiplier for the lower half).")]
        public float BellyScale = 0.88f;
        [Tooltip("Rump is a little fuller than the chest.")]
        public float RumpBias = 0.07f;
        public int SegmentsU = 80;
        public int SegmentsV = 44;

        public Vector3 GetPoint(float u, float v)
        {
            float phi = u * Mathf.PI * 2f;
            float theta = Mathf.Clamp01(v) * Mathf.PI;
            float s = Mathf.Sin(theta);
            float c = Mathf.Cos(theta);
            float rScale = 1f + RumpBias * c;
            float cy = Mathf.Cos(phi);
            float sy = Mathf.Sin(phi);
            float ry = RadiusY * rScale * (cy < 0f ? BellyScale : 1f);
            float rz = RadiusZ * rScale;
            return new Vector3(-c * RadiusX, cy * s * ry, sy * s * rz);
        }

        /// <summary>Rows closer than this to a pole use the exact pole normal (±X).</summary>
        const float PoleBand = 0.004f;

        public Vector3 GetNormal(float u, float v)
        {
            // Exact pole normals. Near the poles the parametric derivative along u vanishes (sin θ → 0); the old code
            // fed a ~1e-6 cross product into Vector3.Normalize, which returns (0,0,0) below 1e-5. Zero normals reached
            // the shaders as normalize(0) = NaN → NaN pixels exploded by Bloom into the glow behind the sheep (Bug 1).
            if (v <= PoleBand) return Vector3.left;
            if (v >= 1f - PoleBand) return Vector3.right;

            // Central differences with a step that scales with the local ring radius, so the tangent along u keeps
            // a usable length all the way to the pole band.
            const float ev = 0.0025f;
            float ring = Mathf.Max(Mathf.Sin(Mathf.Clamp01(v) * Mathf.PI), 0.02f);
            float eu = Mathf.Min(0.02f, ev / ring);
            float v0 = Mathf.Max(v - ev, 0f), v1 = Mathf.Min(v + ev, 1f);
            Vector3 du = GetPoint(u + eu, v) - GetPoint(u - eu, v);
            Vector3 dv = GetPoint(u, v1) - GetPoint(u, v0);
            Vector3 n = Vector3.Cross(dv, du);
            float sq = n.sqrMagnitude;
            if (!(sq > 1e-24f)) return v < 0.5f ? Vector3.left : Vector3.right; // also catches NaN
            n /= Mathf.Sqrt(sq); // manual normalisation: no epsilon cut-off to zero
            if (Vector3.Dot(n, GetPoint(u, v)) < 0f) n = -n; // outward
            return n;
        }

        /// <summary>Relative wool length (shorter under the belly and around the neck where the head sits).</summary>
        public float WoolHeightMultiplier(float u, float v)
        {
            Vector3 n = GetNormal(u, v);
            float belly = Mathf.SmoothStep(0.3f, 1f, Mathf.InverseLerp(-0.95f, -0.35f, n.y));
            float neck = 1f - 0.35f * Mathf.SmoothStep(0.85f, 1f, v);
            return Mathf.Clamp01(belly) * neck;
        }

        /// <summary>
        /// Weight of a texel in progress. The belly IS shearable (reachable via the belly roll / low camera); only the
        /// chest pole hidden under the head is excluded. Texel layout is unchanged, so existing saves stay valid
        /// (progress is recomputed from the saved pattern; credited area can never be credited twice).
        /// </summary>
        public bool IsShearable(float u, float v)
        {
            if (v > 0.93f) return false;
            return true;
        }

        public Mesh BuildSurfaceMesh(string name, float normalOffset)
        {
            int vu = SegmentsU + 1, vv = SegmentsV + 1;
            var verts = new Vector3[vu * vv];
            var normals = new Vector3[vu * vv];
            var uvs = new Vector2[vu * vv];
            for (int j = 0; j < vv; j++)
            {
                float v = j / (float)SegmentsV;
                for (int i = 0; i < vu; i++)
                {
                    float u = i / (float)SegmentsU;
                    int idx = j * vu + i;
                    Vector3 n = GetNormal(u, v);
                    verts[idx] = GetPoint(u, v) + n * normalOffset;
                    normals[idx] = n;
                    uvs[idx] = new Vector2(u, v);
                }
            }
            var mesh = new Mesh { name = name, indexFormat = IndexFormat.UInt32 };
            mesh.vertices = verts;
            mesh.normals = normals;
            mesh.uv = uvs;
            mesh.triangles = BuildGridTriangles(0);
            mesh.RecalculateBounds();
            mesh.RecalculateTangents();
            return mesh;
        }

        /// <summary>
        /// One mesh containing all wool shells (single draw call). uv1.x = shell height 0..1, color.r = wool length multiplier.
        /// </summary>
        public Mesh BuildShellMesh(int shellCount, float thickness)
        {
            int vu = SegmentsU + 1, vv = SegmentsV + 1;
            int perShell = vu * vv;
            var verts = new Vector3[perShell * shellCount];
            var normals = new Vector3[perShell * shellCount];
            var uv0 = new Vector2[perShell * shellCount];
            var uv1 = new Vector2[perShell * shellCount];
            var colors = new Color32[perShell * shellCount];
            var baseP = new Vector3[perShell];
            var baseN = new Vector3[perShell];
            var heightMul = new byte[perShell];
            for (int j = 0; j < vv; j++)
            {
                float v = j / (float)SegmentsV;
                for (int i = 0; i < vu; i++)
                {
                    float u = i / (float)SegmentsU;
                    int idx = j * vu + i;
                    baseP[idx] = GetPoint(u, v);
                    baseN[idx] = GetNormal(u, v);
                    heightMul[idx] = (byte)Mathf.RoundToInt(WoolHeightMultiplier(u, v) * 255f);
                }
            }
            for (int s = 0; s < shellCount; s++)
            {
                float t = shellCount <= 1 ? 1f : s / (float)(shellCount - 1);
                int o = s * perShell;
                for (int k = 0; k < perShell; k++)
                {
                    // Positions are the base surface; the shader extrudes by t * thickness * mask so
                    // a cut really removes thickness (not only colour).
                    verts[o + k] = baseP[k];
                    normals[o + k] = baseN[k];
                    int i = k % vu, j = k / vu;
                    uv0[o + k] = new Vector2(i / (float)SegmentsU, j / (float)SegmentsV);
                    uv1[o + k] = new Vector2(t, s);
                    colors[o + k] = new Color32(heightMul[k], 0, 0, 255);
                }
            }
            var tris = new int[SegmentsU * SegmentsV * 6 * shellCount];
            for (int s = 0; s < shellCount; s++)
            {
                var shellTris = BuildGridTriangles(s * perShell);
                System.Array.Copy(shellTris, 0, tris, s * shellTris.Length, shellTris.Length);
            }
            var mesh = new Mesh { name = "WoolShells", indexFormat = IndexFormat.UInt32 };
            mesh.vertices = verts;
            mesh.normals = normals;
            mesh.uv = uv0;
            mesh.uv2 = uv1;
            mesh.colors32 = colors;
            mesh.triangles = tris;
            mesh.RecalculateBounds();
            var b = mesh.bounds;
            b.Expand(thickness * 2.5f);
            mesh.bounds = b;
            return mesh;
        }

        int[] BuildGridTriangles(int offset)
        {
            int vu = SegmentsU + 1;
            var tris = new int[SegmentsU * SegmentsV * 6];
            int t = 0;
            for (int j = 0; j < SegmentsV; j++)
            {
                for (int i = 0; i < SegmentsU; i++)
                {
                    int a = offset + j * vu + i;
                    int b = a + 1;
                    int c = a + vu;
                    int d = c + 1;
                    tris[t++] = a; tris[t++] = b; tris[t++] = c;
                    tris[t++] = b; tris[t++] = d; tris[t++] = c;
                }
            }
            return tris;
        }
    }
}
