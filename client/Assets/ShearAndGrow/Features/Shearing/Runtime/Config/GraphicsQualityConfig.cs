using UnityEngine;

namespace ShearAndGrow.Shearing
{
    /// <summary>
    /// Quality presets. Degradation lowers decorative fluff, shell count and shadows,
    /// NEVER removes floor clumps or hides the trimmer (spec §13).
    /// </summary>
    [CreateAssetMenu(menuName = "Shear & Grow/Shearing/Graphics Quality Config", fileName = "GraphicsQualityConfig")]
    public sealed class GraphicsQualityConfig : ScriptableObject
    {
        [System.Serializable]
        public struct Preset
        {
            public string Name;
            [Range(8, 48)] public int ShellCount;
            public bool WoolCastsShadows;
            [Range(0f, 2f)] public float FluffMultiplier;
            public int MaxHeroActive;
            public int TargetFrameRate;
        }

        public Preset[] Presets =
        {
            new Preset { Name = "Low", ShellCount = 14, WoolCastsShadows = false, FluffMultiplier = 0.4f, MaxHeroActive = 12, TargetFrameRate = 30 },
            new Preset { Name = "Medium", ShellCount = 20, WoolCastsShadows = false, FluffMultiplier = 0.8f, MaxHeroActive = 16, TargetFrameRate = 60 },
            new Preset { Name = "High", ShellCount = 28, WoolCastsShadows = true, FluffMultiplier = 1f, MaxHeroActive = 24, TargetFrameRate = 60 },
        };

        [Tooltip("Index used in the Editor / desktop.")]
        public int EditorPreset = 2;
        [Tooltip("Index used on mobile.")]
        public int MobilePreset = 1;

        public Preset Active
        {
            get
            {
                int idx = Application.isMobilePlatform ? MobilePreset : EditorPreset;
                idx = Mathf.Clamp(idx, 0, Presets.Length - 1);
                return Presets[idx];
            }
        }
    }
}
