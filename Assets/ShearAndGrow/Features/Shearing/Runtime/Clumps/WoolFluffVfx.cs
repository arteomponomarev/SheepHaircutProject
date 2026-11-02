using UnityEngine;

namespace ShearAndGrow.Shearing
{
    /// <summary>
    /// MICRO fluff (spec §6.2): airy, collision-free down that may fade after seconds. It never replaces the
    /// chunky physical clumps and is the first thing reduced by quality presets.
    /// </summary>
    public sealed class WoolFluffVfx : MonoBehaviour
    {
        [SerializeField] private Material _particleMaterial;
        [SerializeField] private float _particlesPerArea = 9000f;
        [SerializeField] private int _maxParticles = 400;

        ParticleSystem _ps;
        float _multiplier = 1f;
        float _carry;

        public void Initialise(float qualityMultiplier)
        {
            _multiplier = qualityMultiplier;
            var go = new GameObject("FluffParticles");
            go.transform.SetParent(transform, false);
            _ps = go.AddComponent<ParticleSystem>();
            _ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = _ps.main;
            main.playOnAwake = false;
            main.loop = false;
            main.maxParticles = _maxParticles;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.9f, 2.0f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.05f, 0.35f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.012f, 0.035f);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 0.97f, 0.9f, 0.9f), new Color(0.95f, 0.9f, 0.82f, 0.7f));
            main.gravityModifier = 0.03f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            var emission = _ps.emission;
            emission.enabled = false;
            var shape = _ps.shape;
            shape.enabled = false;
            var noise = _ps.noise;
            noise.enabled = true;
            noise.strength = 0.25f;
            noise.frequency = 0.8f;
            noise.scrollSpeed = 0.3f;
            var vel = _ps.limitVelocityOverLifetime;
            vel.enabled = true;
            vel.limit = 0.4f;
            vel.dampen = 0.15f;
            var col = _ps.colorOverLifetime;
            col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                      new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.1f), new GradientAlphaKey(1f, 0.6f), new GradientAlphaKey(0f, 1f) });
            col.color = g;
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = _particleMaterial;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            _ps.Play();
        }

        public void Emit(Vector3 worldPos, Vector3 normal, Vector3 tangent, float newArea)
        {
            if (_ps == null) return;
            _carry += newArea * _particlesPerArea * _multiplier;
            int count = Mathf.FloorToInt(_carry);
            if (count <= 0) return;
            _carry -= count;
            count = Mathf.Min(count, 6);
            var p = new ParticleSystem.EmitParams();
            for (int i = 0; i < count; i++)
            {
                Vector3 jitter = Random.insideUnitSphere * 0.04f;
                p.position = worldPos + normal * 0.06f + jitter;
                p.velocity = (normal * Random.Range(0.08f, 0.3f) + tangent * Random.Range(-0.1f, 0.25f) + Random.insideUnitSphere * 0.08f);
                p.applyShapeToPosition = false;
                _ps.Emit(p, 1);
            }
        }

        public void Clear()
        {
            if (_ps != null) _ps.Clear(true);
        }
    }
}
