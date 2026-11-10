using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace ShearAndGrow
{
    /// <summary>Owns one sheep's GPU mask and paints UV-space circles. No input or reward logic.</summary>
    [DisallowMultipleComponent]
    public sealed class WoolMaskPainter : MonoBehaviour
    {
        [SerializeField] private Renderer woolRenderer;
        [SerializeField] private WoolMaskSettings settings;
        [SerializeField] private Shader stampShader;

        public RenderTexture Mask => mask;
        public float BrushRadius => settings.BrushRadius;
        public bool WrapU => settings.WrapU;
        // Observers receive the exact canonical stamps; wrap copies are rendering-only.
        public event Action<Vector2, float> BrushStamped;
        public event Action StrokePainted;
        public event Action MaskReset;

        private static readonly int BaseMapId = Shader.PropertyToID("_BaseMap");
        private static readonly int BrushId = Shader.PropertyToID("_Brush");
        private RenderTexture mask;
        private Material stampMaterial;
        private Mesh quad;
        private CommandBuffer commands;
        private MaterialPropertyBlock stampProperties;
        private MaterialPropertyBlock rendererProperties;

        private void OnEnable()
        {
            if (woolRenderer == null || settings == null || stampShader == null)
            {
                Debug.LogError("WoolMaskPainter requires renderer, settings and stamp shader references.", this);
                enabled = false;
                return;
            }
            if (mask != null) return; // Disabling/re-enabling must not regrow wool.

            mask = new RenderTexture(settings.Resolution, settings.Resolution, 0,
                RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear)
            {
                name = "ShearMask_" + GetInstanceID(),
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                useMipMap = false,
                autoGenerateMips = false,
                hideFlags = HideFlags.DontSave
            };
            mask.wrapModeU = settings.WrapU ? TextureWrapMode.Repeat : TextureWrapMode.Clamp;
            mask.Create();
            stampMaterial = new Material(stampShader) { hideFlags = HideFlags.HideAndDontSave };
            quad = new Mesh { name = "WoolMaskStampQuad", hideFlags = HideFlags.HideAndDontSave };
            quad.vertices = new[] { new Vector3(0, 0), new Vector3(1, 0), new Vector3(1, 1), new Vector3(0, 1) };
            quad.uv = new[] { Vector2.zero, Vector2.right, Vector2.one, Vector2.up };
            quad.triangles = new[] { 0, 1, 2, 0, 2, 3 };
            quad.UploadMeshData(true);
            commands = new CommandBuffer { name = "Paint sheep wool mask" };
            stampProperties = new MaterialPropertyBlock();
            rendererProperties = new MaterialPropertyBlock();
            woolRenderer.GetPropertyBlock(rendererProperties);
            // URP Lit alpha clipping reads mask alpha in forward, shadow and depth passes.
            rendererProperties.SetTexture(BaseMapId, mask);
            woolRenderer.SetPropertyBlock(rendererProperties);
            ResetMask();
        }

        public void PaintCircle(Vector2 uv, float radius) => PaintStroke(uv, uv, radius);

        /// <summary>Overlapping stamps between valid hits. Callers own gesture/discontinuity boundaries.</summary>
        public void PaintStroke(Vector2 from, Vector2 to, float radius)
        {
            if (!isActiveAndEnabled || mask == null || !mask.IsCreated() ||
                !IsFinite(from) || !IsFinite(to) || float.IsNaN(radius) || float.IsInfinity(radius))
                return;
            radius = Mathf.Clamp(radius, 1f / mask.width, 0.25f);
            from = NormalizeUv(from);
            to = NormalizeUv(to);
            Vector2 delta = to - from;
            if (settings.WrapU)
                delta.x -= Mathf.Round(delta.x); // Short path across the periodic U seam.

            int steps = Mathf.Max(1, Mathf.CeilToInt(delta.magnitude / (radius * settings.StampSpacing)));
            commands.Clear();
            commands.SetRenderTarget(mask, RenderBufferLoadAction.Load, RenderBufferStoreAction.Store);
            commands.SetViewport(new Rect(0, 0, mask.width, mask.height));
            for (int i = 0; i <= steps; i++)
            {
                Vector2 uv = NormalizeUv(from + delta * ((float)i / steps));
                Stamp(uv, radius);
                if (settings.WrapU && uv.x < radius) Stamp(uv + Vector2.right, radius);
                if (settings.WrapU && uv.x > 1f - radius) Stamp(uv - Vector2.right, radius);
                BrushStamped?.Invoke(uv, radius);
            }
            Graphics.ExecuteCommandBuffer(commands);
            commands.Clear();
            StrokePainted?.Invoke();
        }

        private void Stamp(Vector2 uv, float radius)
        {
            stampProperties.SetVector(BrushId, new Vector4(uv.x, uv.y, radius, 0));
            commands.DrawMesh(quad, Matrix4x4.identity, stampMaterial, 0, 0, stampProperties);
        }

        private Vector2 NormalizeUv(Vector2 uv) => new Vector2(
            settings.WrapU ? Mathf.Repeat(uv.x, 1f) : Mathf.Clamp01(uv.x), Mathf.Clamp01(uv.y));

        private static bool IsFinite(Vector2 uv) => !float.IsNaN(uv.x) && !float.IsNaN(uv.y) &&
            !float.IsInfinity(uv.x) && !float.IsInfinity(uv.y);

        [ContextMenu("Reset Mask (Play Mode)")]
        public void ResetMask()
        {
            if (mask == null || commands == null) return;
            commands.Clear();
            commands.SetRenderTarget(mask, RenderBufferLoadAction.DontCare, RenderBufferStoreAction.Store);
            commands.ClearRenderTarget(false, true, Color.white);
            Graphics.ExecuteCommandBuffer(commands);
            commands.Clear();
            MaskReset?.Invoke();
        }

        private void OnDestroy()
        {
            if (woolRenderer != null && rendererProperties != null)
            {
                woolRenderer.GetPropertyBlock(rendererProperties);
                rendererProperties.SetTexture(BaseMapId, Texture2D.whiteTexture);
                woolRenderer.SetPropertyBlock(rendererProperties);
            }
            commands?.Release();
            if (mask != null) { mask.Release(); Destroy(mask); }
            if (quad != null) Destroy(quad);
            if (stampMaterial != null) Destroy(stampMaterial);
        }
    }
}
