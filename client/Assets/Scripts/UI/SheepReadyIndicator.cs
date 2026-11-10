using TMPro;
using UnityEngine;

namespace ShearAndGrow
{
    /// <summary>World-space readiness label only; eligibility belongs to sheep state.</summary>
    public sealed class SheepReadyIndicator : MonoBehaviour
    {
        [SerializeField] private PrototypeSheepState sheep;
        [SerializeField] private Camera viewCamera;
        [SerializeField] private TextMeshPro label;
        public void Bind(PrototypeSheepState state)
        {
            if (sheep != null) sheep.Changed -= Refresh;
            sheep = state;
            if (isActiveAndEnabled && sheep != null) { sheep.Changed += Refresh; Refresh(); }
        }

        private void OnEnable()
        {
            if (sheep == null || viewCamera == null || label == null)
            { Debug.LogError("Ready indicator requires state, camera and label references.", this); enabled = false; return; }
            sheep.Changed += Refresh; Refresh();
        }
        private void Refresh() => label.gameObject.SetActive(sheep.Status == PrototypeSheepState.SheepStatus.Ready);
        private void LateUpdate() => label.transform.rotation = viewCamera.transform.rotation;
        private void OnDisable() { if (sheep != null) sheep.Changed -= Refresh; }
    }
}
