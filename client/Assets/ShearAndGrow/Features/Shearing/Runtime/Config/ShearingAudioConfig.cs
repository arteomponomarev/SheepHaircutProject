using UnityEngine;

namespace ShearAndGrow.Shearing
{
    [CreateAssetMenu(menuName = "Shear & Grow/Shearing/Audio Config", fileName = "ShearingAudioConfig")]
    public sealed class ShearingAudioConfig : ScriptableObject
    {
        [Header("Optional authored clips (procedural placeholders are synthesised when empty)")]
        public AudioClip MotorLoop;
        public AudioClip CutLoop;
        public AudioClip[] WoolTear;
        public AudioClip[] WoolFluff;
        public AudioClip[] WoolLanding;
        public AudioClip[] SheepMurmur;
        public AudioClip Ambience;
        public AudioClip Finish;

        [Header("Mix")]
        [Range(0f, 1f)] public float MasterVolume = 0.9f;
        [Range(0f, 1f)] public float MotorVolume = 0.32f;
        [Range(0f, 1f)] public float MotorIdleVolume = 0.12f;
        [Range(0f, 1f)] public float CutVolume = 0.55f;
        [Range(0f, 1f)] public float OneShotVolume = 0.45f;
        [Range(0f, 1f)] public float AmbienceVolume = 0.16f;
        [Tooltip("New-area-per-second (normalized) that maps to full cut-layer intensity.")]
        public float CutDensityForMax = 0.035f;
        public float MotorRampSeconds = 0.06f;
        public float CutRampSeconds = 0.05f;

        [Header("Rate limits")]
        public int OneShotVoices = 5;
        public float TearMinInterval = 0.12f;
        public float FluffMinInterval = 0.45f;
        public float LandingMinInterval = 0.15f;
        public Vector2 PitchJitter = new Vector2(0.92f, 1.08f);
        public Vector2 GainJitter = new Vector2(0.8f, 1f);
        public Vector2 MurmurInterval = new Vector2(7f, 16f);
    }
}
