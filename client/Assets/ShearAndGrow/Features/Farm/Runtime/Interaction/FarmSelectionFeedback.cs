using System.Collections;
using UnityEngine;

namespace ShearAndGrow.Farm
{
    /// <summary>
    /// Cheap selection feedback (no post effects): squash-and-stretch pulse of the view, a warm rim boost on its
    /// materials through a MaterialPropertyBlock, and one pooled soft ground ring under the selection.
    /// </summary>
    public sealed class FarmSelectionFeedback : MonoBehaviour
    {
        static readonly int RimStrength = Shader.PropertyToID("_RimStrength");
        static readonly int RimColor = Shader.PropertyToID("_RimColor");

        [SerializeField] private Transform _ring;
        [SerializeField] private Color _highlight = new Color(1f, 0.85f, 0.45f, 1f);
        [SerializeField] private float _highlightRim = 1.25f;

        FarmInteractable _selected;
        MaterialPropertyBlock _mpb;
        Coroutine _pulse;
        Vector3 _baseScale = Vector3.one;
        Transform _pulsed;

        public FarmInteractable Selected => _selected;

        void Awake()
        {
            _mpb = new MaterialPropertyBlock();
            if (_ring != null) _ring.gameObject.SetActive(false);
        }

        public void SetRing(Transform ring) { _ring = ring; if (_ring != null) _ring.gameObject.SetActive(false); }

        public void Select(FarmInteractable target)
        {
            if (_selected != target) Deselect();
            _selected = target;
            if (target == null) return;
            Highlight(target, true);
            Pulse(target);
            PlaceRing(target);
        }

        /// <summary>Short confirmation pulse without keeping a selection (sheep tap before the transition).</summary>
        public void Pulse(FarmInteractable target)
        {
            if (_pulse != null) { StopCoroutine(_pulse); if (_pulsed != null) _pulsed.localScale = _baseScale; }
            _pulsed = target.ViewRoot;
            _baseScale = _pulsed.localScale;
            _pulse = StartCoroutine(PulseRoutine(_pulsed, _baseScale));
        }

        public void Deselect()
        {
            if (_selected != null) Highlight(_selected, false);
            _selected = null;
            if (_ring != null) _ring.gameObject.SetActive(false);
        }

        void Highlight(FarmInteractable t, bool on)
        {
            foreach (var r in t.Renderers)
            {
                if (r == null) continue;
                if (on)
                {
                    r.GetPropertyBlock(_mpb);
                    _mpb.SetFloat(RimStrength, _highlightRim);
                    _mpb.SetColor(RimColor, _highlight);
                    r.SetPropertyBlock(_mpb);
                }
                else r.SetPropertyBlock(null);
            }
        }

        void PlaceRing(FarmInteractable t)
        {
            if (_ring == null) return;
            Bounds b = t.ViewBounds;
            float r = Mathf.Max(b.extents.x, b.extents.z) * 1.25f + 0.3f;
            _ring.position = new Vector3(b.center.x, 0.03f, b.center.z);
            _ring.localScale = new Vector3(r * 2f, r * 2f, 1f);
            _ring.gameObject.SetActive(true);
        }

        IEnumerator PulseRoutine(Transform t, Vector3 baseScale)
        {
            float a = 0f;
            while (a < 1f)
            {
                a = Mathf.Min(1f, a + Time.unscaledDeltaTime / 0.32f);
                float s = Mathf.Sin(a * Mathf.PI);
                float squash = Mathf.Sin(a * Mathf.PI * 2f) * (1f - a);
                t.localScale = Vector3.Scale(baseScale, new Vector3(1f + s * 0.06f - squash * 0.04f, 1f + s * 0.1f + squash * 0.06f, 1f + s * 0.06f - squash * 0.04f));
                yield return null;
            }
            t.localScale = baseScale;
            _pulse = null;
        }

        void Update()
        {
            if (_ring != null && _ring.gameObject.activeSelf)
            {
                _ring.localRotation = Quaternion.Euler(90f, 0f, Time.unscaledTime * 25f); // gentle spin, stays flat
                if (_selected is SheepInteraction) PlaceRing(_selected);                   // follows a walking sheep
            }
        }
    }
}
