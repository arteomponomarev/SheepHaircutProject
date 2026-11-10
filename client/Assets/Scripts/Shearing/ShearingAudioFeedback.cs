using UnityEngine;

namespace ShearAndGrow
{
    /// <summary>Two fixed voices: one motor loop and one rate-limited cut accent.</summary>
    public sealed class ShearingAudioFeedback : MonoBehaviour
    {
        [SerializeField] private ShearingFeedbackSignal signal;
        [SerializeField] private ShearingFeedbackSettings settings;
        [SerializeField] private AudioSource motor;
        [SerializeField] private AudioSource accent;
        private float nextAccentAt;

        private void OnEnable()
        {
            if (signal == null || settings == null || motor == null || accent == null || motor == accent)
            { Debug.LogError("ShearingAudioFeedback requires signal, settings and two separate audio sources.", this); enabled = false; return; }
            motor.playOnAwake = accent.playOnAwake = false;
            motor.loop = true; accent.loop = false;
            motor.clip = settings.TrimmerLoop; accent.clip = settings.CutAccent;
            motor.volume = settings.LoopVolume; accent.volume = settings.CutVolume;
            signal.CuttingChanged += OnCuttingChanged;
            signal.Cut += OnCut;
            signal.Cleared += Stop;
            OnCuttingChanged(signal.IsCutting);
        }

        private void OnCuttingChanged(bool cutting)
        {
            if (cutting)
            { if (motor.clip != null && !motor.isPlaying) { motor.volume = 0; motor.Play(); } }
            else Stop();
        }
        private void Update()
        {
            if (motor != null && motor.isPlaying)
                motor.volume = Mathf.MoveTowards(motor.volume, settings.LoopVolume, Time.unscaledDeltaTime * 12f);
        }
        private void OnCut(Vector3 point, Vector3 normal)
        {
            if (Time.unscaledTime < nextAccentAt || accent.clip == null) return;
            nextAccentAt = Time.unscaledTime + settings.AccentInterval;
            accent.pitch = Random.Range(0.94f, 1.06f);
            accent.Play(); // Not PlayOneShot: accents cannot accumulate overlapping voices.
        }
        private void Stop() { if (motor != null) motor.Stop(); if (accent != null) accent.Stop(); }
        private void OnDisable()
        {
            if (signal != null)
            { signal.CuttingChanged -= OnCuttingChanged; signal.Cut -= OnCut; signal.Cleared -= Stop; }
            Stop();
        }
    }
}
