using System.Collections.Generic;
using UnityEngine;

namespace ShearAndGrow.Shearing
{
    public static class WoolCurlGeometry
    {
        public static Mesh Build(SheepBodySurface surface, float thickness, WoolStylePreset style)
        {
            const int lengthSteps = 24, radialSteps = 6;
            var vertices = new List<Vector3>();
            var normals = new List<Vector3>();
            var uv = new List<Vector2>();
            var shell = new List<Vector2>();
            var colors = new List<Color>();
            var triangles = new List<int>();
            var rng = new System.Random(9147);
            for (int k = 0; k < style.LockCount; k++)
            {
                // Fibonacci distribution gives even density, with small deterministic jitter.
                float v = Mathf.Acos(1f - 2f * (k + 0.5f) / style.LockCount) / Mathf.PI;
                v = Mathf.Clamp01(v + ((float)rng.NextDouble() - 0.5f) * 0.025f);
                float u = Mathf.Repeat(k * 0.618033989f + (float)rng.NextDouble() * 0.035f, 1f);
                Vector3 basePoint = surface.GetPoint(u, v);
                Vector3 n = surface.GetNormal(u, v);
                Vector3 a = Vector3.Cross(n, Mathf.Abs(n.x) < 0.9f ? Vector3.right : Vector3.up).normalized;
                Vector3 b = Vector3.Cross(n, a).normalized;
                float angle = (float)rng.NextDouble() * Mathf.PI * 2f;
                float variation = Mathf.Lerp(0.78f, 1.2f, (float)rng.NextDouble());
                float radius = style.LockRadius * variation;
                float tube = style.FibreRadius * variation;
                float height = thickness * (0.35f + 0.65f * surface.WoolHeightMultiplier(u, v));
                float turns = style.Turns * Mathf.Lerp(0.72f, 1.28f, (float)rng.NextDouble());
                float lift = Mathf.Lerp(0.63f, 0.86f, (float)rng.NextDouble());
                int start = vertices.Count;
                for (int j = 0; j <= lengthSteps; j++)
                {
                    float t = j / (float)lengthSteps;
                    float theta = angle + t * turns * Mathf.PI * 2f;
                    float r = radius * Mathf.Lerp(1f, 0.2f, t);
                    float wobble = 1f + Mathf.Sin(theta * 3f + k) * style.Roughness * 0.13f;
                    Vector3 radial = a * Mathf.Cos(theta) + b * Mathf.Sin(theta);
                    Vector3 centre = basePoint + radial * r * wobble + n * (height * (lift + t * 0.16f));
                    Vector3 tangent = (-a * Mathf.Sin(theta) + b * Mathf.Cos(theta) + n * 0.18f).normalized;
                    Vector3 across = Vector3.Cross(tangent, n).normalized;
                    Vector3 up = Vector3.Cross(across, tangent).normalized;
                    float tipTaper = Mathf.Lerp(1f, 0.42f, Mathf.SmoothStep(0.75f, 1f, t));
                    for (int i = 0; i <= radialSteps; i++)
                    {
                        float phi = i / (float)radialSteps * Mathf.PI * 2f;
                        Vector3 normal = across * Mathf.Cos(phi) + up * Mathf.Sin(phi);
                        float crimp = 1f + Mathf.Sin(theta * 11f + phi * 5f) * style.Roughness * 0.08f;
                        Vector3 p = centre + normal * tube * tipTaper * crimp;
                        vertices.Add(p);
                        normals.Add(normal);
                        // Project each point back to the stable base parametrisation; unwrap per lock.
                        float ry = surface.RadiusY * (p.y < 0 ? surface.BellyScale : 1f);
                        float pu = Mathf.Atan2(p.z / surface.RadiusZ, p.y / ry) / (2f * Mathf.PI);
                        pu += Mathf.Round(u - pu);
                        float pv = Mathf.Acos(Mathf.Clamp(-p.x / surface.RadiusX, -1f, 1f)) / Mathf.PI;
                        uv.Add(new Vector2(pu, pv));
                        shell.Add(new Vector2(-1f, 0f)); // baked geometry, not an extruded shell
                        colors.Add(new Color(Mathf.Lerp(0.72f, 1f, t), 1f, 1f, 1f));
                    }
                }
                for (int j = 0; j < lengthSteps; j++)
                    for (int i = 0; i < radialSteps; i++)
                    {
                        int x = start + j * (radialSteps + 1) + i;
                        int y = x + radialSteps + 1;
                        triangles.Add(x); triangles.Add(y); triangles.Add(x + 1);
                        triangles.Add(x + 1); triangles.Add(y); triangles.Add(y + 1);
                    }
            }
            var mesh = new Mesh { name = style.Look + "_CoiledLocks", indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            mesh.SetVertices(vertices); mesh.SetNormals(normals); mesh.SetUVs(0, uv);
            mesh.SetUVs(1, shell); mesh.SetColors(colors); mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
