using System;
using UnityEngine;

namespace ShearAndGrow.Shearing
{
    /// <summary>
    /// Single source of truth for where wool still exists (spec §5.3). Pure C#, no Unity objects,
    /// no allocations in <see cref="Stamp"/>. Texels are evaluated by exact 3D distance on the
    /// analytic body surface, which makes stamps seam-safe and resolution-independent.
    /// </summary>
    public sealed class ShearMaskAuthority
    {
        /// <summary>Occupancy holds the WOOL LENGTH per texel: 255 = full fleece, 0 = bare, between = regrowing.</summary>
        public const byte Full = 255;

        public readonly int Width;
        public readonly int Height;
        public readonly byte[] Occupancy;
        readonly Vector3[] _texelPos;
        readonly float[] _texelWeight;      // progress weight (0 for unshearable texels)
        readonly float[] _rowX;
        readonly float[] _rowMinRadius;
        readonly double _totalWeight;
        readonly SheepBodySurface _surface;
        double _cutWeight;

        public int DirtyRowMin { get; private set; } = int.MaxValue;
        public int DirtyRowMax { get; private set; } = -1;
        public bool IsDirty => DirtyRowMax >= DirtyRowMin;

        /// <summary>Missing wool (0..1 of a full fleece), length-weighted. 1 − <see cref="WoolAmount"/>.</summary>
        public float CutFraction => _totalWeight <= 0 ? 0f : (float)(_cutWeight / _totalWeight);

        public ShearMaskAuthority(SheepBodySurface surface, int width, int height)
        {
            Width = width;
            Height = height;
            Occupancy = new byte[width * height];
            _texelPos = new Vector3[width * height];
            _texelWeight = new float[width * height];
            _rowX = new float[height];
            _rowMinRadius = new float[height];

            float du = 1f / width, dv = 1f / height;
            double total = 0;
            for (int j = 0; j < height; j++)
            {
                float v = (j + 0.5f) / height;
                float minR = float.MaxValue;
                for (int i = 0; i < width; i++)
                {
                    float u = (i + 0.5f) / width;
                    int idx = j * width + i;
                    Vector3 p = surface.GetPoint(u, v);
                    _texelPos[idx] = p;
                    float r = Mathf.Sqrt(p.y * p.y + p.z * p.z);
                    if (r < minR) minR = r;
                    Vector3 pu = surface.GetPoint(u + du * 0.5f, v) - surface.GetPoint(u - du * 0.5f, v);
                    Vector3 pv = surface.GetPoint(u, v + dv * 0.5f) - surface.GetPoint(u, v - dv * 0.5f);
                    float area = Vector3.Cross(pu, pv).magnitude;
                    float w = surface.IsShearable(u, v) ? area : 0f;
                    _texelWeight[idx] = w;
                    total += w;
                    Occupancy[idx] = Full;
                }
                _rowX[j] = _texelPos[j * width].x;
                _rowMinRadius[j] = minR;
            }
            _totalWeight = total;
        }

        public void ResetToFull()
        {
            for (int i = 0; i < Occupancy.Length; i++) Occupancy[i] = Full;
            _cutWeight = 0;
            MarkAllDirty();
        }

        public void MarkAllDirty()
        {
            DirtyRowMin = 0;
            DirtyRowMax = Height - 1;
        }

        public void ClearDirty()
        {
            DirtyRowMin = int.MaxValue;
            DirtyRowMax = -1;
        }

        /// <summary>
        /// Removes wool within <paramref name="radius"/> of <paramref name="centreLocal"/> (body object space).
        /// Returns the NEW unique area removed (normalized to total shearable area). Re-stamping bare
        /// texels returns 0, so repeated passes never produce progress, chunks or feedback.
        /// </summary>
        public float Stamp(Vector3 centreLocal, float radius, out float localDensityBefore)
        {
            float r2 = radius * radius;
            double removed = 0;
            int touched = 0, woolly = 0;

            int jMin = -1, jMax = -1;
            for (int j = 0; j < Height; j++)
            {
                if (Mathf.Abs(_rowX[j] - centreLocal.x) <= radius)
                {
                    if (jMin < 0) jMin = j;
                    jMax = j;
                }
            }
            if (jMin < 0) { localDensityBefore = 0f; return 0f; }

            float centreAngle = Mathf.Atan2(centreLocal.z, centreLocal.y); // matches u = phi/2π with y=cos, z=sin
            if (centreAngle < 0f) centreAngle += Mathf.PI * 2f;
            float centreU = centreAngle / (Mathf.PI * 2f);

            for (int j = jMin; j <= jMax; j++)
            {
                float rowR = _rowMinRadius[j];
                int iStart, iCount;
                if (rowR <= radius * 1.2f)
                {
                    iStart = 0; iCount = Width;
                }
                else
                {
                    float halfAngle = Mathf.Asin(Mathf.Clamp01(radius / rowR)) * 1.35f + 0.5f * Mathf.PI * 2f / Width;
                    int half = Mathf.CeilToInt(halfAngle / (Mathf.PI * 2f) * Width) + 1;
                    if (half * 2 + 1 >= Width) { iStart = 0; iCount = Width; }
                    else
                    {
                        int ic = Mathf.FloorToInt(centreU * Width);
                        iStart = ic - half;
                        iCount = half * 2 + 1;
                    }
                }
                int rowOffset = j * Width;
                bool rowChanged = false;
                for (int k = 0; k < iCount; k++)
                {
                    int i = iStart + k;
                    if (i < 0) i += Width; else if (i >= Width) i -= Width;
                    int idx = rowOffset + i;
                    Vector3 d = _texelPos[idx] - centreLocal;
                    if (d.sqrMagnitude > r2) continue;
                    touched++;
                    byte len = Occupancy[idx];
                    if (len == 0) continue;
                    woolly++;
                    Occupancy[idx] = 0;
                    // removed VOLUME: a partly regrown texel only yields its current length
                    removed += _texelWeight[idx] * (len / 255.0);
                    rowChanged = true;
                }
                if (rowChanged)
                {
                    if (j < DirtyRowMin) DirtyRowMin = j;
                    if (j > DirtyRowMax) DirtyRowMax = j;
                }
            }
            localDensityBefore = touched > 0 ? woolly / (float)touched : 0f;
            _cutWeight += removed;
            return _totalWeight <= 0 ? 0f : (float)(removed / _totalWeight);
        }

        /// <summary>Bilinear-free occupancy lookup at a (u,v) parameter (0..1).</summary>
        public float SampleUV(float u, float v)
        {
            u -= Mathf.Floor(u);
            int i = Mathf.Clamp((int)(u * Width), 0, Width - 1);
            int j = Mathf.Clamp((int)(Mathf.Clamp01(v) * Height), 0, Height - 1);
            return Occupancy[j * Width + i] / 255f;
        }

        public int TexelIndex(float u, float v)
        {
            u -= Mathf.Floor(u);
            int i = Mathf.Clamp((int)(u * Width), 0, Width - 1);
            int j = Mathf.Clamp((int)(Mathf.Clamp01(v) * Height), 0, Height - 1);
            return j * Width + i;
        }

        public Vector3 TexelPosition(int index) => _texelPos[index];

        public bool HasWool(int index) => Occupancy[index] != 0;

        // ---------------------------------------------------------------- persistence

        /// <summary>Run-length encodes the occupancy (pairs of value, count&lt;=255) and base64s it.</summary>
        public string Encode()
        {
            var buffer = new System.Collections.Generic.List<byte>(4096);
            int n = Occupancy.Length;
            int p = 0;
            while (p < n)
            {
                byte val = Occupancy[p];
                int run = 1;
                while (p + run < n && run < 255 && Occupancy[p + run] == val) run++;
                buffer.Add(val);
                buffer.Add((byte)run);
                p += run;
            }
            return Convert.ToBase64String(buffer.ToArray());
        }

        public bool Decode(string encoded)
        {
            if (string.IsNullOrEmpty(encoded)) return false;
            byte[] data;
            try { data = Convert.FromBase64String(encoded); }
            catch (FormatException) { return false; }
            int p = 0;
            for (int k = 0; k + 1 < data.Length; k += 2)
            {
                byte val = data[k];
                int run = data[k + 1];
                if (p + run > Occupancy.Length) return false;
                for (int r = 0; r < run; r++) Occupancy[p++] = val;
            }
            if (p != Occupancy.Length) return false;
            RecomputeCutWeight();
            MarkAllDirty();
            return true;
        }

        /// <summary>Recomputes the missing wool volume Σ weight × (1 − length/255).</summary>
        public void RecomputeCutWeight()
        {
            double cut = 0;
            for (int i = 0; i < Occupancy.Length; i++)
            {
                byte len = Occupancy[i];
                if (len != Full) cut += _texelWeight[i] * ((Full - len) / 255.0);
            }
            _cutWeight = cut;
        }

        /// <summary>Wool currently on the sheep, 0..1 of a full fleece (length-weighted).</summary>
        public float WoolAmount => 1f - CutFraction;

        /// <summary>
        /// Regional regrowth: every texel shorter than full grows by <paramref name="steps"/> length steps (each texel
        /// independently, so patches cut at different times keep independent progress). Untouched wool is unchanged.
        /// Returns true if anything changed. No allocations.
        /// </summary>
        public bool ApplyGrowth(int steps)
        {
            if (steps <= 0) return false;
            bool any = false;
            double regained = 0;
            for (int j = 0; j < Height; j++)
            {
                int row = j * Width;
                bool rowChanged = false;
                for (int i = 0; i < Width; i++)
                {
                    int idx = row + i;
                    byte len = Occupancy[idx];
                    if (len == Full) continue;
                    int n = len + steps;
                    byte nb = n >= Full ? Full : (byte)n;
                    Occupancy[idx] = nb;
                    regained += _texelWeight[idx] * ((nb - len) / 255.0);
                    rowChanged = true;
                }
                if (rowChanged)
                {
                    any = true;
                    if (j < DirtyRowMin) DirtyRowMin = j;
                    if (j > DirtyRowMax) DirtyRowMax = j;
                }
            }
            _cutWeight = System.Math.Max(0.0, _cutWeight - regained);
            return any;
        }

        /// <summary>True if any texel is shorter than full (growth would change something).</summary>
        public bool HasMissingWool => _cutWeight > 1e-9;

        /// <summary>Copies raw lengths in (same layout), e.g. from a farm-side cache.</summary>
        public void CopyFrom(byte[] lengths)
        {
            System.Array.Copy(lengths, Occupancy, Occupancy.Length);
            RecomputeCutWeight();
            MarkAllDirty();
        }
    }
}
