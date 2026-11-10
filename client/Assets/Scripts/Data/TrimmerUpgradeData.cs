using System;
using UnityEngine;

namespace ShearAndGrow
{
    [CreateAssetMenu(menuName = "Shear and Grow/Trimmer Upgrade Data")]
    public sealed class TrimmerUpgradeData : ScriptableObject
    {
        [Serializable]
        public struct Level
        {
            [SerializeField, Min(0)] private int price;
            [Tooltip("UV-space radius; cut width is twice this value.")]
            [SerializeField, Range(0.005f, 0.1f)] private float brushRadius;
            public int Price => price;
            public float BrushRadius => brushRadius;
            public Level(int price, float brushRadius) { this.price = price; this.brushRadius = brushRadius; }
        }

        [SerializeField] private Level[] levels =
        {
            new Level(0, 0.025f), new Level(90, 0.04f), new Level(180, 0.06f)
        };

        public int LevelCount => levels == null ? 0 : levels.Length;
        public bool IsValid
        {
            get
            {
                if (LevelCount == 0 || levels[0].Price != 0) return false;
                for (int i = 0; i < levels.Length; i++)
                {
                    float radius = levels[i].BrushRadius;
                    if (float.IsNaN(radius) || radius < 0.005f || radius > 0.1f ||
                        (i > 0 && (levels[i].Price <= 0 || radius <= levels[i - 1].BrushRadius))) return false;
                }
                return true;
            }
        }

        public bool TryGetLevel(int index, out Level level)
        {
            if (index < 0 || index >= LevelCount) { level = default; return false; }
            level = levels[index]; return true;
        }
    }
}
