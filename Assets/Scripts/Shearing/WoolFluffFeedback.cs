using UnityEngine;

namespace ShearAndGrow
{
    /// <summary>Small bounded particles complement the physical wool chunks.</summary>
    public sealed class WoolFluffFeedback : MonoBehaviour
    {
        [SerializeField] private ShearingFeedbackSignal signal;
        [SerializeField] private ShearingFeedbackSettings settings;
        [SerializeField] private ParticleSystem fluff;
        private float nextBurstAt;

        private void OnEnable()
        {
            if (signal == null || settings == null || fluff == null)
            { Debug.LogError("WoolFluffFeedback requires signal, settings and particle system.", this); enabled = false; return; }
            var main = fluff.main;
            main.maxParticles = settings.MaxFluff;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.playOnAwake = false;
            // Short-lived particles must expire even after their renderer leaves the camera.
            main.cullingMode = ParticleSystemCullingMode.AlwaysSimulate;
            var emission = fluff.emission; emission.enabled = false;
            signal.Cut += OnCut; signal.Cleared += Clear;
        }
        private void OnCut(Vector3 point, Vector3 normal)
        {
            if (Time.unscaledTime < nextBurstAt) return;
            nextBurstAt = Time.unscaledTime + settings.FluffInterval;
            if (!fluff.isPlaying) fluff.Play();
            int count = Mathf.Min(settings.FluffPerBurst, settings.MaxFluff - fluff.particleCount);
            for (int i = 0; i < count; i++)
            {
                var particle = new ParticleSystem.EmitParams
                {
                    position = point + normal * 0.04f,
                    velocity = normal * settings.FluffSpeed + Random.insideUnitSphere * settings.FluffSpeed * 0.6f,
                    startLifetime = settings.FluffLifetime * Random.Range(0.7f, 1f),
                    startSize = Random.Range(0.1f, 0.2f),
                    rotation3D = Random.insideUnitSphere * 180f,
                    startColor = new Color(1f, 0.98f, 0.88f, 0.6f)
                };
                fluff.Emit(particle, 1);
            }
        }
        private void Clear() { if (fluff != null) fluff.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear); }
        private void OnDisable()
        {
            if (signal != null) { signal.Cut -= OnCut; signal.Cleared -= Clear; }
            Clear();
        }
    }
}
