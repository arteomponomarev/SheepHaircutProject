using System;
using UnityEngine;

namespace ShearAndGrow
{
    /// <summary>Shared in-run barn level, capacity and paid upgrades. Does not store sheep or Wool.</summary>
    [CreateAssetMenu(menuName = "Shear and Grow/Barn Controller")]
    public sealed class BarnController : ScriptableObject
    {
        [SerializeField] private BarnUpgradeData data;
        [SerializeField] private EconomyState economy;
        private static int runVersion;
        [NonSerialized] private int initializedVersion = -1;
        [NonSerialized] private int levelIndex;
        [NonSerialized] private bool purchasing;
        public event Action Changed;
        public EconomyState Economy => economy;
        public int Level { get { EnsureRun(); return levelIndex + 1; } }
        public bool IsConfigured => data != null && data.IsValid && economy != null && Level <= data.LevelCount;
        public int Capacity => IsConfigured && data.TryGetLevel(Level - 1, out var current) ? current.Capacity : 0;
        public bool IsMaxLevel => IsConfigured && Level == data.LevelCount;
        public int NextPrice => IsConfigured && data.TryGetLevel(Level, out var next) ? next.Price : 0;
        public int NextCapacity => IsConfigured && data.TryGetLevel(Level, out var next) ? next.Capacity : Capacity;
        public bool CanUpgrade => IsConfigured && !purchasing && !IsMaxLevel && economy.CanAfford(NextPrice);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void NewRun() => runVersion++;
        private void EnsureRun()
        {
            if (initializedVersion == runVersion) return;
            initializedVersion = runVersion; levelIndex = 0; purchasing = false;
        }
        public bool TryUpgrade()
        {
            EnsureRun();
            if (!CanUpgrade) return false;
            purchasing = true;
            try
            {
                if (!economy.TrySpendCoins(NextPrice)) return false;
                levelIndex++;
            }
            finally { purchasing = false; }
            Changed?.Invoke(); return true;
        }
    }
}
