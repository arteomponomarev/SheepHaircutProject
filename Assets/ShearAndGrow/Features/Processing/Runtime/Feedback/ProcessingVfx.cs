using UnityEngine;

namespace ShearAndGrow.Processing
{
    /// <summary>
    /// Small, capped, pooled particle effects (one ParticleSystem per effect, emitted manually with EmitParams – no
    /// allocations, no realtime shadows, small alpha billboards for low overdraw):
    ///   Wash: spray/splash at the jet hit, drips falling from the heap through the grate.
    ///   Dry: droplets blown off the wool, a few fluff motes, soft completion sparkles.
    /// </summary>
    public sealed class ProcessingVfx : MonoBehaviour
    {
        [SerializeField] private Material _dropMaterial;
        [SerializeField] private Material _fluffMaterial;
        [Header("Caps")]
        [SerializeField] private int _maxSpray = 80;
        [SerializeField] private int _maxDrips = 30;
        [SerializeField] private int _maxFluff = 20;
        [SerializeField] private int _maxSparkle = 30;

        ParticleSystem _spray, _drips, _fluff, _sparkle;
        float _sprayAcc, _dripAcc, _fluffAcc;
        Vector3[] _dripPoints;
        System.Random _rng = new System.Random(3);
        ProcessingStage _stage;
        float _fluffMultiplier = 1f;

        public int ActiveParticles =>
            (_spray ? _spray.particleCount : 0) + (_drips ? _drips.particleCount : 0) + (_fluff ? _fluff.particleCount : 0) + (_sparkle ? _sparkle.particleCount : 0);

        public void Initialise(ProcessingStage stage, Vector3[] heapEdgePointsWorld, float qualityMultiplier)
        {
            _stage = stage;
            _dripPoints = heapEdgePointsWorld;
            _fluffMultiplier = qualityMultiplier;
            bool wash = stage == ProcessingStage.Wash;
            int q(int n) => Mathf.Max(4, Mathf.RoundToInt(n * Mathf.Lerp(0.5f, 1f, Mathf.Clamp01(qualityMultiplier))));
            _spray = Create("Spray", _dropMaterial, q(_maxSpray), 1.0f);
            if (wash) _drips = Create("Drips", _dropMaterial, q(_maxDrips), 1.0f);
            else _fluff = Create("Fluff", _fluffMaterial, q(_maxFluff), -0.03f);
            _sparkle = Create("Sparkle", _fluffMaterial, q(_maxSparkle), -0.05f);
        }

        ParticleSystem Create(string n, Material mat, int max, float gravity)
        {
            var go = new GameObject(n);
            go.transform.SetParent(transform, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.playOnAwake = false;
            main.maxParticles = max;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.gravityModifier = gravity;
            main.startLifetime = 0.5f;
            main.loop = false;
            var em = ps.emission; em.enabled = false;
            var shape = ps.shape; shape.enabled = false;
            var col = ps.colorOverLifetime;
            col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                      new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.8f, 0.6f), new GradientAlphaKey(0f, 1f) });
            col.color = new ParticleSystem.MinMaxGradient(g);
            var r = ps.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = mat;
            r.renderMode = ParticleSystemRenderMode.Billboard;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            r.maxParticleSize = 0.05f;
            ps.Play();
            return ps;
        }

        float R(float a, float b) => a + (float)_rng.NextDouble() * (b - a);
        Vector3 RandDir() { var v = new Vector3(R(-1, 1), R(-1, 1), R(-1, 1)); return v.sqrMagnitude > 1e-4f ? v.normalized : Vector3.up; }

        /// <summary>Water hitting wool (or the table). Rate scales with the jet; caps keep it cheap.</summary>
        public void Spray(Vector3 pos, Vector3 normal, Vector3 jetDir, float intensity, float dt, bool onWool)
        {
            _sprayAcc += dt * Mathf.Lerp(18f, 70f, intensity) * (onWool ? 1f : 0.6f);
            var ep = new ParticleSystem.EmitParams();
            while (_sprayAcc >= 1f)
            {
                _sprayAcc -= 1f;
                Vector3 refl = Vector3.Reflect(jetDir, normal);
                Vector3 v = (refl * 0.5f + normal * 0.6f + RandDir() * 0.45f).normalized * R(0.5f, 1.3f);
                ep.position = pos + normal * 0.01f;
                ep.velocity = v;
                ep.startSize = R(0.006f, 0.013f);
                ep.startLifetime = R(0.25f, 0.5f);
                ep.startColor = new Color(0.82f, 0.93f, 1f, R(0.55f, 0.85f));
                _spray.Emit(ep, 1);
            }
        }

        /// <summary>Drips under the heap / through the grate; rate grows with how wet the batch already is.</summary>
        public void Drips(float wetness01, float dt)
        {
            if (_drips == null || _dripPoints == null || _dripPoints.Length == 0) return;
            _dripAcc += dt * Mathf.Lerp(0f, 11f, wetness01);
            var ep = new ParticleSystem.EmitParams();
            while (_dripAcc >= 1f)
            {
                _dripAcc -= 1f;
                ep.position = _dripPoints[_rng.Next(0, _dripPoints.Length)] + new Vector3(R(-0.01f, 0.01f), 0f, R(-0.01f, 0.01f));
                ep.velocity = new Vector3(0f, R(-0.2f, 0f), 0f);
                ep.startSize = R(0.007f, 0.011f);
                ep.startLifetime = R(0.45f, 0.7f);
                ep.startColor = new Color(0.8f, 0.92f, 1f, 0.8f);
                _drips.Emit(ep, 1);
            }
        }

        /// <summary>Drying: droplets blown off the wool + a few fluff motes.</summary>
        public void Blow(Vector3 pos, Vector3 normal, Vector3 airDir, float work01, float dt)
        {
            _sprayAcc += dt * 40f * work01;
            var ep = new ParticleSystem.EmitParams();
            Vector3 flat = Vector3.ProjectOnPlane(airDir, Vector3.up).normalized;
            while (_sprayAcc >= 1f)
            {
                _sprayAcc -= 1f;
                ep.position = pos + normal * 0.012f + RandDir() * 0.02f;
                ep.velocity = (flat * 0.9f + Vector3.up * 0.55f + RandDir() * 0.4f).normalized * R(0.5f, 1.1f);
                ep.startSize = R(0.005f, 0.01f);
                ep.startLifetime = R(0.3f, 0.55f);
                ep.startColor = new Color(0.85f, 0.94f, 1f, 0.75f);
                _spray.Emit(ep, 1);
            }
            if (_fluff == null) return;
            _fluffAcc += dt * 5f * work01 * _fluffMultiplier;
            while (_fluffAcc >= 1f)
            {
                _fluffAcc -= 1f;
                ep.position = pos + normal * 0.02f;
                ep.velocity = (Vector3.up * 0.25f + flat * 0.15f + RandDir() * 0.08f);
                ep.startSize = R(0.012f, 0.022f);
                ep.startLifetime = R(1.0f, 1.6f);
                ep.startColor = new Color(1f, 0.98f, 0.95f, 0.9f);
                _fluff.Emit(ep, 1);
            }
        }

        /// <summary>Soft sparkles (notable area finished / completion).</summary>
        public void Sparkle(Vector3 pos, int count, float radius)
        {
            var ep = new ParticleSystem.EmitParams();
            for (int i = 0; i < count; i++)
            {
                ep.position = pos + new Vector3(R(-radius, radius), R(0f, radius * 0.4f), R(-radius, radius));
                ep.velocity = Vector3.up * R(0.06f, 0.2f) + RandDir() * 0.04f;
                ep.startSize = R(0.012f, 0.026f);
                ep.startLifetime = R(0.6f, 1.1f);
                ep.startColor = new Color(1f, 0.97f, 0.8f, 0.95f);
                _sparkle.Emit(ep, 1);
            }
        }

        public void StopEmitting() { _sprayAcc = _dripAcc = _fluffAcc = 0f; }
    }
}
