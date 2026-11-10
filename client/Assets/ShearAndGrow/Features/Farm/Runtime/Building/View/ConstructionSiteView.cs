using UnityEngine;

namespace ShearAndGrow.Farm
{
    /// <summary>
    /// The single (pooled) construction-site dressing: a dirt pad scaled to the footprint, scaffold poles at the
    /// corners, a plank pile, a rope fence and a small dust ParticleSystem. Shown while a building is under
    /// construction; the completion sparkle is a second small ParticleSystem. No physics, ≤ 12 dust particles.
    /// </summary>
    public sealed class ConstructionSiteView : MonoBehaviour
    {
        [SerializeField] private Transform _pad;
        [SerializeField] private Transform[] _cornerPoles = new Transform[4];
        [SerializeField] private Transform _scaffold;
        [SerializeField] private Transform _planks;
        [SerializeField] private ParticleSystem _dust;
        [SerializeField] private ParticleSystem _sparkle;

        Vector2 _half;
        Vector3 _center;

        public bool IsShown => gameObject.activeSelf && _pad != null && _pad.gameObject.activeSelf;

        public void Configure(Transform pad, Transform[] poles, Transform scaffold, Transform planks, ParticleSystem dust, ParticleSystem sparkle)
        {
            _pad = pad; _cornerPoles = poles; _scaffold = scaffold; _planks = planks; _dust = dust; _sparkle = sparkle;
        }

        public void Show(Vector3 center, Vector2 halfSize)
        {
            _center = center; _half = halfSize;
            gameObject.SetActive(true);
            transform.position = center;
            SetDressing(true);
            if (_pad != null) _pad.localScale = new Vector3(halfSize.x * 2f + 0.3f, 1f, halfSize.y * 2f + 0.3f);
            for (int i = 0; i < _cornerPoles.Length && i < 4; i++)
            {
                if (_cornerPoles[i] == null) continue;
                float sx = (i == 0 || i == 3) ? -1f : 1f, sz = i < 2 ? -1f : 1f;
                _cornerPoles[i].localPosition = new Vector3(sx * (halfSize.x + 0.12f), 0f, sz * (halfSize.y + 0.12f));
            }
            if (_planks != null) _planks.localPosition = new Vector3(halfSize.x + 0.55f, 0f, -halfSize.y * 0.4f);
            if (_scaffold != null) _scaffold.localScale = new Vector3(halfSize.x * 2f + 0.24f, 1f, halfSize.y * 2f + 0.24f);
        }

        /// <summary>Visual stage: scaffold appears with the walls and stays until completion.</summary>
        public void SetStage(int stage)
        {
            if (_scaffold != null) _scaffold.gameObject.SetActive(stage >= 1 && stage < 4);
            if (_planks != null) _planks.localScale = Vector3.one * (stage <= 1 ? 1f : stage == 2 ? 0.7f : 0.4f);
        }

        public void Hide()
        {
            SetDressing(false);
            if (_dust != null) _dust.Stop(true, ParticleSystemStopBehavior.StopEmitting);
        }

        void SetDressing(bool on)
        {
            if (_pad != null) _pad.gameObject.SetActive(on);
            foreach (var p in _cornerPoles) if (p != null) p.gameObject.SetActive(on);
            if (_scaffold != null) _scaffold.gameObject.SetActive(on);
            if (_planks != null) _planks.gameObject.SetActive(on);
        }

        public void Puff(Vector3 at)
        {
            if (_dust == null) return;
            _dust.transform.position = at + Vector3.up * 0.1f;
            _dust.Emit(3);
        }

        public void Sparkle(Vector3 center, Vector2 halfSize)
        {
            if (_sparkle == null) return;
            gameObject.SetActive(true);
            _sparkle.transform.position = center + Vector3.up * 1.2f;
            var shape = _sparkle.shape;
            shape.scale = new Vector3(halfSize.x * 2f, 1.5f, halfSize.y * 2f);
            _sparkle.Emit(26);
            if (_dust != null) { _dust.transform.position = center; _dust.Emit(8); }
        }
    }
}