using UnityEngine;

namespace ShearAndGrow
{
    /// <summary>Supplies active game time to an explicitly assigned sheep or roster. No offline growth.</summary>
    [DisallowMultipleComponent]
    public sealed class SheepWoolGrowthClock : MonoBehaviour
    {
        [SerializeField] private PrototypeSheepState sheep;
        [SerializeField] private GameBalanceData balance;
        [SerializeField] private SheepRosterState roster;

        private void OnEnable()
        {
            if (balance != null && (roster != null ? roster.IsConfigured : sheep != null)) return;
            Debug.LogError("Wool growth clock requires sheep state and balance references.", this);
            enabled = false;
        }

        private void Update()
        {
            if (roster == null) { sheep.AdvanceGrowth(Time.timeAsDouble, balance); return; }
            int count = roster.Count;
            for (int i = 0; i < count; i++) roster.GetSheep(i).AdvanceGrowth(Time.timeAsDouble, balance);
        }
    }
}
