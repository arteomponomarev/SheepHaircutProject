using UnityEngine;

namespace ShearAndGrow
{
    [CreateAssetMenu(fileName = "GameBalanceData", menuName = "Shear and Grow/Game Balance")]
    public sealed class GameBalanceData : ScriptableObject
    {
        [Tooltip("Fraction of unique shearable area required to mark the shearing session complete.")]
        [SerializeField, Range(0.01f, 1f)] private float completionThreshold = 0.95f;

        [Tooltip("Active game seconds from bare to fully grown wool. No offline growth.")]
        [SerializeField, Min(1f)] private float woolGrowthSeconds = 90f;
        [Tooltip("Growth required to enter shearing. Stage 7 uses fully grown wool (1).")]
        [SerializeField, Range(0.01f, 1f)] private float readyToShearThreshold = 1f;
        [Tooltip("Coins received per unit of Wool. Fractional Wool is retained when selling.")]
        [SerializeField, Min(1)] private int woolUnitPrice = 1;
        [SerializeField, Range(1, 2)] private int initialSheepCount = 1;
        [SerializeField, Min(1)] private int firstSheepPrice = 150;
        [SerializeField, Min(1)] private int sheepPriceIncrease = 100;

        public float CompletionThreshold => completionThreshold;
        public float WoolGrowthSeconds => woolGrowthSeconds;
        public float ReadyToShearThreshold => readyToShearThreshold;
        public int WoolUnitPrice => woolUnitPrice;
        public int InitialSheepCount => Mathf.Clamp(initialSheepCount, 1, 2);
        public decimal SheepPurchasePrice(int alreadyPurchased) => Mathf.Max(1, firstSheepPrice)
            + (decimal)Mathf.Max(0, alreadyPurchased) * Mathf.Max(1, sheepPriceIncrease);

        private void OnValidate()
        {
            completionThreshold = Mathf.Clamp(completionThreshold, 0.01f, 1f);
            woolGrowthSeconds = float.IsNaN(woolGrowthSeconds) || float.IsInfinity(woolGrowthSeconds)
                ? 90f : Mathf.Max(1f, woolGrowthSeconds);
            readyToShearThreshold = float.IsNaN(readyToShearThreshold) ? 1f : Mathf.Clamp(readyToShearThreshold, 0.01f, 1f);
            woolUnitPrice = Mathf.Max(1, woolUnitPrice);
            initialSheepCount = Mathf.Clamp(initialSheepCount, 1, 2);
            firstSheepPrice = Mathf.Max(1, firstSheepPrice);
            sheepPriceIncrease = Mathf.Max(1, sheepPriceIncrease);
        }
    }
}
