using Unity.Collections;
using UnityEngine;

namespace ShearAndGrow.Processing
{
    /// <summary>
    /// Visual representation of a wool heap: builds the procedural puff mesh, owns its material instance and the R8
    /// state-mask texture (CPU authority mirror, changed rows only, never read back) and drives tool uniforms.
    /// Pure presentation – no economy, no input.
    /// </summary>
    public sealed class WoolVisualStateController : MonoBehaviour
    {
        static readonly int MaskId = Shader.PropertyToID("_StateMask");
        static readonly int FootprintId = Shader.PropertyToID("_Footprint");
        static readonly int MaskIsCleanId = Shader.PropertyToID("_MaskIsClean");
        static readonly int MaskIsDryId = Shader.PropertyToID("_MaskIsDry");
        static readonly int CleanBaseId = Shader.PropertyToID("_CleanBase");
        static readonly int DryBaseId = Shader.PropertyToID("_DryBase");
        static readonly int ToolPosId = Shader.PropertyToID("_ToolPos");
        static readonly int ToolDirId = Shader.PropertyToID("_ToolDir");
        static readonly int AirPushId = Shader.PropertyToID("_AirPush");
        static readonly int WaterPushId = Shader.PropertyToID("_WaterPush");

        [SerializeField] private Material _material;
        [SerializeField] private bool _castShadows = true;

        Material _instance;
        Texture2D _maskTex;
        MeshFilter _filter;
        MeshRenderer _renderer;
        float _toolActive;

        public HeapHeightfield Field { get; private set; }
        public HeapStateMask Mask { get; private set; }
        public Mesh Mesh => _filter != null ? _filter.sharedMesh : null;
        public int PuffCount { get; private set; }
        public Material MaterialInstance => _instance;

        /// <summary>Builds the heap. <paramref name="stage"/> selects which channel the mask drives.</summary>
        public void Build(ProcessingConfig cfg, ProcessingStage stage, int puffCount, int segU, int segV)
        {
            var res = WoolHeapBuilder.Build("WoolHeap_" + stage, cfg.HeapHalfSize, cfg.HeapHeight, puffCount, cfg.PuffRadius, cfg.Seed, segU, segV, 112);
            Field = res.Field;
            PuffCount = res.PuffCount;
            Mask = new HeapStateMask(Field) { DoneLevel = cfg.CellDoneLevel };
            Mask.Reset(0f);

            _filter = GetComponent<MeshFilter>();
            if (_filter == null) _filter = gameObject.AddComponent<MeshFilter>();
            _renderer = GetComponent<MeshRenderer>();
            if (_renderer == null) _renderer = gameObject.AddComponent<MeshRenderer>();
            _filter.sharedMesh = res.Mesh;
            _instance = new Material(_material) { name = _material.name + " (Instance)" };
            _renderer.sharedMaterial = _instance;
            _renderer.shadowCastingMode = _castShadows ? UnityEngine.Rendering.ShadowCastingMode.On : UnityEngine.Rendering.ShadowCastingMode.Off;
            _renderer.receiveShadows = true;

            _maskTex = new Texture2D(Field.Resolution, Field.Resolution, TextureFormat.R8, false, true)
            {
                name = "HeapStateMask", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, anisoLevel = 0,
            };
            _instance.SetTexture(MaskId, _maskTex);
            _instance.SetVector(FootprintId, new Vector4(cfg.HeapHalfSize.x, cfg.HeapHalfSize.y, 0f, 0f));
            _instance.SetColor("_DirtyColor", cfg.DirtyColor);
            _instance.SetColor("_DirtSpotColor", cfg.DirtSpotColor);
            _instance.SetColor("_WetColor", cfg.WetColor);
            _instance.SetColor("_DryColor", cfg.DryColor);
            SetStage(stage);
            Upload(force: true);
        }

        /// <summary>Wash: mask = cleanliness (dryness 0). Dry: mask = dryness (fully clean).</summary>
        public void SetStage(ProcessingStage stage)
        {
            bool wash = stage == ProcessingStage.Wash;
            _instance.SetFloat(MaskIsCleanId, wash ? 1f : 0f);
            _instance.SetFloat(MaskIsDryId, wash ? 0f : 1f);
            _instance.SetFloat(CleanBaseId, 1f);
            _instance.SetFloat(DryBaseId, 0f);
        }

        /// <summary>Static showcase (Hub): fixed cleanliness/dryness, no mask.</summary>
        public void SetStatic(float clean, float dry)
        {
            _instance.SetFloat(MaskIsCleanId, 0f);
            _instance.SetFloat(MaskIsDryId, 0f);
            _instance.SetFloat(CleanBaseId, clean);
            _instance.SetFloat(DryBaseId, dry);
        }

        /// <summary>Copies changed mask rows into the GPU texture (no readback, no allocations).</summary>
        public void Upload(bool force = false)
        {
            if (Mask == null || _maskTex == null) return;
            if (!force && !Mask.IsDirty) return;
            NativeArray<byte> data = _maskTex.GetPixelData<byte>(0);
            int n = Field.Resolution;
            int r0 = force ? 0 : Mask.DirtyRowMin, r1 = force ? n - 1 : Mask.DirtyRowMax;
            NativeArray<byte>.Copy(Mask.Bytes, r0 * n, data, r0 * n, (r1 - r0 + 1) * n);
            _maskTex.Apply(false, false);
            Mask.ClearDirty();
        }

        /// <summary>Tool influence on the wool (world space). Air pushes/wobbles, water presses.</summary>
        public void SetTool(bool active, Vector3 worldPos, Vector3 worldDir, float radius, float airPush, float waterPush)
        {
            _toolActive = Mathf.MoveTowards(_toolActive, active ? 1f : 0f, Time.deltaTime * 6f);
            Vector3 p = transform.InverseTransformPoint(worldPos);
            Vector3 d = transform.InverseTransformDirection(worldDir);
            _instance.SetVector(ToolPosId, new Vector4(p.x, p.y, p.z, _toolActive));
            _instance.SetVector(ToolDirId, new Vector4(d.x, d.y, d.z, radius));
            _instance.SetFloat(AirPushId, airPush);
            _instance.SetFloat(WaterPushId, waterPush);
        }

        /// <summary>World-space raycast against the heap surface.</summary>
        public bool Raycast(Ray worldRay, out Vector3 worldHit, out Vector3 localHit)
        {
            Vector3 o = transform.InverseTransformPoint(worldRay.origin);
            Vector3 d = transform.InverseTransformDirection(worldRay.direction).normalized;
            if (Field != null && Field.Raycast(o, d, out localHit))
            {
                worldHit = transform.TransformPoint(localHit);
                return true;
            }
            worldHit = localHit = default;
            return false;
        }

        void OnDestroy()
        {
            if (_maskTex != null) Destroy(_maskTex);
            if (_instance != null) Destroy(_instance);
            if (_filter != null && _filter.sharedMesh != null) Destroy(_filter.sharedMesh);
        }
    }
}
