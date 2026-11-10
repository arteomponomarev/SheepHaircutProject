using ShearAndGrow.Shearing;
using UnityEngine;

namespace ShearAndGrow.Processing
{
    /// <summary>
    /// Straws and dust flecks stuck in the dirty wool. Created once (never destroyed during play). When the wool under
    /// a piece gets clean enough it slides downhill along the heap, tumbles over the edge and shrinks away.
    /// </summary>
    public sealed class WashDebris : MonoBehaviour
    {
        [SerializeField] private Material _strawMaterial;
        [SerializeField] private Material _dustMaterial;
        [SerializeField] private int _straws = 18;
        [SerializeField] private int _flecks = 12;
        [Tooltip("Cleanliness under a piece at which it washes off.")]
        [SerializeField] private float _releaseAt = 0.45f;

        struct Piece
        {
            public Transform T;
            public Vector3 Local;      // heap object space
            public Vector3 Vel;
            public Vector3 Spin;
            public float T0;
            public int State;          // 0 stuck, 1 sliding, 2 gone
            public float Scale;
            public bool IsStraw;
        }

        Piece[] _pieces;
        HeapHeightfield _field;
        HeapStateMask _mask;
        Transform _heap;
        Mesh _strawMesh, _fleckMesh;

        public int Remaining { get; private set; }
        public int WashedOff { get; private set; }

        public void Build(Transform heap, HeapHeightfield field, HeapStateMask mask, int seed)
        {
            _heap = heap; _field = field; _mask = mask;
            var rng = new System.Random(seed);
            float R(float a, float b) => a + (float)rng.NextDouble() * (b - a);
            _strawMesh = ProceduralMeshFactory.Cylinder("Straw", 0.0028f, 0.0022f, 1f, 6, true);
            _fleckMesh = ProceduralMeshFactory.Ellipsoid("DustFleck", new Vector3(0.006f, 0.0025f, 0.005f), 8, 5);
            int total = _straws + _flecks;
            _pieces = new Piece[total];
            for (int k = 0; k < total; k++)
            {
                bool straw = k < _straws;
                Vector3 p = Vector3.zero;
                for (int tries = 0; tries < 30; tries++)
                {
                    float x = R(-field.HalfSize.x, field.HalfSize.x) * 0.8f, z = R(-field.HalfSize.y, field.HalfSize.y) * 0.8f;
                    float h = field.Sample(x, z);
                    if (h > field.MaxHeight * 0.25f) { p = new Vector3(x, h, z); break; }
                }
                var go = new GameObject(straw ? "Straw" : "Dust", typeof(MeshFilter), typeof(MeshRenderer));
                go.transform.SetParent(transform, false);
                go.GetComponent<MeshFilter>().sharedMesh = straw ? _strawMesh : _fleckMesh;
                var mr = go.GetComponent<MeshRenderer>();
                mr.sharedMaterial = straw ? _strawMaterial : _dustMaterial;
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                Vector3 n = field.Normal(p.x, p.z);
                Vector3 tangent = Vector3.Cross(n, new Vector3(R(-1, 1), 0f, R(-1, 1)).normalized).normalized;
                float scale = straw ? R(0.045f, 0.08f) : R(0.8f, 1.4f);
                go.transform.position = heap.TransformPoint(p - n * (straw ? 0.002f : 0.001f));
                go.transform.rotation = heap.rotation * (straw ? Quaternion.FromToRotation(Vector3.up, tangent) : Quaternion.LookRotation(tangent, n));
                go.transform.localScale = straw ? new Vector3(1f, scale, 1f) : Vector3.one * scale;
                if (straw) go.transform.position -= go.transform.up * scale * 0.5f; // centre the straw on the point
                _pieces[k] = new Piece { T = go.transform, Local = p, Scale = scale, State = 0, IsStraw = straw };
            }
            Remaining = total;
        }

        public void ResetAll()
        {
            // not needed in the MVP flow (scene reload), kept for completeness
        }

        void Update()
        {
            if (_pieces == null) return;
            float dt = Time.deltaTime;
            for (int k = 0; k < _pieces.Length; k++)
            {
                ref Piece p = ref _pieces[k];
                if (p.State == 2) continue;
                if (p.State == 0)
                {
                    if (_mask.ValueAt(p.Local.x, p.Local.z) < _releaseAt) continue;
                    p.State = 1;
                    p.T0 = Time.time;
                    Vector3 n = _field.Normal(p.Local.x, p.Local.z);
                    Vector3 downhill = new Vector3(n.x, 0f, n.z);
                    if (downhill.sqrMagnitude < 1e-4f) downhill = new Vector3(p.Local.x, 0f, p.Local.z);
                    p.Vel = _heap.TransformDirection(downhill.normalized * 0.35f + Vector3.up * 0.15f);
                    p.Spin = new Vector3(Random.Range(-360f, 360f), Random.Range(-200f, 200f), Random.Range(-360f, 360f));
                    WashedOff++;
                    Remaining--;
                }
                float age = Time.time - p.T0;
                p.Vel += Physics.gravity * dt * 0.6f;
                p.T.position += p.Vel * dt;
                p.T.Rotate(p.Spin * dt, Space.World);
                float s = Mathf.Clamp01(1f - age / 0.6f);
                p.T.localScale = p.IsStraw
                    ? new Vector3(s, p.Scale * Mathf.Max(0.001f, s), s)
                    : Vector3.one * p.Scale * Mathf.Max(0.001f, s);
                if (age >= 0.6f) { p.State = 2; p.T.gameObject.SetActive(false); }
            }
        }

        void OnDestroy()
        {
            if (_strawMesh != null) Destroy(_strawMesh);
            if (_fleckMesh != null) Destroy(_fleckMesh);
        }
    }
}
