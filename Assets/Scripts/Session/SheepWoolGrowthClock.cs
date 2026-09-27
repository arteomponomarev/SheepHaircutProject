using UnityEngine;

namespace ShearAndGrow
{
    /// <summary>Supplies active game time to one explicitly assigned sheep. No offline growth.</summary>
    [DisallowMultipleComponent]
    public sealed class SheepWoolGrowthClock : MonoBehaviour
    {
        [SerializeField] private PrototypeSheepState sheep;
        [SerializeField] private GameBalanceData balance;

        private void OnEnable()
        {
            if (sheep != null && balance != null) return;
            Debug.LogError("Wool growth clock requires sheep state and balance references.", this);
            enabled = false;
        }

        private void Update() => sheep.AdvanceGrowth(Time.timeAsDouble, balance);
    }
}
