using System;
using UnityEngine;

namespace ShearAndGrow
{
    /// <summary>Maps cut UV cells back to a readable static mesh. No input or physics queries.</summary>
    public sealed class WoolSurfaceLookup
    {
        private readonly Transform surface;
        private readonly Vector3[] vertices;
        private readonly Vector3[] normals;
        private readonly Vector2[] uvs;
        private readonly int[] triangles;

        public WoolSurfaceLookup(MeshCollider collider)
        {
            if (collider == null || collider.sharedMesh == null || !collider.sharedMesh.isReadable)
                throw new ArgumentException("Wool surface lookup requires a readable MeshCollider mesh.");
            surface = collider.transform;
            Mesh mesh = collider.sharedMesh;
            vertices = mesh.vertices; normals = mesh.normals; uvs = mesh.uv; triangles = mesh.triangles;
            if (uvs.Length != vertices.Length) throw new ArgumentException("Wool mesh requires UV0 for each vertex.");
        }

        // Called only for a bounded number of potential spawns, not every brush stamp.
        public bool TryGetPoint(Vector2 uv, out Vector3 point, out Vector3 normal)
        {
            point = normal = default;
            if (surface == null) return false;
            for (int i = 0; i < triangles.Length; i += 3)
            {
                int a = triangles[i], b = triangles[i + 1], c = triangles[i + 2];
                Vector2 ab = uvs[b] - uvs[a], ac = uvs[c] - uvs[a], offset = uv - uvs[a];
                float determinant = ab.x * ac.y - ab.y * ac.x;
                if (Mathf.Abs(determinant) < 0.0000001f) continue;
                float v = (offset.x * ac.y - offset.y * ac.x) / determinant;
                float w = (ab.x * offset.y - ab.y * offset.x) / determinant;
                float u = 1f - v - w;
                if (u < -0.00001f || v < -0.00001f || w < -0.00001f) continue;
                point = surface.TransformPoint(vertices[a] * u + vertices[b] * v + vertices[c] * w);
                Vector3 localNormal = normals.Length == vertices.Length
                    ? normals[a] * u + normals[b] * v + normals[c] * w
                    : Vector3.Cross(vertices[b] - vertices[a], vertices[c] - vertices[a]);
                normal = surface.localToWorldMatrix.inverse.transpose.MultiplyVector(localNormal).normalized;
                return normal.sqrMagnitude > 0.5f;
            }
            return false;
        }
    }
}
