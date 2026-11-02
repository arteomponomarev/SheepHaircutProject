using System.Collections.Generic;
using UnityEngine;

namespace ShearAndGrow.Shearing
{
    /// <summary>
    /// Keeps every landed clump visible until the session is closed (spec §6.4 P0).
    /// Settled clumps raise a pile heightfield so new clumps land ON TOP of old ones (no all-to-all collisions).
    /// When the individual budget is exceeded, the oldest settled clumps are merged into static pile meshes
    /// in the same frame — geometry is identical, so nothing pops or disappears.
    /// </summary>
    public sealed class FloorWoolAccumulator : MonoBehaviour
    {
        [SerializeField] private Material _pileMaterial;

        ChunkPhysicsConfig _cfg;
        float[] _height;
        int _res;
        Vector2 _half;
        readonly List<SoftClumpInstance> _settled = new List<SoftClumpInstance>(256);
        readonly List<GameObject> _mergedPiles = new List<GameObject>(32);
        readonly List<(Mesh, Matrix4x4)> _mergeParts = new List<(Mesh, Matrix4x4)>(32);
        System.Action<SoftClumpInstance> _returnToPool;

        public int IndividualSettledCount => _settled.Count;
        public int MergedClumpCount { get; private set; }
        public int MergedPileMeshes => _mergedPiles.Count;
        /// <summary>All clumps visibly on the floor (individual + merged).</summary>
        public int TotalFloorClumps => _settled.Count + MergedClumpCount;
        public float MaxPileHeight { get; private set; }

        public void Initialise(ChunkPhysicsConfig cfg, Material pileMaterial, System.Action<SoftClumpInstance> returnToPool)
        {
            _cfg = cfg;
            if (pileMaterial != null) _pileMaterial = pileMaterial;
            _returnToPool = returnToPool;
            _res = Mathf.Max(16, cfg.FloorGridResolution);
            _half = cfg.FloorHalfExtents;
            _height = new float[_res * _res];
        }

        public float SampleHeight(float x, float z)
        {
            if (_height == null) return 0f;
            float fx = (x + _half.x) / (_half.x * 2f) * (_res - 1);
            float fz = (z + _half.y) / (_half.y * 2f) * (_res - 1);
            if (fx < 0f || fz < 0f || fx > _res - 1 || fz > _res - 1) return 0f;
            int x0 = (int)fx, z0 = (int)fz;
            int x1 = Mathf.Min(x0 + 1, _res - 1), z1 = Mathf.Min(z0 + 1, _res - 1);
            float tx = fx - x0, tz = fz - z0;
            float a = Mathf.Lerp(_height[z0 * _res + x0], _height[z0 * _res + x1], tx);
            float b = Mathf.Lerp(_height[z1 * _res + x0], _height[z1 * _res + x1], tx);
            return Mathf.Lerp(a, b, tz);
        }

        /// <summary>Called once when a clump falls asleep: it becomes part of the pile.</summary>
        public void RegisterSettled(SoftClumpInstance clump)
        {
            Bounds b = clump.WorldBounds;
            Deposit(b.center, Mathf.Max(b.extents.x, b.extents.z) * 0.85f, b.max.y - _cfg.FloorY);
            _settled.Add(clump);
            if (_settled.Count > _cfg.FloorIndividualBudget) MergeOldest(_cfg.MergeBatchSize);
        }

        void Deposit(Vector3 centre, float radius, float topHeight)
        {
            float cellX = _half.x * 2f / (_res - 1), cellZ = _half.y * 2f / (_res - 1);
            int cx = Mathf.RoundToInt((centre.x + _half.x) / cellX);
            int cz = Mathf.RoundToInt((centre.z + _half.y) / cellZ);
            int rx = Mathf.CeilToInt(radius / cellX) + 1, rz = Mathf.CeilToInt(radius / cellZ) + 1;
            // pile height is a soft dome a bit lower than the clump top (wool compresses)
            float target = topHeight * 0.78f;
            for (int z = cz - rz; z <= cz + rz; z++)
            {
                if (z < 0 || z >= _res) continue;
                for (int x = cx - rx; x <= cx + rx; x++)
                {
                    if (x < 0 || x >= _res) continue;
                    float wx = x * cellX - _half.x, wz = z * cellZ - _half.y;
                    float d = Mathf.Sqrt((wx - centre.x) * (wx - centre.x) + (wz - centre.z) * (wz - centre.z)) / radius;
                    if (d >= 1f) continue;
                    float h = target * (1f - d * d);
                    int idx = z * _res + x;
                    if (h > _height[idx]) _height[idx] = h;
                    if (h > MaxPileHeight) MaxPileHeight = h;
                }
            }
        }

        void MergeOldest(int count)
        {
            count = Mathf.Min(count, _settled.Count);
            if (count <= 1) return;
            _mergeParts.Clear();
            for (int i = 0; i < count; i++) _mergeParts.Add((_settled[i].Mesh, Matrix4x4.identity));
            var mesh = ProceduralMeshFactory.Combine("FloorPile_" + _mergedPiles.Count, _mergeParts);
            var go = new GameObject("FloorPile_" + _mergedPiles.Count);
            go.transform.SetParent(transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = _pileMaterial != null ? _pileMaterial : _settled[0].Renderer.sharedMaterial;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            go.isStatic = false;
            _mergedPiles.Add(go);
            // Swap in the SAME frame: pile mesh visible, individual objects freed back to the pool.
            for (int i = 0; i < count; i++) _returnToPool?.Invoke(_settled[i]);
            _settled.RemoveRange(0, count);
            MergedClumpCount += count;
        }

        /// <summary>Only legal after the shearing scene is hidden / closed (spec §6.4).</summary>
        public void ClearFloorAfterSessionClosed()
        {
            for (int i = 0; i < _settled.Count; i++) _returnToPool?.Invoke(_settled[i]);
            _settled.Clear();
            foreach (var go in _mergedPiles)
            {
                if (go == null) continue;
                var mf = go.GetComponent<MeshFilter>();
                if (mf != null && mf.sharedMesh != null) Destroy(mf.sharedMesh);
                Destroy(go);
            }
            _mergedPiles.Clear();
            MergedClumpCount = 0;
            MaxPileHeight = 0f;
            if (_height != null) System.Array.Clear(_height, 0, _height.Length);
        }
    }
}
