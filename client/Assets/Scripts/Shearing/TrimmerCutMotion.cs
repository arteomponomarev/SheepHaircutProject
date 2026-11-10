using UnityEngine;

namespace ShearAndGrow
{
    /// <summary>Offsets only the model child. Never changes the raycast, surface pose or brush.</summary>
    [DefaultExecutionOrder(200)]
    public sealed class TrimmerCutMotion : MonoBehaviour
    {
        [SerializeField] private ShearingFeedbackSignal signal;
        [SerializeField] private ShearingFeedbackSettings settings;
        [SerializeField] private Transform visualRoot;
        private Vector3 restPosition;
        private Quaternion restRotation;
        private bool initialized;

        private void OnEnable()
        {
            if (signal == null || settings == null || visualRoot == null)
            { Debug.LogError("TrimmerCutMotion requires signal, settings and visual child.", this); enabled = false; return; }
            restPosition = visualRoot.localPosition; restRotation = visualRoot.localRotation; initialized = true;
            signal.CuttingChanged += OnCuttingChanged;
            signal.Cleared += Restore;
        }
        private void LateUpdate()
        {
            if (!signal.IsCutting) return;
            float wave = Mathf.Sin(Time.unscaledTime * settings.TrimmerFrequency * Mathf.PI * 2);
            visualRoot.localPosition = restPosition + new Vector3(wave, Mathf.Abs(wave) * 0.4f, 0) * settings.TrimmerAmplitude;
            visualRoot.localRotation = restRotation * Quaternion.Euler(0, 0, wave * settings.TrimmerTilt);
        }
        private void OnCuttingChanged(bool cutting) { if (!cutting) Restore(); }
        private void Restore()
        { if (initialized && visualRoot != null) { visualRoot.localPosition = restPosition; visualRoot.localRotation = restRotation; } }
        private void OnDisable()
        {
            if (signal != null) { signal.CuttingChanged -= OnCuttingChanged; signal.Cleared -= Restore; }
            Restore();
        }
    }
}
