using Unity.Collections;
using UnityEngine;

namespace ShearAndGrow.Shearing
{
    /// <summary>
    /// Visual-only wool layers (spec §5.1): separate undercoat mesh, volumetric shell bulk whose thickness is clipped by
    /// the mask, and a shear collider proxy. Mirrors the CPU authority into an R8 texture – only dirty rows are copied.
    /// Never used as a source of rewards.
    /// </summary>
    public sealed class WoolVisualRenderer : MonoBehaviour
    {
        static readonly int MaskId = Shader.PropertyToID("_Mask");
        static readonly int ThicknessId = Shader.PropertyToID("_Thickness");
        static readonly int TrimmerPosId = Shader.PropertyToID("_TrimmerPosOS");
        static readonly int TrimmerDirId = Shader.PropertyToID("_TrimmerDirOS");

        [Header("Materials")]
        [SerializeField] private Material _woolShellMaterial;
        [SerializeField] private Material _undercoatMaterial;

        Texture2D _maskTexture;
        Material _woolInstance;
        MeshRenderer _shellRenderer;
        MeshFilter _curlFilter;
        MeshRenderer _curlRenderer;
        SheepBodySurface _surface;
        float _thickness;
        readonly System.Collections.Generic.Dictionary<WoolStylePreset, Mesh> _styleMeshes = new System.Collections.Generic.Dictionary<WoolStylePreset, Mesh>();
        public void ApplyStyle(WoolStylePreset style)
        {
            if (style == null || _woolInstance == null) return;
            if (!_styleMeshes.TryGetValue(style, out var mesh))
            {
                mesh = style.Look == WoolLook.ReferencePuffs
                    ? WoolPuffMeshBuilder.BuildCoat(_surface, _thickness, style)
                    : WoolCurlGeometry.Build(_surface, _thickness, style);
                _styleMeshes.Add(style, mesh);
            }
            bool puffs = style.Look == WoolLook.ReferencePuffs;
            if (_curlFilter == null)
            {
                var curls = new GameObject("FleeceCoiledLocks");
                curls.transform.SetParent(transform, false);
                _curlFilter = curls.AddComponent<MeshFilter>();
                _curlRenderer = curls.AddComponent<MeshRenderer>();
                _curlRenderer.sharedMaterial = _woolInstance;
                _curlRenderer.shadowCastingMode = _shellRenderer.shadowCastingMode;
            }
            _curlFilter.sharedMesh = mesh;
            _woolInstance.SetColor("_WoolRootColor", style.RootColor);
            _woolInstance.SetColor("_WoolTipColor", style.TipColor);
            _woolInstance.SetFloat("_CurlScale", 1f / Mathf.Max(0.02f, style.LockRadius * 2.5f));
            _woolInstance.SetFloat("_DetailScale", style.Look == WoolLook.RealisticFleece ? 145f : 55f);
            // Puffs: the shell volume becomes a smooth, slightly darker base so gaps read as soft shadow, not skin.
            _woolInstance.SetFloat("_MinDensity", puffs ? 1f : 0.93f);
            _woolInstance.SetFloat("_BulkHeight", puffs ? 0.34f : 0.62f);
            _woolInstance.SetFloat("_BulkShade", puffs ? 0.9f : 1f);
            _woolInstance.SetFloat("_Droop", puffs ? 0f : 0.06f);
        }
        float _trimmerActive;
        Vector3 _trimmerPosOS = new Vector3(0, 100, 0);
        Vector3 _trimmerDirOS = Vector3.right;
        float _trimmerRadius = 0.08f;

        public MeshCollider ShearCollider { get; private set; }
        public Texture2D MaskTexture => _maskTexture;
        public Transform Body => transform;

        public void Build(SheepBodySurface surface, ShearingFeelConfig feel, GraphicsQualityConfig.Preset quality, int maskWidth, int maskHeight)
        {
            _surface = surface;
            _thickness = feel.WoolThickness;
            // Undercoat: always visible under the wool, stays at 100% shear.
            var under = new GameObject("BodyUndercoat");
            under.transform.SetParent(transform, false);
            under.AddComponent<MeshFilter>().sharedMesh = surface.BuildSurfaceMesh("Undercoat", 0f);
            var underMr = under.AddComponent<MeshRenderer>();
            underMr.sharedMaterial = _undercoatMaterial;

            // Wool bulk volume: all shells in one mesh / one draw call.
            var shells = new GameObject("WoolShell");
            shells.transform.SetParent(transform, false);
            shells.AddComponent<MeshFilter>().sharedMesh = surface.BuildShellMesh(quality.ShellCount, feel.WoolThickness);
            _shellRenderer = shells.AddComponent<MeshRenderer>();
            _woolInstance = new Material(_woolShellMaterial) { name = "WoolShell (Instance)" };
            _shellRenderer.sharedMaterial = _woolInstance;
            _shellRenderer.shadowCastingMode = quality.WoolCastsShadows
                ? UnityEngine.Rendering.ShadowCastingMode.On
                : UnityEngine.Rendering.ShadowCastingMode.Off;
            if (!quality.WoolCastsShadows)
            {
                // cheap shadow proxy so the sheep still grounds itself
                underMr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            }

            // Shear collider proxy at ~85% of the wool height: hit.textureCoord == body parametrisation.
            var col = new GameObject("ShearCollider");
            col.transform.SetParent(transform, false);
            col.layer = gameObject.layer;
            ShearCollider = col.AddComponent<MeshCollider>();
            ShearCollider.sharedMesh = surface.BuildSurfaceMesh("ShearColliderProxy", feel.WoolThickness * 0.85f);

            _maskTexture = new Texture2D(maskWidth, maskHeight, TextureFormat.R8, false, true)
            {
                name = "WoolCutMask",
                wrapModeU = TextureWrapMode.Repeat,
                wrapModeV = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                anisoLevel = 0,
            };
            _woolInstance.SetTexture(MaskId, _maskTexture);
            _woolInstance.SetFloat(ThicknessId, feel.WoolThickness);
        }

        /// <summary>Copies only dirty rows of the authority into the GPU mask (no per-frame full readback / upload of unchanged data).</summary>
        public void UploadMask(ShearMaskAuthority authority)
        {
            if (!authority.IsDirty || _maskTexture == null) return;
            NativeArray<byte> data = _maskTexture.GetPixelData<byte>(0);
            int start = authority.DirtyRowMin * authority.Width;
            int length = (authority.DirtyRowMax - authority.DirtyRowMin + 1) * authority.Width;
            NativeArray<byte>.Copy(authority.Occupancy, start, data, start, length);
            _maskTexture.Apply(false, false);
            authority.ClearDirty();
        }

        public void SetTrimmer(bool active, Vector3 worldPos, Vector3 worldDir, float radiusLocal)
        {
            float target = active ? 1f : 0f;
            _trimmerActive = Mathf.MoveTowards(_trimmerActive, target, Time.deltaTime * 8f);
            if (active)
            {
                _trimmerPosOS = transform.InverseTransformPoint(worldPos);
                Vector3 d = transform.InverseTransformDirection(worldDir);
                if (d.sqrMagnitude > 1e-6f) _trimmerDirOS = d.normalized;
            }
            _trimmerRadius = radiusLocal;
            if (_woolInstance == null) return;
            _woolInstance.SetVector(TrimmerPosId, new Vector4(_trimmerPosOS.x, _trimmerPosOS.y, _trimmerPosOS.z, _trimmerActive));
            _woolInstance.SetVector(TrimmerDirId, new Vector4(_trimmerDirOS.x, _trimmerDirOS.y, _trimmerDirOS.z, _trimmerRadius));
        }

        void OnDestroy()
        {
            foreach (var mesh in _styleMeshes.Values) if (mesh != null) Destroy(mesh);
            _styleMeshes.Clear();
            if (_maskTexture != null) Destroy(_maskTexture);
            if (_woolInstance != null) Destroy(_woolInstance);
        }
    }
}
