using UnityEngine;

namespace ShearAndGrow.Processing
{
    /// <summary>
    /// Top-down heightfield of the wool heap (object space). Used for cheap analytic raycasts (no physics colliders)
    /// and for the area weight of every mask cell.
    /// </summary>
    public sealed class HeapHeightfield
    {
        public readonly int Resolution;
        public readonly Vector2 HalfSize;      // footprint half extents (x, z)
        public readonly float[] Height;        // object-space top height per cell, 0 = table
        public float MaxHeight { get; private set; }

        public HeapHeightfield(int resolution, Vector2 halfSize)
        {
            Resolution = resolution;
            HalfSize = halfSize;
            Height = new float[resolution * resolution];
        }

        public Vector2 ToUV(float x, float z) => new Vector2((x / HalfSize.x + 1f) * 0.5f, (z / HalfSize.y + 1f) * 0.5f);
        public Vector2 CellCentreXZ(int i, int j) => new Vector2(((i + 0.5f) / Resolution * 2f - 1f) * HalfSize.x, ((j + 0.5f) / Resolution * 2f - 1f) * HalfSize.y);

        /// <summary>Raises the field with the top cap of a sphere (puff).</summary>
        public void AddSphere(Vector3 c, float r)
        {
            int n = Resolution;
            float cellX = HalfSize.x * 2f / n, cellZ = HalfSize.y * 2f / n;
            int i0 = Mathf.Max(0, Mathf.FloorToInt((c.x - r + HalfSize.x) / cellX)), i1 = Mathf.Min(n - 1, Mathf.CeilToInt((c.x + r + HalfSize.x) / cellX));
            int j0 = Mathf.Max(0, Mathf.FloorToInt((c.z - r + HalfSize.y) / cellZ)), j1 = Mathf.Min(n - 1, Mathf.CeilToInt((c.z + r + HalfSize.y) / cellZ));
            for (int j = j0; j <= j1; j++)
                for (int i = i0; i <= i1; i++)
                {
                    Vector2 p = CellCentreXZ(i, j);
                    float dx = p.x - c.x, dz = p.y - c.z;
                    float d2 = dx * dx + dz * dz;
                    if (d2 >= r * r) continue;
                    float h = c.y + Mathf.Sqrt(r * r - d2);
                    int idx = j * n + i;
                    if (h > Height[idx]) Height[idx] = h;
                    if (h > MaxHeight) MaxHeight = h;
                }
        }

        public float Sample(float x, float z)
        {
            float fu = (x / HalfSize.x + 1f) * 0.5f * Resolution - 0.5f;
            float fv = (z / HalfSize.y + 1f) * 0.5f * Resolution - 0.5f;
            int i = Mathf.FloorToInt(fu), j = Mathf.FloorToInt(fv);
            float tx = fu - i, tz = fv - j;
            float a = At(i, j), b = At(i + 1, j), c = At(i, j + 1), d = At(i + 1, j + 1);
            return Mathf.Lerp(Mathf.Lerp(a, b, tx), Mathf.Lerp(c, d, tx), tz);
        }

        float At(int i, int j)
        {
            if (i < 0 || j < 0 || i >= Resolution || j >= Resolution) return 0f;
            return Height[j * Resolution + i];
        }

        public Vector3 Normal(float x, float z)
        {
            float e = HalfSize.x * 2f / Resolution;
            float hx = Sample(x + e, z) - Sample(x - e, z);
            float hz = Sample(x, z + e) - Sample(x, z - e);
            return new Vector3(-hx, 2f * e, -hz).normalized;
        }

        /// <summary>Object-space ray march against the heightfield. Only hits where wool exists (height &gt; epsilon).</summary>
        public bool Raycast(Vector3 origin, Vector3 dir, out Vector3 hit)
        {
            hit = default;
            // clip to the heap box
            var box = new Bounds(new Vector3(0f, MaxHeight * 0.5f + 0.005f, 0f), new Vector3(HalfSize.x * 2f, MaxHeight + 0.01f, HalfSize.y * 2f));
            if (!box.IntersectRay(new Ray(origin, dir), out float tEnter)) return false;
            float step = Mathf.Min(HalfSize.x, HalfSize.y) * 2f / Resolution * 0.7f;
            float t = Mathf.Max(0f, tEnter);
            float tMax = t + (box.size.magnitude + step * 2f);
            Vector3 prev = origin + dir * t;
            for (; t <= tMax; t += step)
            {
                Vector3 p = origin + dir * t;
                float h = Sample(p.x, p.z);
                if (h > 0.004f && p.y <= h)
                {
                    // refine between prev and p
                    Vector3 a = prev, b = p;
                    for (int k = 0; k < 6; k++)
                    {
                        Vector3 m = (a + b) * 0.5f;
                        if (m.y <= Sample(m.x, m.z)) b = m; else a = m;
                    }
                    hit = b;
                    return true;
                }
                prev = p;
                if (p.y < -0.01f) break;
            }
            return false;
        }
    }

    /// <summary>
    /// CPU-authoritative processing mask (cleanliness for washing, dryness for drying), one cell per heightfield
    /// cell. Progress = Σ weight of cells that reached <see cref="DoneLevel"/>; each cell is counted once, so passing
    /// over already processed wool adds nothing. Mirrored into an R8 texture (changed rows only). No allocations
    /// in <see cref="Stamp"/>.
    /// </summary>
    public sealed class HeapStateMask
    {
        public readonly int Resolution;
        readonly HeapHeightfield _field;
        readonly float[] _value;
        readonly float[] _weight;
        readonly bool[] _done;
        readonly byte[] _bytes;
        double _totalWeight;
        double _doneWeight;
        int _dirtyMin = int.MaxValue, _dirtyMax = -1;

        public float DoneLevel = 0.85f;
        public float Progress => _totalWeight <= 0 ? 0f : (float)(_doneWeight / _totalWeight);
        public byte[] Bytes => _bytes;
        public bool IsDirty => _dirtyMax >= _dirtyMin;
        public int DirtyRowMin => _dirtyMin;
        public int DirtyRowMax => _dirtyMax;

        public HeapStateMask(HeapHeightfield field)
        {
            _field = field;
            Resolution = field.Resolution;
            int n = Resolution * Resolution;
            _value = new float[n];
            _weight = new float[n];
            _done = new bool[n];
            _bytes = new byte[n];
            double total = 0;
            float cell = field.HalfSize.x * 2f / Resolution;
            for (int j = 0; j < Resolution; j++)
                for (int i = 0; i < Resolution; i++)
                {
                    int idx = j * Resolution + i;
                    if (field.Height[idx] <= 0.004f) continue;
                    Vector2 p = field.CellCentreXZ(i, j);
                    Vector3 nrm = field.Normal(p.x, p.y);
                    // visible surface area of the cell: steeper flanks hold more wool than their footprint
                    float w = 1f / Mathf.Max(0.35f, nrm.y);
                    _weight[idx] = w;
                    total += w;
                }
            _totalWeight = total;
        }

        /// <summary>
        /// Progress only counts wool the player can actually reach: cells facing away from the fixed camera or hidden
        /// behind the heap get weight 0 (they are still processed visually by brush spill and the completion settle).
        /// Returns the fraction of area kept.
        /// </summary>
        public float RestrictToVisible(Vector3 viewPointLocal)
        {
            double kept = 0, all = 0;
            float cell = _field.HalfSize.x * 2f / Resolution;
            for (int j = 0; j < Resolution; j++)
                for (int i = 0; i < Resolution; i++)
                {
                    int idx = j * Resolution + i;
                    float w = _weight[idx];
                    if (w <= 0f) continue;
                    all += w;
                    Vector2 xz = _field.CellCentreXZ(i, j);
                    Vector3 p = new Vector3(xz.x, _field.Height[idx], xz.y);
                    Vector3 toView = viewPointLocal - p;
                    Vector3 n = _field.Normal(xz.x, xz.y);
                    bool visible = Vector3.Dot(n, toView.normalized) > 0.08f;
                    if (visible && _field.Raycast(viewPointLocal, (p - viewPointLocal).normalized, out Vector3 hit))
                        visible = (hit - p).sqrMagnitude < (cell * 3f) * (cell * 3f);
                    if (!visible) { _weight[idx] = 0f; continue; }
                    kept += w;
                }
            _totalWeight = kept;
            return all > 0 ? (float)(kept / all) : 0f;
        }

        public void Reset(float value)
        {
            for (int k = 0; k < _value.Length; k++) { _value[k] = value; _done[k] = false; _bytes[k] = (byte)(value * 255f); }
            _doneWeight = 0;
            _dirtyMin = 0; _dirtyMax = Resolution - 1;
        }

        public float ValueAt(float x, float z)
        {
            Vector2 uv = _field.ToUV(x, z);
            int i = Mathf.Clamp((int)(uv.x * Resolution), 0, Resolution - 1);
            int j = Mathf.Clamp((int)(uv.y * Resolution), 0, Resolution - 1);
            return _value[j * Resolution + i];
        }

        /// <summary>
        /// Applies one brush stamp centred at object-space (x, z). Returns the processed amount (weight × value
        /// increase, i.e. real work done on unfinished wool) and adds newly finished weight to <paramref name="newlyDone"/>.
        /// </summary>
        public float Stamp(float x, float z, float radius, float dose, ref float newlyDone)
        {
            Vector2 uv = _field.ToUV(x, z);
            float cell = _field.HalfSize.x * 2f / Resolution;
            int ci = (int)(uv.x * Resolution), cj = (int)(uv.y * Resolution);
            int rc = Mathf.CeilToInt(radius / cell) + 1;
            int i0 = Mathf.Max(0, ci - rc), i1 = Mathf.Min(Resolution - 1, ci + rc);
            int j0 = Mathf.Max(0, cj - rc), j1 = Mathf.Min(Resolution - 1, cj + rc);
            float r2 = radius * radius;
            float work = 0f;
            for (int j = j0; j <= j1; j++)
            {
                bool rowChanged = false;
                for (int i = i0; i <= i1; i++)
                {
                    int idx = j * Resolution + i;
                    float w = _weight[idx];
                    if (w <= 0f) continue;
                    float v = _value[idx];
                    if (v >= 1f) continue;
                    Vector2 p = _field.CellCentreXZ(i, j);
                    float dx = p.x - x, dz = p.y - z;
                    float d2 = dx * dx + dz * dz;
                    if (d2 >= r2) continue;
                    float f = 1f - d2 / r2;
                    f *= f;
                    float nv = Mathf.Min(1f, v + dose * f);
                    _value[idx] = nv;
                    work += (nv - v) * w;
                    byte b = (byte)(nv * 255f);
                    if (b != _bytes[idx]) { _bytes[idx] = b; rowChanged = true; }
                    if (!_done[idx] && nv >= DoneLevel)
                    {
                        _done[idx] = true;
                        _doneWeight += w;
                        newlyDone += w / (float)_totalWeight;
                    }
                }
                if (rowChanged)
                {
                    if (j < _dirtyMin) _dirtyMin = j;
                    if (j > _dirtyMax) _dirtyMax = j;
                }
            }
            return (float)(work / _totalWeight);
        }

        /// <summary>Completion: move every unfinished cell toward fully processed (visual settle). Returns true when done.</summary>
        public bool Settle(float amount)
        {
            bool all = true;
            for (int k = 0; k < _value.Length; k++)
            {
                if (_weight[k] <= 0f) { if (_value[k] < 1f) { _value[k] = 1f; _bytes[k] = 255; } continue; }
                float v = _value[k];
                if (v >= 1f) continue;
                v = Mathf.Min(1f, v + amount);
                _value[k] = v;
                _bytes[k] = (byte)(v * 255f);
                if (v < 1f) all = false;
                if (!_done[k] && v >= DoneLevel) { _done[k] = true; _doneWeight += _weight[k]; }
            }
            _dirtyMin = 0; _dirtyMax = Resolution - 1;
            return all;
        }

        public void ClearDirty() { _dirtyMin = int.MaxValue; _dirtyMax = -1; }
    }
}
