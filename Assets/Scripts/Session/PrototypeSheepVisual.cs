using UnityEngine;

namespace ShearAndGrow
{
    /// <summary>Farm sheep presentation only; reads state and never changes it.</summary>
    public sealed class PrototypeSheepVisual : MonoBehaviour
    {
        [SerializeField] private PrototypeSheepState sheep;
        [SerializeField] private GameObject woolVisual;
        private void OnEnable()
        {
            if (sheep == null || woolVisual == null)
            { Debug.LogError("Sheep visual requires state and wool references.", this); enabled = false; return; }
            sheep.Changed += Refresh; Refresh();
        }
        private void Refresh() => woolVisual.SetActive(sheep.Status != PrototypeSheepState.SheepStatus.RecentlySheared);
        private void OnDisable() { if (sheep != null) sheep.Changed -= Refresh; }
    }
}
