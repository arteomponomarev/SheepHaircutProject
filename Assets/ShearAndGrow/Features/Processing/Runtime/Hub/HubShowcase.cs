using ShearAndGrow.Economy;
using UnityEngine;

namespace ShearAndGrow.Processing
{
    /// <summary>
    /// Three small static wool piles (Dirty / Wet / Clean) on the Hub table using the same ProcessWool shader.
    /// Each pile scales smoothly with the stored amount (hidden at 0). Built once; no per-frame mask uploads.
    /// </summary>
    public sealed class HubShowcase : MonoBehaviour
    {
        [SerializeField] private ProcessingConfig _look;
        [SerializeField] private Material _material;
        [SerializeField] private WoolVisualStateController[] _piles = new WoolVisualStateController[3];
        [SerializeField] private int _puffsPerPile = 70;
        [SerializeField] private float _amountForFullSize = 300f;

        readonly float[] _scale = new float[3];
        readonly float[] _target = new float[3];

        public void Build()
        {
            for (int i = 0; i < 3; i++)
            {
                var pile = _piles[i];
                if (pile == null) continue;
                var cfg = Instantiate(_look);
                cfg.HeapHalfSize = new Vector2(0.15f, 0.12f);
                cfg.HeapHeight = 0.11f;
                cfg.PuffRadius = new Vector2(0.022f, 0.036f);
                cfg.Seed = 300 + i * 17;
                pile.Build(cfg, ProcessingStage.Wash, _puffsPerPile, 10, 7);
                if (i == 0) pile.SetStatic(0f, 0f);       // dirty
                else if (i == 1) pile.SetStatic(1f, 0f);  // wet
                else pile.SetStatic(1f, 1f);              // clean & dry
                Destroy(cfg);
                pile.transform.localScale = Vector3.zero;
            }
        }

        public void SetAmounts(int dirty, int wet, int clean)
        {
            _target[0] = Size(dirty); _target[1] = Size(wet); _target[2] = Size(clean);
        }

        float Size(int amount) => amount <= 0 ? 0f : Mathf.Lerp(0.45f, 1f, Mathf.Clamp01(Mathf.Log10(1f + amount) / Mathf.Log10(1f + _amountForFullSize)));

        void Update()
        {
            float dt = Time.deltaTime;
            for (int i = 0; i < 3; i++)
            {
                if (_piles[i] == null) continue;
                float s = Mathf.Lerp(_scale[i], _target[i], 1f - Mathf.Exp(-6f * dt));
                if (Mathf.Abs(s - _scale[i]) < 1e-4f) continue;
                _scale[i] = s;
                _piles[i].transform.localScale = Vector3.one * s;
            }
        }
    }
}
