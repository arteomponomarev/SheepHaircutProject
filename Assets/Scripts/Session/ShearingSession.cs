using System;
using UnityEngine;

namespace ShearAndGrow
{
    /// <summary>Owns session boundaries, threshold gating and one immutable completion snapshot.</summary>
    public sealed class ShearingSession : MonoBehaviour
    {
        [SerializeField] private PrototypeSheepState sheep;
        [SerializeField] private ShearingProgress progress;
        [SerializeField] private ShearingInputAdapter input;
        [SerializeField] private ShearingGestureRouter gestures;
        [SerializeField] private WoolMaskPainter mask;
        public bool HasResult => sheep != null && sheep.Status == PrototypeSheepState.SheepStatus.RecentlySheared;
        public bool CanFinish => ownsSession && !HasResult && progress != null && progress.IsComplete;
        public float ResultCoverage => sheep.ResultCoverage;
        public float ResultWool => sheep.ResultWool;
        public event Action Changed;
        private bool ownsSession;

        private void Start()
        {
            if (sheep == null || progress == null || input == null || gestures == null || mask == null)
            { Debug.LogError("ShearingSession requires explicit state, progress and interaction references.", this); enabled = false; return; }
            progress.Changed += Refresh;
            ownsSession = sheep.TryBeginShearing();
            if (!ownsSession) StopInteraction();
            Changed?.Invoke();
        }
        public void Finish()
        {
            if (!CanFinish) return;
            // Set the durable in-run status before any UI can submit Finish again.
            if (!sheep.TryCompleteShearing(progress.Sheared01, progress.WoolEarned)) return;
            StopInteraction(); Changed?.Invoke();
        }
        private void StopInteraction()
        {
            input.Cancel(); gestures.enabled = false; input.enabled = false; mask.enabled = false;
        }
        private void Refresh() => Changed?.Invoke();
        private void OnDestroy()
        {
            if (progress != null) progress.Changed -= Refresh;
            if (ownsSession && sheep != null) sheep.CancelUnfinishedShearing();
        }
    }
}
