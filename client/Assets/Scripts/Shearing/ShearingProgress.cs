using System;
using UnityEngine;

namespace ShearAndGrow
{
    /// <summary>Per-sheep unique coverage and session yield. No inventory, input or UI rules.</summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(MeshFilter))]
    public sealed class ShearingProgress : MonoBehaviour
    {
        [SerializeField] private WoolMaskPainter maskPainter;
        [SerializeField] private SheepShearingData sheepData;
        [SerializeField] private GameBalanceData balance;
        [SerializeField, Range(32, 256)] private int gridResolution = 128;

        public float Sheared01 => grid == null ? 0f : grid.Removed01;
        public float ShearedPercent => Sheared01 * 100f;
        public float WoolRemaining01 => 1f - Sheared01;
        public float WoolEarned => Sheared01 * fullWoolAmount;
        public float FullWoolAmount => fullWoolAmount;
        public bool IsComplete { get; private set; }
        public event Action Changed;
        public event Action CompletionReached;
        /// <summary>A newly removed cell location and the new UV area fraction of this stamp.</summary>
        public event Action<Vector2, float> WoolRemoved;
        public event Action ResetOccurred;

        private WoolOccupancyGrid grid;
        private float fullWoolAmount;
        private float completionThreshold;
        private bool coverageChanged;

        private void Awake()
        {
            if (maskPainter == null || sheepData == null || balance == null)
            {
                Debug.LogError("ShearingProgress requires mask painter, sheep data and balance references.", this);
                enabled = false;
                return;
            }
            int resolution = Mathf.Clamp(gridResolution, 32, 256);
            try { grid = new WoolOccupancyGrid(resolution, WoolUvCoverage.Build(GetComponent<MeshFilter>().sharedMesh, resolution)); }
            catch (ArgumentException exception)
            { Debug.LogError(exception.Message, this); enabled = false; return; }
            fullWoolAmount = sheepData.WoolAmount;
            completionThreshold = balance.CompletionThreshold;
            // State observes for its whole lifetime: disabling the view/component must not lose cuts.
            maskPainter.BrushStamped += OnBrushStamped;
            maskPainter.StrokePainted += OnStrokePainted;
            maskPainter.MaskReset += OnMaskReset;
        }

        private void OnBrushStamped(Vector2 uv, float radius)
        {
            int added = grid.RemoveCircle(uv, radius, maskPainter.WrapU, out Vector2 firstRemovedUv);
            if (added == 0) return;
            coverageChanged = true;
            WoolRemoved?.Invoke(firstRemovedUv, (float)added / grid.TotalCells);
        }

        private void OnStrokePainted()
        {
            if (!coverageChanged) return;
            coverageChanged = false;
            bool justCompleted = !IsComplete && Sheared01 >= completionThreshold;
            if (justCompleted) IsComplete = true;
            Changed?.Invoke();
            if (justCompleted) CompletionReached?.Invoke();
        }

        private void OnMaskReset()
        {
            grid.Reset();
            coverageChanged = false;
            IsComplete = false;
            Changed?.Invoke();
            ResetOccurred?.Invoke();
        }

        private void OnDestroy()
        {
            if (maskPainter == null) return;
            maskPainter.BrushStamped -= OnBrushStamped;
            maskPainter.StrokePainted -= OnStrokePainted;
            maskPainter.MaskReset -= OnMaskReset;
        }
    }
}
