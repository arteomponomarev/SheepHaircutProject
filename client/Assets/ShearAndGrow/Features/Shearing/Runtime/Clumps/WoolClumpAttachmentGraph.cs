using System.Collections.Generic;
using UnityEngine;

namespace ShearAndGrow.Shearing
{
    public enum ClumpState : byte { Attached, Hanging, Released }

    public sealed class ClumpNode
    {
        public int Id;
        public Vector3 CentreLocal;
        public Vector3 NormalLocal;
        public float RegionRadius;
        public bool IsHero;
        public int VariantIndex;
        public float Scale;
        public float HangDuration;
        public int[] SupportTexels;
        public int[] AnchorTexels;
        public ClumpState State;
        public float Support = 1f;
    }

    /// <summary>
    /// Pre-computed, deterministic map from wool regions to physical clumps. A clump can only come
    /// into existence from its own region of authoritative wool, exactly once (spec §6.3).
    /// </summary>
    public sealed class WoolClumpAttachmentGraph
    {
        public readonly List<ClumpNode> Nodes = new List<ClumpNode>(512);
        readonly ShearMaskAuthority _mask;
        readonly SheepBodySurface _surface;
        readonly List<ClumpNode> _transitions = new List<ClumpNode>(32);

        public WoolClumpAttachmentGraph(SheepBodySurface surface, ShearMaskAuthority mask, ChunkPhysicsConfig cfg, int variantCount, int seed)
        {
            _surface = surface;
            _mask = mask;
            var rng = new System.Random(seed);

            // Area-uniform random candidates via rejection on the parametrisation, Poisson-disk filtered.
            int attempts = 9000;
            var grid = new Dictionary<Vector3Int, List<ClumpNode>>();
            float cell = cfg.ClumpRegionRadius * 2f;
            for (int a = 0; a < attempts; a++)
            {
                float u = (float)rng.NextDouble();
                float v = Mathf.Acos(1f - 2f * (float)rng.NextDouble()) / Mathf.PI; // area-uniform on sphere-like param
bool hero = rng.NextDouble() < cfg.HeroClumpFraction;
                float radius = cfg.ClumpRegionRadius * (hero ? cfg.HeroClumpScale : Mathf.Lerp(0.8f, 1.1f, (float)rng.NextDouble()));
                Vector3 p = surface.GetPoint(u, v);
                var key = new Vector3Int(Mathf.FloorToInt(p.x / cell), Mathf.FloorToInt(p.y / cell), Mathf.FloorToInt(p.z / cell));
                bool ok = true;
                for (int dz = -2; dz <= 2 && ok; dz++)
                    for (int dy = -2; dy <= 2 && ok; dy++)
                        for (int dx = -2; dx <= 2 && ok; dx++)
                        {
                            if (!grid.TryGetValue(key + new Vector3Int(dx, dy, dz), out var list)) continue;
                            foreach (var other in list)
                            {
                                float minD = (radius + other.RegionRadius) * 0.82f;
                                if ((other.CentreLocal - p).sqrMagnitude < minD * minD) { ok = false; break; }
                            }
                        }
                if (!ok) continue;

                var node = new ClumpNode
                {
                    Id = Nodes.Count,
                    CentreLocal = p,
                    NormalLocal = surface.GetNormal(u, v),
                    RegionRadius = radius,
                    IsHero = hero,
                    VariantIndex = rng.Next(0, variantCount),
                    Scale = radius * 2.35f * Mathf.Lerp(0.9f, 1.1f, (float)rng.NextDouble()) * Mathf.Lerp(0.7f, 1f, surface.WoolHeightMultiplier(u, v)),
                    HangDuration = Mathf.Lerp(cfg.HangDuration.x, cfg.HangDuration.y, (float)rng.NextDouble()) + (hero ? cfg.HeroExtraHang : 0f),
                };
                node.SupportTexels = SampleDisc(u, v, radius * 0.85f, 14, rng);
                node.AnchorTexels = SampleRing(u, v, radius * 1.35f, radius * 1.9f, 10, rng);
                Nodes.Add(node);
                if (!grid.TryGetValue(key, out var cellList)) grid[key] = cellList = new List<ClumpNode>();
                cellList.Add(node);
            }
            PromoteHeroes(cfg, rng);
            RefreshAllSupport();
        }

        /// <summary>
        /// Poisson spacing rejects most large candidates, so far fewer than HeroClumpFraction end up as heroes.
        /// Promote random regular clumps (deterministic) without touching their support region / spacing.
        /// </summary>
        void PromoteHeroes(ChunkPhysicsConfig cfg, System.Random rng)
        {
            int hero = 0;
            foreach (var n in Nodes) if (n.IsHero) hero++;
            int target = Mathf.RoundToInt(Nodes.Count * cfg.HeroClumpFraction);
            int guard = Nodes.Count * 4;
            while (hero < target && guard-- > 0)
            {
                var n = Nodes[rng.Next(0, Nodes.Count)];
                if (n.IsHero) continue;
                n.IsHero = true;
                n.Scale *= Mathf.Lerp(1.25f, 1.4f, (float)rng.NextDouble());
                n.HangDuration += cfg.HeroExtraHang;
                hero++;
            }
        }

        int[] SampleDisc(float u, float v, float radius, int count, System.Random rng)
        {
            var result = new int[count];
            for (int i = 0; i < count; i++)
            {
                float a = (float)rng.NextDouble() * Mathf.PI * 2f;
                float r = Mathf.Sqrt((float)rng.NextDouble()) * radius;
                if (i == 0) r = 0f;
                result[i] = OffsetTexel(u, v, Mathf.Cos(a) * r, Mathf.Sin(a) * r);
            }
            return result;
        }

        int[] SampleRing(float u, float v, float rMin, float rMax, int count, System.Random rng)
        {
            var result = new int[count];
            for (int i = 0; i < count; i++)
            {
                float a = (i + (float)rng.NextDouble() * 0.5f) / count * Mathf.PI * 2f;
                float r = Mathf.Lerp(rMin, rMax, (float)rng.NextDouble());
                result[i] = OffsetTexel(u, v, Mathf.Cos(a) * r, Mathf.Sin(a) * r);
            }
            return result;
        }

        /// <summary>Offsets (u,v) by metric distances along the surface parameter directions.</summary>
        int OffsetTexel(float u, float v, float alongU, float alongV)
        {
            const float e = 0.002f;
            float vv = Mathf.Clamp(v, e, 1f - e);
            float lenU = (_surface.GetPoint(u + e, vv) - _surface.GetPoint(u - e, vv)).magnitude / (2f * e);
            float lenV = (_surface.GetPoint(u, vv + e) - _surface.GetPoint(u, vv - e)).magnitude / (2f * e);
            float nu = u + alongU / Mathf.Max(lenU, 0.05f);
            float nv = Mathf.Clamp01(v + alongV / Mathf.Max(lenV, 0.05f));
            return _mask.TexelIndex(nu, nv);
        }

        float ComputeSupport(ClumpNode n)
        {
            int wool = 0;
            var s = n.SupportTexels;
            for (int i = 0; i < s.Length; i++) if (_mask.HasWool(s[i])) wool++;
            return wool / (float)s.Length;
        }

        /// <summary>Re-evaluates every node (used after restoring a saved mask). Already-cut regions become Released silently.</summary>
        public void RefreshAllSupport()
        {
            foreach (var n in Nodes)
            {
                n.Support = ComputeSupport(n);
                n.State = n.Support < 0.6f ? ClumpState.Released : ClumpState.Attached;
            }
        }

        /// <summary>
        /// Updates support of nodes affected by a stamp. Returns nodes that just crossed the hanging threshold.
        /// The returned list is reused (no allocation).
        /// </summary>
        public List<ClumpNode> OnStamp(Vector3 stampLocal, float brushRadius, float hangingThreshold)
        {
            _transitions.Clear();
            for (int i = 0; i < Nodes.Count; i++)
            {
                var n = Nodes[i];
                if (n.State == ClumpState.Released) continue;
                float reach = brushRadius + n.RegionRadius;
                if ((n.CentreLocal - stampLocal).sqrMagnitude > reach * reach) continue;
                n.Support = ComputeSupport(n);
                if (n.State == ClumpState.Attached && n.Support < hangingThreshold)
                {
                    n.State = ClumpState.Hanging;
                    _transitions.Add(n);
                }
            }
            return _transitions;
        }

        public float CurrentSupport(ClumpNode n)
        {
            n.Support = ComputeSupport(n);
            return n.Support;
        }

        /// <summary>
        /// Finds the remaining-wool anchor a hanging clump clings to. Prefers anchors above the clump (it hangs down from them).
        /// Returns false if no wool remains nearby (the clump then clings to its own cut spot for the minimum hang time).
        /// </summary>
        public bool TryFindAnchor(ClumpNode n, Vector3 worldUpLocal, out Vector3 anchorLocal)
        {
            float best = float.MinValue;
            anchorLocal = n.CentreLocal;
            bool found = false;
            var a = n.AnchorTexels;
            for (int i = 0; i < a.Length; i++)
            {
                if (!_mask.HasWool(a[i])) continue;
                Vector3 p = _mask.TexelPosition(a[i]);
                float score = Vector3.Dot((p - n.CentreLocal).normalized, worldUpLocal);
                if (score > best) { best = score; anchorLocal = p; found = true; }
            }
            return found;
        }

        public void MarkReleased(ClumpNode n) => n.State = ClumpState.Released;

        public void ResetAll()
        {
            foreach (var n in Nodes) { n.State = ClumpState.Attached; n.Support = 1f; }
        }
    }
}
