using System;
using System.Collections.Generic;
using UnityEngine;

namespace ShearAndGrow.Shearing
{
    /// <summary>Small procedural mesh toolkit for placeholder sheep / trimmer / environment art.</summary>
    public static class ProceduralMeshFactory
    {
        /// <summary>Super-ellipsoid (exponent 2 = ellipsoid, higher = boxier). Optional radial displacement.</summary>
        public static Mesh SuperEllipsoid(string name, Vector3 radii, float exponent = 2f, int segU = 32, int segV = 20,
            Func<Vector3, float> radialDisplacement = null, Color? vertexColor = null)
        {
            int vu = segU + 1, vv = segV + 1;
            var verts = new Vector3[vu * vv];
            var uvs = new Vector2[vu * vv];
            var cols = new Color[vu * vv];
            float e = 2f / Mathf.Max(exponent, 0.5f);
            for (int j = 0; j < vv; j++)
            {
                float v = j / (float)segV;
                float theta = v * Mathf.PI - Mathf.PI * 0.5f;
                for (int i = 0; i < vu; i++)
                {
                    float u = i / (float)segU;
                    float phi = u * Mathf.PI * 2f;
                    float ct = SignPow(Mathf.Cos(theta), e), st = SignPow(Mathf.Sin(theta), e);
                    float cp = SignPow(Mathf.Cos(phi), e), sp = SignPow(Mathf.Sin(phi), e);
                    var dir = new Vector3(ct * cp, st, ct * sp);
                    float disp = radialDisplacement != null ? radialDisplacement(dir.normalized) : 1f;
                    verts[j * vu + i] = Vector3.Scale(dir, radii) * disp;
                    uvs[j * vu + i] = new Vector2(u, v);
                    cols[j * vu + i] = vertexColor ?? Color.white;
                }
            }
            var mesh = new Mesh { name = name };
            mesh.vertices = verts;
            mesh.uv = uvs;
            mesh.colors = cols;
            mesh.triangles = GridTriangles(segU, segV, true);
            mesh.RecalculateNormals();
            WeldSeamNormals(mesh, segU, segV);
            mesh.RecalculateBounds();
            return mesh;
        }

        public static Mesh Ellipsoid(string name, Vector3 radii, int segU = 28, int segV = 18) => SuperEllipsoid(name, radii, 2f, segU, segV);

        public static Mesh Cylinder(string name, float radiusBottom, float radiusTop, float height, int seg = 20, bool caps = true)
        {
            var verts = new List<Vector3>();
            var tris = new List<int>();
            for (int i = 0; i <= seg; i++)
            {
                float a = i / (float)seg * Mathf.PI * 2f;
                var d = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
                verts.Add(d * radiusBottom);
                verts.Add(d * radiusTop + Vector3.up * height);
            }
            for (int i = 0; i < seg; i++)
            {
                int a = i * 2, b = a + 1, c = a + 2, d = a + 3;
                tris.Add(a); tris.Add(b); tris.Add(c);
                tris.Add(b); tris.Add(d); tris.Add(c);
            }
            if (caps)
            {
                int cb = verts.Count; verts.Add(Vector3.zero);
                int ct = verts.Count; verts.Add(Vector3.up * height);
                int ringB = verts.Count;
                for (int i = 0; i <= seg; i++)
                {
                    float a = i / (float)seg * Mathf.PI * 2f;
                    var d = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
                    verts.Add(d * radiusBottom);
                    verts.Add(d * radiusTop + Vector3.up * height);
                }
                for (int i = 0; i < seg; i++)
                {
                    int b0 = ringB + i * 2, b1 = ringB + (i + 1) * 2;
                    tris.Add(cb); tris.Add(b0); tris.Add(b1);
                    tris.Add(ct); tris.Add(b1 + 1); tris.Add(b0 + 1);
                }
            }
            var mesh = new Mesh { name = name };
            mesh.SetVertices(verts);
            mesh.SetTriangles(tris, 0);
            var cols = new Color[verts.Count];
            for (int i = 0; i < cols.Length; i++) cols[i] = Color.white;
            mesh.colors = cols;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        public static Mesh Box(string name, Vector3 size)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            var src = go.GetComponent<MeshFilter>().sharedMesh;
            var mesh = UnityEngine.Object.Instantiate(src);
            if (Application.isPlaying) UnityEngine.Object.Destroy(go); else UnityEngine.Object.DestroyImmediate(go);
            var v = mesh.vertices;
            for (int i = 0; i < v.Length; i++) v[i] = Vector3.Scale(v[i], size);
            mesh.vertices = v;
            var cols = new Color[v.Length];
            for (int i = 0; i < cols.Length; i++) cols[i] = Color.white;
            mesh.colors = cols;
            mesh.name = name;
            mesh.RecalculateBounds();
            return mesh;
        }

        public static Mesh Combine(string name, IList<(Mesh mesh, Matrix4x4 matrix)> parts)
        {
            var ci = new CombineInstance[parts.Count];
            for (int i = 0; i < parts.Count; i++) ci[i] = new CombineInstance { mesh = parts[i].mesh, transform = parts[i].matrix };
            var m = new Mesh { name = name, indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            m.CombineMeshes(ci, true, true);
            m.RecalculateBounds();
            return m;
        }

        static float SignPow(float x, float p) => Mathf.Sign(x) * Mathf.Pow(Mathf.Abs(x), p);

        static int[] GridTriangles(int segU, int segV, bool outward)
        {
            int vu = segU + 1;
            var tris = new int[segU * segV * 6];
            int t = 0;
            for (int j = 0; j < segV; j++)
                for (int i = 0; i < segU; i++)
                {
                    int a = j * vu + i, b = a + 1, c = a + vu, d = c + 1;
                    if (outward)
                    {
                        tris[t++] = a; tris[t++] = c; tris[t++] = b;
                        tris[t++] = b; tris[t++] = c; tris[t++] = d;
                    }
                    else
                    {
                        tris[t++] = a; tris[t++] = b; tris[t++] = c;
                        tris[t++] = b; tris[t++] = d; tris[t++] = c;
                    }
                }
            return tris;
        }

        static void WeldSeamNormals(Mesh mesh, int segU, int segV)
        {
            var n = mesh.normals;
            int vu = segU + 1;
            for (int j = 0; j <= segV; j++)
            {
                int a = j * vu, b = j * vu + segU;
                var avg = (n[a] + n[b]).normalized;
                n[a] = avg; n[b] = avg;
            }
            // poles
            Vector3 bottom = Vector3.zero, top = Vector3.zero;
            for (int i = 0; i <= segU; i++) { bottom += n[i]; top += n[segV * vu + i]; }
            bottom.Normalize(); top.Normalize();
            for (int i = 0; i <= segU; i++) { n[i] = bottom; n[segV * vu + i] = top; }
            mesh.normals = n;
        }
    }
}
