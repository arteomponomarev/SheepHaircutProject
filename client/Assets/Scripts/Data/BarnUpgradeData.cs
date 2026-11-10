using System;
using UnityEngine;

namespace ShearAndGrow
{
    [CreateAssetMenu(menuName = "Shear and Grow/Barn Upgrade Data")]
    public sealed class BarnUpgradeData : ScriptableObject
    {
        [Serializable]
        public struct Level
        {
            [SerializeField, Min(0)] private int price;
            [SerializeField, Min(1)] private int capacity;
            public int Price => price;
            public int Capacity => capacity;
            public Level(int price, int capacity) { this.price = price; this.capacity = capacity; }
        }

        [SerializeField] private Level[] levels = { new Level(0, 3), new Level(300, 5), new Level(600, 8) };
        public int LevelCount => levels == null ? 0 : levels.Length;
        public bool IsValid
        {
            get
            {
                if (LevelCount == 0 || levels[0].Price != 0) return false;
                for (int i = 0; i < levels.Length; i++)
                    if (levels[i].Capacity < 1 || (i > 0 && (levels[i].Price <= 0 || levels[i].Capacity <= levels[i - 1].Capacity))) return false;
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
