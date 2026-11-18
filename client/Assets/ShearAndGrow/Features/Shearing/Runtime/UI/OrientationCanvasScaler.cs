using ShearAndGrow.Economy;
using UnityEngine;
using UnityEngine.UI;

namespace ShearAndGrow.Shearing
{
    /// <summary>
    /// Keeps UI elements at the same PHYSICAL size in portrait and landscape: the CanvasScaler reference resolution
    /// is the portrait reference (e.g. 1080×1920) in portrait and the swapped one (1920×1080) in landscape, with the
    /// same match factor. A 1080×2400 phone and the same phone rotated get an identical scale factor, so touch targets
    /// and text sizes do not change; layouts then decide where things go (see <see cref="ScreenLayout"/>).
    /// </summary>
    [RequireComponent(typeof(CanvasScaler))]
    public sealed class OrientationCanvasScaler : MonoBehaviour
    {
        [SerializeField] private Vector2 _portraitReference = new Vector2(1080f, 1920f);
        CanvasScaler _scaler;

        public Vector2 PortraitReference { get => _portraitReference; set { _portraitReference = value; Apply(); } }

        void Awake() { _scaler = GetComponent<CanvasScaler>(); Apply(); }
        void OnEnable() { ScreenLayout.Changed += Apply; Apply(); }
        void OnDisable() => ScreenLayout.Changed -= Apply;

        public void Apply()
        {
            if (_scaler == null) _scaler = GetComponent<CanvasScaler>();
            if (_scaler == null) return;
            var p = _portraitReference;
            float shortSide = Mathf.Min(p.x, p.y), longSide = Mathf.Max(p.x, p.y);
            _scaler.referenceResolution = ScreenLayout.IsLandscape ? new Vector2(longSide, shortSide) : new Vector2(shortSide, longSide);
        }
    }

    /// <summary>Small layout helpers shared by the code-built HUDs.</summary>
    public static class UiFit
    {
        /// <summary>
        /// Uniformly scales a point-anchored panel down (never up) so it fits inside <paramref name="container"/> with a
        /// margin. Safety net for unusual aspect ratios (tablets, foldables, very wide landscape phones).
        /// </summary>
        public static void ScaleToFit(RectTransform panel, RectTransform container, float margin = 24f)
        {
            if (panel == null || container == null) return;
            Vector2 avail = container.rect.size - new Vector2(margin * 2f, margin * 2f);
            Vector2 size = panel.rect.size;
            if (size.x <= 1f || size.y <= 1f || avail.x <= 1f || avail.y <= 1f) { panel.localScale = Vector3.one; return; }
            float s = Mathf.Min(1f, avail.x / size.x, avail.y / size.y);
            panel.localScale = new Vector3(s, s, 1f);
        }
    }
}
