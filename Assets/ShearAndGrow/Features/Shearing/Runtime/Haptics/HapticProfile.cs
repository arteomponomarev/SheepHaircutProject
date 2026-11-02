using UnityEngine;

namespace ShearAndGrow.Shearing
{
    /// <summary>
    /// One feel profile (Soft / Satisfying / Powerful). All values are tunable in the Inspector without code changes.
    /// Android and iOS motors are perceived very differently, so each platform gets its own output scale.
    /// </summary>
    [CreateAssetMenu(menuName = "Shear & Grow/Shearing/Haptic Profile", fileName = "HapticProfile")]
    public sealed class HapticProfile : ScriptableObject
    {
        [Header("Identity")]
        public string DisplayName = "Satisfying";

        [Header("Global")]
        [Tooltip("Master multiplier for every effect of this profile.")]
        [Range(0f, 1f)] public float MainIntensity = 0.85f;
        [Tooltip("Base strength of the continuous cutting texture (scaled by cut density).")]
        [Range(0f, 1f)] public float ContinuousIntensity = 0.55f;
        [Tooltip("Crispness 0 = round/dull, 1 = sharp/clicky. Used by iOS Core Haptics and Android primitive choice.")]
        [Range(0f, 1f)] public float Sharpness = 0.55f;

        [Header("Accents")]
        [Range(0f, 1f)] public float ContactImpactStrength = 0.85f;
        [Range(0f, 1f)] public float ChunkImpactStrength = 1f;
        [Range(0f, 1f)] public float CompletionStrength = 0.9f;
        [Tooltip("Gap between the two completion taps (s).")]
        [Range(0.05f, 0.4f)] public float CompletionGap = 0.14f;

        [Header("Timing")]
        [Tooltip("Minimum time between any two discrete pulses (s).")]
        [Range(0.02f, 0.3f)] public float MinIntervalBetweenPulses = 0.07f;
        [Tooltip("How often the continuous effect may be re-parameterised (s). Also the texture pulse period on pulse-based devices.")]
        [Range(0.03f, 0.3f)] public float ContinuousUpdateInterval = 0.09f;
        [Tooltip("Minimum time between two chunk accents (s).")]
        [Range(0.05f, 1.5f)] public float ChunkCooldown = 0.35f;

        [Header("Texture dynamics")]
        [Tooltip("Lowest texture strength while cutting (fraction of ContinuousIntensity) so a thin cut is still felt.")]
        [Range(0f, 1f)] public float TextureFloor = 0.45f;
        [Tooltip("New wool area per second (normalised) that reaches full texture strength.")]
        public float DensityForFullTexture = 0.03f;

        [Header("Per-platform output scale (perception differs)")]
        [Range(0.2f, 1.5f)] public float AndroidScale = 1f;
        [Range(0.2f, 1.5f)] public float IOSScale = 1f;
    }
}
