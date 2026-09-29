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
        public bool HasResult { get; private set; }
        public bool EntryBlocked { get; private set; }
        public bool CanFinish => ownsSession && !HasResult && progress != null && progress.IsComplete;
        public float ResultCoverage { get; private set; }
        public float ResultWool { get; private set; }
        public event Action Changed;
        /// <summary>Raised once, after the completion snapshot is finalized. No inventory dependency.</summary>
        public event Action Completed;
        private bool ownsSession;
        private bool started;

        /// <summary>Scene bindings may assign a sheep before Start; the active session cannot switch sheep.</summary>
        public bool AssignSheep(PrototypeSheepState selected)
        {
            if (started || selected == null) return false;
            sheep = selected; return true;
        }

        private void Start()
        {
            started = true;
            if (sheep == null || progress == null || input == null || gestures == null || mask == null)
            { Debug.LogError("ShearingSession requires explicit state, progress and interaction references.", this); enabled = false; return; }
            progress.Changed += Refresh;
            ownsSession = sheep.TryBeginShearing();
            EntryBlocked = !ownsSession;
            if (EntryBlocked) StopInteraction();
            Changed?.Invoke();
        }
        public void Finish()
        {
            if (!CanFinish) return;
            // Set the durable in-run status before any UI can submit Finish again.
            if (!sheep.TryCompleteShearing(progress.Sheared01, progress.WoolEarned)) return;
            ResultCoverage = sheep.ResultCoverage; ResultWool = sheep.ResultWool;
            HasResult = true; ownsSession = false;
            StopInteraction(); Completed?.Invoke(); Changed?.Invoke();
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
