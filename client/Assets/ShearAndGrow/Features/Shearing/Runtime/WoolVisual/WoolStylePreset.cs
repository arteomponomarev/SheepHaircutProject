using UnityEngine;

namespace ShearAndGrow.Shearing
{
    public enum WoolLook { RealisticFleece, CartoonCurls, PlushWool, ReferencePuffs }

    [CreateAssetMenu(menuName = "Shear & Grow/Shearing/Wool Style")]
    public sealed class WoolStylePreset : ScriptableObject
    {
        [SerializeField] private WoolLook _look;
        [SerializeField] private Color _rootColor = new Color(0.76f, 0.69f, 0.59f);
        [SerializeField] private Color _tipColor = new Color(1f, 0.96f, 0.87f);
        [SerializeField, Range(200, 1800)] private int _lockCount = 850;
        [SerializeField, Range(0.01f, 0.09f)] private float _lockRadius = 0.043f;
        [SerializeField, Range(0.003f, 0.04f)] private float _fibreRadius = 0.012f;
        [SerializeField, Range(0.5f, 3f)] private float _turns = 1.65f;
        [SerializeField, Range(0f, 1f)] private float _roughness = 0.55f;
        public WoolLook Look => _look;
        public Color RootColor => _rootColor;
        public Color TipColor => _tipColor;
        public int LockCount => _lockCount;
        public float LockRadius => _lockRadius;
        public float FibreRadius => _fibreRadius;
        public float Turns => _turns;
        public float Roughness => _roughness;
    }
}
