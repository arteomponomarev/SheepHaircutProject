using UnityEngine;

namespace ShearAndGrow.Shearing
{
    /// <summary>
    /// One wool clump silhouette prepared for the soft-body solver: rest vertices, control points
    /// (particles) and per-vertex weights to the 3 nearest control points.
    /// </summary>
    public sealed class ClumpVariant
    {
        public Mesh RestMesh;
        public Vector3[] RestVertices;
        public Vector3[] RestNormals;
        public Vector3[] RestParticles;
        public int[] WeightIndex;   // 3 per vertex
        public float[] WeightValue; // 3 per vertex
        /// <summary>Particle index that is pinned while hanging (top of the clump).</summary>
        public int PinParticle;
        public float BoundingRadius;
        public float Height;
    }

    /// <summary>Deterministically generates ≥5 visibly different fluffy clump shapes (spec §6.5).</summary>
    public static class ClumpMeshLibrary
    {
        public const int ParticleCount = 7;

        public static ClumpVariant[] Build(int variantCount, int seed)
        {
            variantCount = Mathf.Max(5, variantCount);
            var rng = new System.Random(seed);
            var result = new ClumpVariant[variantCount];
            for (int k = 0; k < variantCount; k++) result[k] = BuildVariant(k, rng);
            return result;
        }

        static float R(System.Random r, float a, float b) => a + (float)r.NextDouble() * (b - a);

        static ClumpVariant BuildVariant(int index, System.Random rng)
        {
            // Shape archetypes: round puff, long lock, flat mat, twin lobe, curly cluster, droopy tuft...
            int lobeCount = 3 + (index % 4) + rng.Next(0, 2);
            var lobeDirs = new Vector3[lobeCount];
            var lobeAmp = new float[lobeCount];
            for (int i = 0; i < lobeCount; i++)
            {
                lobeDirs[i] = new Vector3(R(rng, -1, 1), R(rng, -0.6f, 1f), R(rng, -1, 1)).normalized;
                lobeAmp[i] = R(rng, 0.18f, 0.42f);
            }
            Vector3 aspect;
            switch (index % 6)
            {
                case 0: aspect = new Vector3(1f, 0.8f, 1f); break;        // round puff
                case 1: aspect = new Vector3(0.75f, 1.35f, 0.75f); break; // long lock
                case 2: aspect = new Vector3(1.3f, 0.55f, 1.1f); break;   // flat mat
                case 3: aspect = new Vector3(1.35f, 0.8f, 0.8f); break;   // twin lobe
                case 4: aspect = new Vector3(1.05f, 0.95f, 0.9f); break;  // curly cluster
                default: aspect = new Vector3(0.85f, 1.15f, 1.05f); break; // droopy tuft
            }
            float curlFreq = R(rng, 5f, 9f);
            float curlAmp = R(rng, 0.06f, 0.12f);
            Vector3 curlOffset = new Vector3(R(rng, 0, 50), R(rng, 0, 50), R(rng, 0, 50));

            float Radial(Vector3 dir)
            {
                float r = 0.62f;
                for (int i = 0; i < lobeCount; i++)
                {
                    float d = Mathf.Max(0f, Vector3.Dot(dir, lobeDirs[i]));
                    r += lobeAmp[i] * d * d * d;
                }
                Vector3 p = dir * curlFreq + curlOffset;
                float n = Mathf.PerlinNoise(p.x, p.y + p.z * 0.37f) - 0.5f;
                float n2 = Mathf.PerlinNoise(p.z * 2.1f, p.x * 2.1f + p.y) - 0.5f;
                return r * (1f + n * curlAmp * 2f + n2 * curlAmp);
            }

            // Cartoon puff cluster matching the reference coat: a few round, lumpy puffs grouped together
            // (index 0 is one big single puff, like the loose floating puff in the reference).
            int puffCount = index == 0 ? 0 : 3 + (index % 5) + rng.Next(0, 2);
            var mesh = WoolPuffMeshBuilder.BuildCluster("WoolClump_" + index, puffCount, 0.26f, index == 0 ? 0.34f : 0.2f,
                0.22f, aspect, 7000 + index * 31, index == 0 || index % 2 == 1);
            var verts = mesh.vertices;
            float maxR = 0f, minY = float.MaxValue, maxY = float.MinValue;
            for (int i = 0; i < verts.Length; i++)
            {
                maxR = Mathf.Max(maxR, verts[i].magnitude);
                minY = Mathf.Min(minY, verts[i].y);
                maxY = Mathf.Max(maxY, verts[i].y);
            }

            // Control points: centre + up to 6 points along the dominant lobes / aspect axes.
            var particles = new Vector3[ParticleCount];
            particles[0] = Vector3.zero;
            Vector3[] axes =
            {
                Vector3.up, Vector3.down, Vector3.right, Vector3.left, Vector3.forward, Vector3.back
            };
            for (int i = 0; i < 6; i++)
            {
                Vector3 dir = axes[i];
                if (i < lobeCount) dir = (dir + lobeDirs[i] * 0.5f).normalized;
                particles[i + 1] = Vector3.Scale(dir, aspect * 0.5f) * Radial(dir.normalized) * 0.62f;
            }

            int pin = 1; // "up" particle
            float bestUp = float.MinValue;
            for (int i = 1; i < ParticleCount; i++)
                if (particles[i].y > bestUp) { bestUp = particles[i].y; pin = i; }

            var wIdx = new int[verts.Length * 3];
            var wVal = new float[verts.Length * 3];
            for (int v = 0; v < verts.Length; v++)
            {
                int i0 = -1, i1 = -1, i2 = -1;
                float d0 = float.MaxValue, d1 = float.MaxValue, d2 = float.MaxValue;
                for (int p = 0; p < ParticleCount; p++)
                {
                    float d = (verts[v] - particles[p]).sqrMagnitude;
                    if (d < d0) { d2 = d1; i2 = i1; d1 = d0; i1 = i0; d0 = d; i0 = p; }
                    else if (d < d1) { d2 = d1; i2 = i1; d1 = d; i1 = p; }
                    else if (d < d2) { d2 = d; i2 = p; }
                }
                float w0 = 1f / (d0 + 1e-4f), w1 = 1f / (d1 + 1e-4f), w2 = 1f / (d2 + 1e-4f);
                float sum = w0 + w1 + w2;
                wIdx[v * 3] = i0; wIdx[v * 3 + 1] = i1; wIdx[v * 3 + 2] = i2;
                wVal[v * 3] = w0 / sum; wVal[v * 3 + 1] = w1 / sum; wVal[v * 3 + 2] = w2 / sum;
            }

            return new ClumpVariant
            {
                RestMesh = mesh,
                RestVertices = verts,
                RestNormals = mesh.normals,
                RestParticles = particles,
                WeightIndex = wIdx,
                WeightValue = wVal,
                PinParticle = pin,
                BoundingRadius = maxR,
                Height = maxY - minY,
            };
        }
    }
}
