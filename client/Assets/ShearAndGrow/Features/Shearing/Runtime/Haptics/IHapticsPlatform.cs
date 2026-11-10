namespace ShearAndGrow.Shearing
{
    /// <summary>Discrete accent kinds. Platforms map them to their best native primitive.</summary>
    public enum HapticImpact
    {
        /// <summary>Light grain pulse of the cutting texture.</summary>
        Texture = 0,
        /// <summary>Blade meets dense wool (start of a real new cut).</summary>
        Contact = 1,
        /// <summary>A large clump tears off.</summary>
        Chunk = 2,
    }

    /// <summary>
    /// Thin native layer. All values are normalised 0..1; perception differs per platform, so per-platform scaling lives
    /// in <see cref="HapticProfile"/>, not here. Implementations must be cheap to call (no per-call native allocations)
    /// and must never throw into gameplay code.
    /// </summary>
    public interface IHapticsPlatform
    {
        string PlatformName { get; }
        bool IsSupported { get; }
        /// <summary>True if the platform can play a smooth, dynamically controllable continuous effect.</summary>
        bool HasNativeContinuous { get; }
        /// <summary>True if playing an impact cancels the running continuous effect (Android single-vibrator semantics).</summary>
        bool ImpactInterruptsContinuous { get; }
        /// <summary>Human-readable capability / API-path summary for the debug panel.</summary>
        string Capabilities { get; }
        /// <summary>Last native error (empty if none).</summary>
        string LastError { get; }
        /// <summary>Number of native calls issued (diagnostics / budget verification).</summary>
        int NativeCallCount { get; }

        void Initialize();
        void PlayImpact(HapticImpact kind, float intensity, float sharpness);
        /// <summary>Two distinct taps separated by <paramref name="gapSeconds"/>.</summary>
        void PlayCompletion(float intensity, float sharpness, float gapSeconds);
        void StartContinuous(float intensity, float sharpness);
        void UpdateContinuous(float intensity, float sharpness);
        void StopContinuous();
        /// <summary>Stop everything immediately (release, focus loss, disable).</summary>
        void StopAll();
        void OnApplicationPause(bool paused);
        void Dispose();
    }
}
