using UnityEngine;

namespace ShearAndGrow
{
    /// <summary>Farm herd purchase/selection commands. Rendering, UI and growth clocks stay separate.</summary>
    public sealed class SheepManager : MonoBehaviour
    {
        [SerializeField] private SheepRosterState roster;
        [SerializeField] private EconomyState economy;
        [SerializeField] private GameBalanceData balance;
        private bool purchasing;
        public event System.Action Changed;
        public SheepRosterState Roster => roster;
        public EconomyState Economy => economy;
        public bool IsConfigured => roster != null && roster.IsConfigured && economy != null && balance != null;
        public decimal NextSheepPrice => IsConfigured ? balance.SheepPurchasePrice(roster.PurchasedCount) : 0;
        public bool CanBuySheep => IsConfigured && !purchasing && economy.CanAfford(NextSheepPrice);
        public bool TryBuySheep()
        {
            if (!CanBuySheep) return false;
            purchasing = true;
            try
            {
                if (!economy.TrySpendCoins(NextSheepPrice)) return false;
                roster.AddSheep();
            }
            finally { purchasing = false; }
            Changed?.Invoke(); return true;
        }
        public bool TrySelectSheep(int index) => IsConfigured && roster.TrySelectReady(index);
    }
}
