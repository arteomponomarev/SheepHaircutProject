using System.Collections.Generic;
using ShearAndGrow.Shearing;
using UnityEngine;

namespace ShearAndGrow.Farm
{
    /// <summary>
    /// Farm sheep. The woolly body uses the SAME parametrisation as the shearing sheep (scaled to farm size): an
    /// undercoat plus a light LOD puff coat whose puffs shrink with the per-texel wool length of this sheep's
    /// authoritative mask — partial shearing, regrowth and preserved hairstyles look on the farm exactly as cut.
    /// Head, legs and the idle/pen-wander animation are cheap procedural parts (no physics, no NavMesh).
    /// </summary>
    public sealed class FarmSheepView : MonoBehaviour
    {
        public const int MaskW = 512, MaskH = 256;
        /// <summary>Farm sheep = shearing sheep body × this scale.</summary>
        public const float BodyScale = 0.5f;

        [Header("Materials")]
        [SerializeField] private Material _skin;
        [Tooltip("Puff wool material (ShearAndGrow/WoolShell). Instanced per sheep with its own mask.")]
        [SerializeField] private Material _wool;
        [SerializeField] private Material _dark;
        [SerializeField] private Material _undercoat;
        [Header("Wander (pen rectangle, world XZ)")]
        [SerializeField] private Rect _pen = new Rect(-2f, -2f, 4f, 4f);
        [SerializeField] private float _speed = 0.35f;
        [SerializeField] private Vector2 _pause = new Vector2(1.5f, 4.5f);
        [SerializeField] private float _scale = 0.9f;
        [SerializeField] private int _seed = 1;

        Transform _body, _head;
        MeshRenderer _coatR;
        Material _coatMat;
        Texture2D _maskTex;
        readonly List<Mesh> _meshes = new List<Mesh>();
        Vector3 _target;
        float _waitUntil;
        float _phase;
        System.Random _rng;
        float _remaining = 1f;

        static Mesh _sharedCoat, _sharedUndercoat;
        static readonly SheepBodySurface Surface = new SheepBodySurface();

        public float RemainingWool => _remaining;
        public bool IsWalking { get; private set; }
        public int MaskUploads { get; private set; }
        public Texture2D MaskTexture => _maskTex;

        public void SetPen(Rect pen, int seed) { _pen = pen; _seed = seed; }

        /// <summary>The barn (and its pen) moved: teleport into the new pen, keeping the relative offset.</summary>
        public void MoveToPen(Rect pen)
        {
            Vector2 rel = _pen.width > 0f && _pen.height > 0f
                ? new Vector2((transform.position.x - _pen.xMin) / _pen.width, (transform.position.z - _pen.yMin) / _pen.height)
                : new Vector2(0.5f, 0.5f);
            rel = new Vector2(Mathf.Clamp(rel.x, 0.1f, 0.9f), Mathf.Clamp(rel.y, 0.1f, 0.9f));
            _pen = pen;
            transform.position = new Vector3(pen.xMin + rel.x * pen.width, transform.position.y, pen.yMin + rel.y * pen.height);
            _target = transform.position;
        }

        public void Build()
        {
            if (_body != null) return;
            _rng = new System.Random(_seed);
            _phase = (float)_rng.NextDouble() * 10f;
            var root = new GameObject("Model").transform;
            root.SetParent(transform, false);
            root.localScale = Vector3.one * _scale;
            _body = new GameObject("Body").transform;
            _body.SetParent(root, false);
            _body.localPosition = new Vector3(0f, 0.5f, 0f);

            // woolly body: undercoat + LOD coat (shared meshes), per-sheep material instance with its own mask
            var bodyRoot = new GameObject("WoolBody").transform;
            bodyRoot.SetParent(_body, false);
            bodyRoot.localScale = Vector3.one * BodyScale;
            var under = PartRenderer("Undercoat", SharedUndercoat(), _undercoat != null ? _undercoat : _skin, bodyRoot);
            under.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On; // grounds a sheared sheep
            _coatR = PartRenderer("Coat", SharedCoat(), null, bodyRoot);
            _coatMat = new Material(_wool) { name = _wool.name + " (Farm " + _seed + ")" };
            _maskTex = new Texture2D(MaskW, MaskH, TextureFormat.R8, false, true)
            {
                name = "FarmSheepMask", wrapModeU = TextureWrapMode.Repeat, wrapModeV = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear,
            };
            var px = _maskTex.GetPixelData<byte>(0);
            for (int i = 0; i < px.Length; i++) px[i] = 255;
            _maskTex.Apply(false, false);
            _coatMat.SetTexture("_Mask", _maskTex);
            _coatR.sharedMaterial = _coatMat;
            _coatR.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;

            // head & legs (body surface: long axis X, head at +X; radii 0.72/0.5 × BodyScale)
            _head = new GameObject("Head").transform;
            _head.SetParent(_body, false);
            _head.localPosition = new Vector3(0.40f, 0.12f, 0f);
            Part("Skull", ProceduralMeshFactory.Ellipsoid("FarmSheepHead", new Vector3(0.17f, 0.15f, 0.14f), 12, 8), _skin, _head, Vector3.zero, Quaternion.identity);
            Part("EarL", ProceduralMeshFactory.Ellipsoid("FarmSheepEar", new Vector3(0.09f, 0.03f, 0.05f), 8, 5), _skin, _head, new Vector3(-0.02f, 0.06f, 0.15f), Quaternion.Euler(-30f, 0f, 0f));
            Part("EarR", ProceduralMeshFactory.Ellipsoid("FarmSheepEar", new Vector3(0.09f, 0.03f, 0.05f), 8, 5), _skin, _head, new Vector3(-0.02f, 0.06f, -0.15f), Quaternion.Euler(30f, 0f, 0f));
            Part("EyeL", ProceduralMeshFactory.Ellipsoid("FarmSheepEye", Vector3.one * 0.028f, 6, 4), _dark, _head, new Vector3(0.12f, 0.04f, 0.075f), Quaternion.identity);
            Part("EyeR", ProceduralMeshFactory.Ellipsoid("FarmSheepEye", Vector3.one * 0.028f, 6, 4), _dark, _head, new Vector3(0.12f, 0.04f, -0.075f), Quaternion.identity);
            var tuft = WoolPuffMeshBuilder.BuildCluster("FarmSheepTuft", 5, 0.06f, 0.05f, 0.2f, new Vector3(1.1f, 0.6f, 1.2f), _seed * 7 + 3, true);
            Part("Tuft", tuft, _wool, _head, new Vector3(-0.02f, 0.13f, 0f), Quaternion.identity);
            var leg = ProceduralMeshFactory.Cylinder("FarmSheepLeg", 0.045f, 0.04f, 0.34f, 8);
            foreach (var p in new[] { new Vector3(0.2f, 0f, 0.12f), new Vector3(0.2f, 0f, -0.12f), new Vector3(-0.2f, 0f, 0.12f), new Vector3(-0.2f, 0f, -0.12f) })
                Part("Leg", leg, _dark, root, p, Quaternion.identity);

            transform.position = RandomPenPoint();
            _target = transform.position;
            _waitUntil = Time.time + Range(_pause);
        }

        void Part(string name, Mesh mesh, Material mat, Transform parent, Vector3 pos, Quaternion rot)
        {
            if (!_meshes.Contains(mesh)) _meshes.Add(mesh);
            var go = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localRotation = rot;
            go.GetComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.GetComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        MeshRenderer PartRenderer(string name, Mesh mesh, Material mat, Transform parent)
        {
            var go = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
            go.transform.SetParent(parent, false);
            go.GetComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.GetComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            return r;
        }

        /// <summary>One LOD coat shared by all farm sheep (same body parametrisation; only the mask differs).</summary>
        static Mesh SharedCoat()
        {
            if (_sharedCoat == null)
                _sharedCoat = WoolPuffMeshBuilder.BuildCoat(Surface, 0.2f, 130, 0.13f, 0.22f, 7, 5, "FarmSheepCoat_LOD");
            return _sharedCoat;
        }

        static Mesh SharedUndercoat()
        {
            if (_sharedUndercoat == null)
            {
                var s = new SheepBodySurface { SegmentsU = 40, SegmentsV = 22 };
                _sharedUndercoat = s.BuildSurfaceMesh("FarmSheepUndercoat", 0f);
            }
            return _sharedUndercoat;
        }

        /// <summary>
        /// Shows the sheep's real fleece: copies the authoritative per-texel wool lengths (changed rows only, or all
        /// with <paramref name="force"/>) into this sheep's mask texture. No readback, no allocations.
        /// </summary>
        public void SetMask(ShearMaskAuthority mask, bool force)
        {
            if (_maskTex == null || mask == null) return;
            if (!force && !mask.IsDirty) return;
            var data = _maskTex.GetPixelData<byte>(0);
            int r0 = force ? 0 : mask.DirtyRowMin, r1 = force ? mask.Height - 1 : mask.DirtyRowMax;
            Unity.Collections.NativeArray<byte>.Copy(mask.Occupancy, r0 * mask.Width, data, r0 * mask.Width, (r1 - r0 + 1) * mask.Width);
            _maskTex.Apply(false, false);
            mask.ClearDirty();
            _remaining = mask.WoolAmount;
            MaskUploads++;
        }

        /// <summary>For sheep without a mask (no save yet = full wool).</summary>
        public void SetRemainingWool(float remaining) => _remaining = Mathf.Clamp01(remaining);

        float Range(Vector2 r) => r.x + (float)_rng.NextDouble() * (r.y - r.x);
        Vector3 RandomPenPoint() => new Vector3(_pen.xMin + (float)_rng.NextDouble() * _pen.width, 0f, _pen.yMin + (float)_rng.NextDouble() * _pen.height);

        float _eatUntil;

        /// <summary>Feeding feedback: stands still and nibbles for a few seconds (no pathfinding).</summary>
        public void PlayEat(float seconds) { _eatUntil = Time.time + seconds; _target = transform.position; }
        public bool IsEating => Time.time < _eatUntil;

        void Update()
        {
            if (_body == null) return;
            float t = Time.time + _phase;
            float dt = Time.deltaTime;
            if (Time.time < _eatUntil)
            {
                IsWalking = false;
                _body.localPosition = new Vector3(0f, 0.5f + Mathf.Abs(Mathf.Sin(t * 6f)) * 0.012f, 0f);
                _head.localRotation = Quaternion.Euler(0f, Mathf.Sin(t * 3f) * 6f, -32f + Mathf.Sin(t * 14f) * 6f);
                return;
            }
            Vector3 pos = transform.position;
            Vector3 to = _target - pos; to.y = 0f;
            IsWalking = to.sqrMagnitude > 0.0025f && Time.time >= _waitUntil;
            if (IsWalking)
            {
                Vector3 step = to.normalized * _speed * dt;
                if (step.sqrMagnitude > to.sqrMagnitude) step = to;
                transform.position = pos + step;
                Quaternion face = Quaternion.LookRotation(to.normalized, Vector3.up) * Quaternion.Euler(0f, -90f, 0f); // model's head is +X
                transform.rotation = Quaternion.Slerp(transform.rotation, face, 1f - Mathf.Exp(-5f * dt));
                _body.localPosition = new Vector3(0f, 0.5f + Mathf.Abs(Mathf.Sin(t * 9f)) * 0.025f, 0f);
                _head.localRotation = Quaternion.Euler(0f, 0f, Mathf.Sin(t * 9f) * 3f);
            }
            else
            {
                if (to.sqrMagnitude <= 0.0025f && Time.time >= _waitUntil)
                {
                    _target = RandomPenPoint();
                    _waitUntil = Time.time + Range(_pause);
                }
                _body.localPosition = new Vector3(0f, 0.5f + Mathf.Sin(t * 2.1f) * 0.008f, 0f);
                float graze = Mathf.Clamp01(Mathf.Sin(t * 0.7f) * 2f - 1f);
                _head.localRotation = Quaternion.Euler(0f, Mathf.Sin(t * 0.9f) * 12f, -graze * 28f);
            }
        }

        void OnDestroy()
        {
            foreach (var m in _meshes) if (m != null) Destroy(m);
            if (_coatMat != null) Destroy(_coatMat);
            if (_maskTex != null) Destroy(_maskTex);
        }
    }
}
