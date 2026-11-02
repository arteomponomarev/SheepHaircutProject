using UnityEngine;

namespace ShearAndGrow.Economy
{
    /// <summary>Processing stage of wool. Order matters: index into price multipliers.</summary>
    public enum WoolType { Dirty = 0, Wet = 1, Clean = 2 }

    /// <summary>
    /// Tunable economy balance (temporary test values). Read-only at runtime; also defines the DEFAULT GAME STATE of
    /// the economy for a brand-new player (starting coins / wool).
    /// </summary>
    [CreateAssetMenu(menuName = "Shear & Grow/Economy/Game Balance Data", fileName = "GameBalanceData")]
    public sealed class GameBalanceData : ScriptableObject
    {
        [Header("Shearing yield")]
        [Tooltip("Dirty Wool units for a sheep sheared to 100% unique area. Partial shearing gives a proportional amount, credited once.")]
        [Min(1)] public int WoolPerFullSheep = 100;

        [Header("Selling")]
        [Tooltip("Coins per unit of wool before the stage multiplier.")]
        [Min(0f)] public float BaseWoolPrice = 1f;
        [Tooltip("Price multipliers for Dirty, Wet, Clean.")]
        public float[] PriceMultiplier = { 1.0f, 1.2f, 1.8f };

        [Header("New player (default game state)")]
        [Min(0)] public long StartingCoins = 0;
        [Min(0)] public int StartingDirtyWool = 0;
        [Min(0)] public int StartingWetWool = 0;
        [Min(0)] public int StartingCleanWool = 0;

        [Header("Save compatibility")]
        [Tooltip("Bump ONLY to deliberately invalidate all economy saves of older builds. Normal updates keep this value.")]
        [Min(0)] public int EconomySaveEpoch = 0;

        public float Multiplier(WoolType type)
        {
            int i = (int)type;
            return PriceMultiplier != null && i < PriceMultiplier.Length ? PriceMultiplier[i] : 1f;
        }

        public float UnitPrice(WoolType type) => BaseWoolPrice * Multiplier(type);

        /// <summary>Coins for selling <paramref name="amount"/> units (rounded down, deterministic).</summary>
        public long SellValue(WoolType type, int amount)
        {
            if (amount <= 0) return 0;
            return (long)System.Math.Floor(amount * (double)UnitPrice(type) + 1e-6);
        }
    }
}
