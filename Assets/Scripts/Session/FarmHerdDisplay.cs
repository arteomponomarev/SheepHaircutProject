using UnityEngine;

namespace ShearAndGrow
{
    /// <summary>Reuses a fixed set of sheep presentation slots. No purchases, selection rules or simulation.</summary>
    public sealed class FarmHerdDisplay : MonoBehaviour
    {
        [SerializeField] private PrototypeSheepVisual[] visuals;
        [SerializeField] private SheepReadyIndicator[] indicators;
        public int SlotCount => visuals == null ? 0 : visuals.Length;
        public void Show(int slot, PrototypeSheepState sheep)
        {
            if (slot < 0 || slot >= SlotCount) return;
            var visual = visuals[slot];
            if (sheep == null) { visual.gameObject.SetActive(false); return; }
            visual.Bind(sheep); indicators[slot].Bind(sheep); visual.gameObject.SetActive(true);
        }
    }
}
