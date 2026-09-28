using System;
using UnityEngine;

namespace ShearAndGrow
{
    /// <summary>In-run trimmer level and purchase rules. No rendering, input or sheep dependencies.</summary>
    [CreateAssetMenu(menuName = "Shear and Grow/Trimmer Upgrade State")]
    public sealed class TrimmerUpgradeState : ScriptableObject
    {
        [SerializeField] private TrimmerUpgradeData data;
        [SerializeField] private EconomyState economy;
        private static int runVersion;
        [NonSerialized] private int initializedVersion = -1;
        [NonSerialized] private int levelIndex;
        [NonSerialized] private bool purchasing;
        public event Action Changed;

        public EconomyState Economy => economy;
        public int Level { get { EnsureRun(); return levelIndex + 1; } }
        public bool IsConfigured => data != null && data.IsValid && economy != null && Level <= data.LevelCount;
        public bool IsMaxLevel => IsConfigured && Level == data.LevelCount;
        public int NextPrice => IsConfigured && data.TryGetLevel(Level, out var next) ? next.Price : 0;
        public float BrushRadius => IsConfigured && data.TryGetLevel(Level - 1, out var current) ? current.BrushRadius : 0;
        public bool CanUpgrade => !purchasing && IsConfigured && !IsMaxLevel && economy.CanAfford(NextPrice);

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
            // Wallet observers cannot re-enter this purchase while its level is being committed.
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
