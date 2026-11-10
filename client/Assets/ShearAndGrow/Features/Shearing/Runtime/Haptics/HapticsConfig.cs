using UnityEngine;

namespace ShearAndGrow.Shearing
{
    [CreateAssetMenu(menuName = "Shear & Grow/Shearing/Haptics Config", fileName = "HapticsConfig")]
    public sealed class HapticsConfig : ScriptableObject
    {
        [Tooltip("Soft, Satisfying, Powerful (order is used by the debug panel and saved settings).")]
        public HapticProfile[] Profiles = new HapticProfile[3];
        [Tooltip("Index into Profiles used when the player has no saved choice. 1 = Satisfying.")]
        public int DefaultProfile = 1;

        [Header("Cutting state")]
        [Tooltip("If no NEW wool is removed for this long during a swipe, the texture stops (prevents on/off jitter).")]
        [Range(0.02f, 0.5f)] public float StopTimeout = 0.12f;
        [Tooltip("Contact impact needs at least this much new area in the first cut (filters accidental 1-texel touches).")]
        public float MinContactArea = 0.00002f;
        [Tooltip("Clumps smaller than this relative size never trigger a chunk accent.")]
        public float MinChunkSize = 1.3f;

        [Header("Android")]
        [Tooltip("Also mute haptics when Android 'Touch feedback / Haptic feedback' is turned off in system settings.")]
        public bool RespectAndroidTouchFeedbackSetting = false;
    }
}
