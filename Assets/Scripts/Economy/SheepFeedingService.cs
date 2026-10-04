using System;
using UnityEngine;

namespace ShearAndGrow
{
    /// <summary>Purchases and immediately applies one food serving to an owned, growing sheep.</summary>
    public sealed class SheepFeedingService : MonoBehaviour
    {
        public enum Availability { Available, Unavailable, AlreadyReady, Shearing, BoostActive, NeedCoins }
        [SerializeField] private FoodData food;
        [SerializeField] private EconomyState economy;
        [SerializeField] private SheepRosterState roster;
        [SerializeField] private GameBalanceData balance;
        private bool purchasing;
        public event Action Changed;
        public FoodData Food => food;
        public EconomyState Economy => economy;
        public bool IsConfigured => food != null && food.IsValid && economy != null && roster != null && roster.IsConfigured && balance != null;
        public Availability GetAvailability(PrototypeSheepState sheep)
        {
            if (!IsConfigured || purchasing || sheep == null) return Availability.Unavailable;
            bool owned = false;
            for (int i = 0; i < roster.Count; i++) if (roster.GetSheep(i) == sheep) { owned = true; break; }
            if (!owned) return Availability.Unavailable;
            if (sheep.Status == PrototypeSheepState.SheepStatus.Ready) return Availability.AlreadyReady;
            if (sheep.Status == PrototypeSheepState.SheepStatus.Shearing) return Availability.Shearing;
            if (sheep.HasFeedBoost) return Availability.BoostActive;
            return economy.CanAfford(food.Cost) ? Availability.Available : Availability.NeedCoins;
        }
        public bool TryFeed(PrototypeSheepState sheep)
        {
            if (!IsConfigured || purchasing || sheep == null) return false;
            if (GetAvailability(sheep) == Availability.Unavailable) return false;
            sheep.AdvanceGrowth(Time.timeAsDouble, balance);
            if (GetAvailability(sheep) != Availability.Available) return false;
            purchasing = true;
            try
            {
                if (!economy.TrySpendCoins(food.Cost)) return false;
                // Unity purchase callbacks execute synchronously; the guard prevents duplicate payment.
                if (!sheep.TryApplyGrowthBoost(Time.timeAsDouble, food.Duration, food.Multiplier, balance))
                    throw new InvalidOperationException("Feed target changed during purchase.");
            }
            finally { purchasing = false; }
            Changed?.Invoke(); return true;
        }
    }
}
