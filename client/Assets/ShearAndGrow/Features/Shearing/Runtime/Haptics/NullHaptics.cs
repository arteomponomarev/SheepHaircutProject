namespace ShearAndGrow.Shearing
{
    /// <summary>
    /// Safe no-op platform for the Editor, desktop and unsupported devices. In the Editor it records every command so
    /// the haptic timeline can be verified without a phone (counts are shown in the debug panel / HUD).
    /// </summary>
    public sealed class NullHaptics : IHapticsPlatform
    {
        readonly string _reason;
        public NullHaptics(string reason, bool simulateSupport)
        {
            _reason = reason;
            IsSupported = simulateSupport;
        }

        public string PlatformName => IsSupported ? "Editor (simulated)" : "None";
        public bool IsSupported { get; }
        public bool HasNativeContinuous => true;
        public bool ImpactInterruptsContinuous => false;
        public string Capabilities => _reason;
        public string LastError => "";
        public int NativeCallCount { get; private set; }

        // Recorded timeline (Editor verification)
        public int ImpactCount;
        public int ContactCount;
        public int ChunkCount;
        public int TextureCount;
        public int CompletionCount;
        public int ContinuousStarts;
        public int ContinuousStops;
        public int ContinuousUpdates;
        public int StopAllCount;
        public bool ContinuousActive;
        public float LastIntensity;

        public void Initialize() { }

        public void PlayImpact(HapticImpact kind, float intensity, float sharpness)
        {
            if (!IsSupported) return;
            NativeCallCount++; ImpactCount++; LastIntensity = intensity;
            if (kind == HapticImpact.Contact) ContactCount++;
            else if (kind == HapticImpact.Chunk) ChunkCount++;
            else TextureCount++;
        }

        public void PlayCompletion(float intensity, float sharpness, float gapSeconds)
        {
            if (!IsSupported) return;
            NativeCallCount++; CompletionCount++; LastIntensity = intensity;
        }

        public void StartContinuous(float intensity, float sharpness)
        {
            if (!IsSupported) return;
            NativeCallCount++; ContinuousStarts++; ContinuousActive = true; LastIntensity = intensity;
        }

        public void UpdateContinuous(float intensity, float sharpness)
        {
            if (!IsSupported || !ContinuousActive) return;
            NativeCallCount++; ContinuousUpdates++; LastIntensity = intensity;
        }

        public void StopContinuous()
        {
            if (!ContinuousActive) return;
            ContinuousActive = false; ContinuousStops++;
        }

        public void StopAll()
        {
            StopAllCount++;
            if (ContinuousActive) { ContinuousActive = false; ContinuousStops++; }
        }

        public void OnApplicationPause(bool paused) { if (paused) StopAll(); }
        public void Dispose() { StopAll(); }
    }

    public static class HapticsPlatformFactory
    {
        public static IHapticsPlatform Create(HapticsConfig config)
        {
            IHapticsPlatform platform;
#if UNITY_EDITOR
            platform = new NullHaptics("Editor: commands are simulated and counted, no device motor", true);
#elif UNITY_IOS
            platform = new IOSHaptics();
#elif UNITY_ANDROID
            platform = new AndroidHaptics(config != null && config.RespectAndroidTouchFeedbackSetting);
#else
            platform = new NullHaptics("Platform has no haptics backend", false);
#endif
            platform.Initialize();
            return platform;
        }

#if UNITY_ANDROID
        // Never executed. Referencing Handheld.Vibrate makes Unity add android.permission.VIBRATE to the merged
        // manifest (required by Vibrator APIs) without replacing the project's generated AndroidManifest.
        internal static void EnsureVibratePermissionIsDeclared() { if (UnityEngine.Time.frameCount < 0) UnityEngine.Handheld.Vibrate(); }
#endif
    }
}
