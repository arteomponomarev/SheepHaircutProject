using UnityEngine;

namespace ShearAndGrow
{
    [CreateAssetMenu(fileName = "WoolMaskSettings", menuName = "Shear and Grow/Wool Mask Settings")]
    public sealed class WoolMaskSettings : ScriptableObject
    {
        [SerializeField, Range(128, 1024)] private int resolution = 512;
        [Tooltip("Circular brush radius in the wool mesh's UV space.")]
        [SerializeField, Range(0.005f, 0.1f)] private float brushRadius = 0.025f;
        [Tooltip("Distance between stamps as a fraction of brush radius.")]
        [SerializeField, Range(0.1f, 1f)] private float stampSpacing = 0.5f;
        [Tooltip("Enable for a continuous cylindrical/spherical U seam; disable for non-wrapping UV charts.")]
        [SerializeField] private bool wrapU = true;

        public int Resolution => Mathf.Clamp(Mathf.NextPowerOfTwo(resolution), 128, 1024);
        public float BrushRadius => Mathf.Clamp(brushRadius, 0.005f, 0.1f);
        public float StampSpacing => Mathf.Clamp(stampSpacing, 0.1f, 1f);
        public bool WrapU => wrapU;
    }
}
