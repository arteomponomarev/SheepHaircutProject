using System;
using UnityEngine;

namespace ShearAndGrow
{
    /// <summary>Shared in-run inventory and sale rules. No sheep, scene, input or UI dependencies.</summary>
    [CreateAssetMenu(menuName = "Shear and Grow/Economy State")]
    public sealed class EconomyState : ScriptableObject
    {
        [SerializeField] private GameBalanceData balance;
        private static int runVersion;
        [NonSerialized] private int initializedVersion = -1;
        [NonSerialized] private decimal wool, coins;

        public decimal Wool { get { EnsureRun(); return wool; } }
        public decimal Coins { get { EnsureRun(); return coins; } }
        public bool CanSellWool => TryGetSaleValue(out _);
        public decimal SaleValue => TryGetSaleValue(out decimal value) ? value : 0;
        public event Action Changed;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void NewRun() => runVersion++;

        private void EnsureRun()
        {
            if (initializedVersion == runVersion) return;
            initializedVersion = runVersion; wool = coins = 0;
        }

        public bool TryAddWool(float amount)
        {
            EnsureRun();
            if (float.IsNaN(amount) || float.IsInfinity(amount) || amount < 0) return false;
            if (amount == 0) return true;
            // Convert once at the gameplay/inventory boundary. Never round each sale or UI update.
            try { wool += (decimal)amount; }
            catch (OverflowException) { return false; }
            Changed?.Invoke(); return true;
        }

        private bool TryGetSaleValue(out decimal value)
        {
            EnsureRun(); value = 0;
            if (wool <= 0 || balance == null || balance.WoolUnitPrice <= 0) return false;
            try
            {
                decimal sale = wool * balance.WoolUnitPrice;
                if (sale > decimal.MaxValue - coins) return false;
                value = sale; return true;
            }
            catch (OverflowException) { return false; }
        }

        public bool TrySellAllWool(out decimal coinsEarned)
        {
            if (!TryGetSaleValue(out coinsEarned)) return false;
            // Commit both balances before notifying observers: repeated/reentrant Sell is a no-op.
            coins += coinsEarned; wool = 0;
            Changed?.Invoke(); return true;
        }

        public bool CanAfford(decimal cost) => cost >= 0 && Coins >= cost;

        public bool TrySpendCoins(decimal cost)
        {
            if (!CanAfford(cost)) return false;
            if (cost == 0) return true;
            coins -= cost;
            Changed?.Invoke(); return true;
        }
    }
}
