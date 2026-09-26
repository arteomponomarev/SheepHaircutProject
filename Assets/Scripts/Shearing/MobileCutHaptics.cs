using UnityEngine;

namespace ShearAndGrow
{
    /// <summary>Rate-limited Android ticks; intentionally silent on Editor/unsupported platforms.</summary>
    public sealed class MobileCutHaptics : MonoBehaviour
    {
        [SerializeField] private ShearingFeedbackSignal signal;
        [SerializeField] private ShearingFeedbackSettings settings;
        private float nextPulseAt;
#if UNITY_ANDROID && !UNITY_EDITOR
        private AndroidJavaObject activity;
        private AndroidJavaObject view;
        private object[] dispatchArgs;
        private readonly object[] tickArgs = { 4 }; // HapticFeedbackConstants.CLOCK_TICK (API 21+).
        private readonly object nativeLock = new object();
        private volatile bool pulsePending;
#endif
        private void OnEnable()
        {
            if (signal == null || settings == null)
            { Debug.LogError("MobileCutHaptics requires signal and settings.", this); enabled = false; return; }
#if UNITY_ANDROID && !UNITY_EDITOR
            if (activity == null)
            {
                try
                {
                    using var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
                    activity = player.GetStatic<AndroidJavaObject>("currentActivity");
                    using var window = activity.Call<AndroidJavaObject>("getWindow");
                    view = window.Call<AndroidJavaObject>("getDecorView");
                    dispatchArgs = new object[] { new AndroidJavaRunnable(PerformTick) };
                }
                catch (AndroidJavaException) { Debug.LogWarning("Android haptic view unavailable; continuing without haptics.", this); }
            }
#endif
            signal.Cut += OnCut; signal.CuttingChanged += OnCuttingChanged; signal.Cleared += Cancel;
        }
        private void OnCut(Vector3 point, Vector3 normal)
        {
            if (!settings.HapticsEnabled || !signal.IsCutting || Time.unscaledTime < nextPulseAt) return;
            nextPulseAt = Time.unscaledTime + settings.HapticInterval;
#if UNITY_ANDROID && !UNITY_EDITOR
            if (activity == null || view == null || dispatchArgs == null) return;
            pulsePending = true;
            activity.Call("runOnUiThread", dispatchArgs);
#endif
        }
#if UNITY_ANDROID && !UNITY_EDITOR
        private void PerformTick()
        {
            lock (nativeLock)
            {
                if (!pulsePending || view == null) return;
                pulsePending = false;
                // Respects Android's touch-feedback preference; no VIBRATE permission required.
                // https://developer.android.com/develop/ui/views/haptics/haptic-feedback
                view.Call<bool>("performHapticFeedback", tickArgs);
            }
        }
#endif
        private void OnCuttingChanged(bool cutting) { if (!cutting) Cancel(); }
        private void Cancel()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            pulsePending = false;
#endif
        }
        private void OnDisable()
        {
            if (signal != null) { signal.Cut -= OnCut; signal.CuttingChanged -= OnCuttingChanged; signal.Cleared -= Cancel; }
            Cancel();
        }
        private void OnDestroy()
        {
            Cancel();
#if UNITY_ANDROID && !UNITY_EDITOR
            lock (nativeLock) { view?.Dispose(); activity?.Dispose(); view = null; activity = null; }
#endif
        }
    }
}
