using System;
using UnityEngine;

namespace ShearAndGrow
{
    /// <summary>Read-only feedback timing from actual cuts and pointer contact. No reward or rendering rules.</summary>
    [DefaultExecutionOrder(100)]
    public sealed class ShearingFeedbackSignal : MonoBehaviour
    {
        [SerializeField] private ShearingProgress progress;
        [SerializeField] private TrimmerPointerDriver driver;
        [SerializeField] private MeshCollider woolSurface;
        [SerializeField] private ShearingFeedbackSettings settings;
        public bool IsCutting { get; private set; }
        public event Action<bool> CuttingChanged;
        public event Action<Vector3, Vector3> Cut;
        public event Action Cleared;
        private WoolSurfaceLookup lookup;
        private Vector2 pendingUv;
        private bool pending;
        private float lastCutTime = float.NegativeInfinity;

        private void OnEnable()
        {
            if (progress == null || driver == null || woolSurface == null || settings == null)
            { Debug.LogError("ShearingFeedbackSignal requires progress, driver, surface and settings.", this); enabled = false; return; }
            try { lookup ??= new WoolSurfaceLookup(woolSurface); }
            catch (ArgumentException e) { Debug.LogError(e.Message, this); enabled = false; return; }
            progress.WoolRemoved += OnWoolRemoved;
            progress.ResetOccurred += Clear;
            driver.ContactEnded += Stop;
        }

        private void OnWoolRemoved(Vector2 uv, float area)
        {
            if (!driver.HasContact || area <= 0) return;
            pendingUv = uv; pending = true;
        }

        private void LateUpdate()
        {
            if (!driver.HasContact) { Stop(); return; }
            bool hasCut = pending;
            pending = false;
            if (hasCut) lastCutTime = Time.unscaledTime;
            SetCutting(Time.unscaledTime - lastCutTime <= settings.CutHoldSeconds);
            if (hasCut && lookup.TryGetPoint(pendingUv, out Vector3 point, out Vector3 normal))
                Cut?.Invoke(point, normal);
        }

        private void SetCutting(bool cutting)
        {
            if (IsCutting == cutting) return;
            IsCutting = cutting;
            CuttingChanged?.Invoke(cutting);
        }
        private void Stop() { pending = false; lastCutTime = float.NegativeInfinity; SetCutting(false); }
        private void Clear() { Stop(); Cleared?.Invoke(); }
        private void OnApplicationFocus(bool focused) { if (!focused) Clear(); }
        private void OnApplicationPause(bool paused) { if (paused) Clear(); }
        private void OnDisable()
        {
            if (progress != null) { progress.WoolRemoved -= OnWoolRemoved; progress.ResetOccurred -= Clear; }
            if (driver != null) driver.ContactEnded -= Stop;
            Clear();
        }
    }
}
