using UnityEngine;

namespace ShearAndGrow
{
    /// <summary>Connects surface samples to the assigned sheep's mask, keeping strokes separate.</summary>
    public sealed class WoolStrokePainter : MonoBehaviour
    {
        [SerializeField] private TrimmerPointerDriver surfaceSource;
        [SerializeField] private Collider woolSurface;
        [SerializeField] private WoolMaskPainter painter;

        private bool hasPrevious;
        private Vector2 previousUv;
        private float brushRadiusOverride;
        public float BrushRadius => brushRadiusOverride > 0 ? brushRadiusOverride : painter.BrushRadius;

        /// <summary>Generic runtime brush configuration. No knowledge of upgrades or economy.</summary>
        public void SetBrushRadius(float radius)
        {
            if (float.IsNaN(radius) || float.IsInfinity(radius) || radius < 0.005f || radius > 0.1f) return;
            if (Mathf.Approximately(brushRadiusOverride, radius)) return;
            brushRadiusOverride = radius;
            BreakStroke();
        }

        private void OnEnable()
        {
            if (surfaceSource == null || woolSurface == null || painter == null)
            {
                Debug.LogError("WoolStrokePainter requires surface source, collider and painter references.", this);
                enabled = false;
                return;
            }
            surfaceSource.SurfaceSampled += OnSurfaceSample;
            surfaceSource.ContactEnded += BreakStroke;
            painter.MaskReset += BreakStroke;
            BreakStroke();
        }

        private void OnSurfaceSample(RaycastHit hit)
        {
            if (hit.collider != woolSurface || !painter.isActiveAndEnabled)
            { BreakStroke(); return; }
            Vector2 uv = hit.textureCoord;
            if (!hasPrevious)
                painter.PaintCircle(uv, BrushRadius);
            else if ((uv - previousUv).sqrMagnitude > 0.00000001f)
                painter.PaintStroke(previousUv, uv, BrushRadius);
            previousUv = uv;
            hasPrevious = true;
        }

        private void BreakStroke() => hasPrevious = false;

        private void OnDisable()
        {
            if (surfaceSource != null)
            {
                surfaceSource.SurfaceSampled -= OnSurfaceSample;
                surfaceSource.ContactEnded -= BreakStroke;
            }
            if (painter != null) painter.MaskReset -= BreakStroke;
            BreakStroke();
        }
    }
}
