using UnityEngine;

namespace ShearAndGrow
{
    [CreateAssetMenu(fileName = "ShearingFeedbackSettings", menuName = "Shear and Grow/Shearing Feedback")]
    public sealed class ShearingFeedbackSettings : ScriptableObject
    {
        [SerializeField] private AudioClip trimmerLoop;
        [SerializeField] private AudioClip cutAccent;
        [SerializeField, Range(0f, 1f)] private float loopVolume = 0.2f;
        [SerializeField, Range(0f, 1f)] private float cutVolume = 0.16f;
        [Tooltip("Brief audio/motion continuity between sparse occupancy samples; release still stops immediately.")]
        [SerializeField, Range(0f, 0.12f)] private float cutHoldSeconds = 0.075f;
        [SerializeField, Min(0.08f)] private float accentInterval = 0.14f;
        [SerializeField, Min(0.02f)] private float fluffInterval = 0.04f;
        [SerializeField, Range(1, 8)] private int fluffPerBurst = 3;
        [SerializeField, Range(8, 128)] private int maxFluff = 64;
        [SerializeField, Range(0.1f, 0.8f)] private float fluffLifetime = 0.4f;
        [SerializeField, Min(0f)] private float fluffSpeed = 0.3f;
        [SerializeField] private bool hapticsEnabled = true;
        [SerializeField, Min(0.1f)] private float hapticInterval = 0.16f;
        [SerializeField, Range(0f, 0.01f)] private float trimmerAmplitude = 0.0025f;
        [SerializeField, Range(0f, 2f)] private float trimmerTilt = 0.6f;
        [SerializeField, Range(5f, 40f)] private float trimmerFrequency = 24f;

        public AudioClip TrimmerLoop => trimmerLoop;
        public AudioClip CutAccent => cutAccent;
        public float LoopVolume => Mathf.Clamp01(loopVolume);
        public float CutVolume => Mathf.Clamp01(cutVolume);
        public float CutHoldSeconds => Mathf.Clamp(cutHoldSeconds, 0f, 0.12f);
        public float AccentInterval => Mathf.Max(0.08f, accentInterval);
        public float FluffInterval => Mathf.Max(0.02f, fluffInterval);
        public int FluffPerBurst => Mathf.Clamp(fluffPerBurst, 1, 8);
        public int MaxFluff => Mathf.Clamp(maxFluff, 8, 128);
        public float FluffLifetime => Mathf.Clamp(fluffLifetime, 0.1f, 0.8f);
        public float FluffSpeed => Mathf.Max(0f, fluffSpeed);
        public bool HapticsEnabled => hapticsEnabled;
        public float HapticInterval => Mathf.Max(0.1f, hapticInterval);
        public float TrimmerAmplitude => Mathf.Clamp(trimmerAmplitude, 0f, 0.01f);
        public float TrimmerTilt => Mathf.Clamp(trimmerTilt, 0f, 2f);
        public float TrimmerFrequency => Mathf.Clamp(trimmerFrequency, 5f, 40f);
    }
}
