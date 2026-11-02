#if UNITY_IOS && !UNITY_EDITOR
using System.Runtime.InteropServices;
#endif

namespace ShearAndGrow.Shearing
{
    /// <summary>
    /// Core Haptics implementation (Assets/Plugins/iOS/ShearHaptics.mm). Statically linked, IL2CPP-safe
    /// [DllImport("__Internal")]. Native calls only happen on discrete events / rate-limited updates.
    /// </summary>
    public sealed class IOSHaptics : IHapticsPlatform
    {
#if UNITY_IOS && !UNITY_EDITOR
        [DllImport("__Internal")] static extern int ShearHaptics_Init();
        [DllImport("__Internal")] static extern void ShearHaptics_Transient(float intensity, float sharpness);
        [DllImport("__Internal")] static extern void ShearHaptics_Double(float intensity, float sharpness, float gap);
        [DllImport("__Internal")] static extern void ShearHaptics_StartContinuous(float intensity, float sharpness);
        [DllImport("__Internal")] static extern void ShearHaptics_UpdateContinuous(float intensity, float sharpness);
        [DllImport("__Internal")] static extern void ShearHaptics_StopContinuous();
        [DllImport("__Internal")] static extern void ShearHaptics_StopAll();
        [DllImport("__Internal")] static extern void ShearHaptics_SetPaused(int paused);
        [DllImport("__Internal")] static extern void ShearHaptics_Shutdown();
        [DllImport("__Internal")] static extern int ShearHaptics_CallCount();
        [DllImport("__Internal")] static extern string ShearHaptics_LastError();
        [DllImport("__Internal")] static extern string ShearHaptics_Capabilities();
#endif

        bool _supported;
        string _initError = "";

        public string PlatformName => "iOS (Core Haptics)";
        public bool IsSupported => _supported;
        public bool HasNativeContinuous => true;
        public bool ImpactInterruptsContinuous => false; // transients layer on top of the continuous player

        public string Capabilities
        {
            get
            {
#if UNITY_IOS && !UNITY_EDITOR
                try { return ShearHaptics_Capabilities(); } catch (System.Exception e) { return "native unavailable: " + e.Message; }
#else
                return "not running on iOS";
#endif
            }
        }

        public string LastError
        {
            get
            {
#if UNITY_IOS && !UNITY_EDITOR
                if (_initError.Length > 0) return _initError;
                try { return ShearHaptics_LastError(); } catch (System.Exception e) { return e.Message; }
#else
                return _initError;
#endif
            }
        }

        public int NativeCallCount
        {
            get
            {
#if UNITY_IOS && !UNITY_EDITOR
                return _supported ? ShearHaptics_CallCount() : 0;
#else
                return 0;
#endif
            }
        }

        public void Initialize()
        {
#if UNITY_IOS && !UNITY_EDITOR
            try { _supported = ShearHaptics_Init() == 1; }
            catch (System.Exception e)
            {
                // EntryPointNotFound = plugin not linked (wrong importer settings) -> fail safe, keep the game running.
                _supported = false;
                _initError = "Native plugin missing: " + e.GetType().Name + " " + e.Message;
            }
#else
            _supported = false;
            _initError = "IOSHaptics used outside an iOS player";
#endif
        }

        public void PlayImpact(HapticImpact kind, float intensity, float sharpness)
        {
#if UNITY_IOS && !UNITY_EDITOR
            if (!_supported) return;
            ShearHaptics_Transient(intensity, sharpness);
#endif
        }

        public void PlayCompletion(float intensity, float sharpness, float gapSeconds)
        {
#if UNITY_IOS && !UNITY_EDITOR
            if (!_supported) return;
            ShearHaptics_Double(intensity, sharpness, gapSeconds);
#endif
        }

        public void StartContinuous(float intensity, float sharpness)
        {
#if UNITY_IOS && !UNITY_EDITOR
            if (!_supported) return;
            ShearHaptics_StartContinuous(intensity, sharpness);
#endif
        }

        public void UpdateContinuous(float intensity, float sharpness)
        {
#if UNITY_IOS && !UNITY_EDITOR
            if (!_supported) return;
            ShearHaptics_UpdateContinuous(intensity, sharpness);
#endif
        }

        public void StopContinuous()
        {
#if UNITY_IOS && !UNITY_EDITOR
            if (!_supported) return;
            ShearHaptics_StopContinuous();
#endif
        }

        public void StopAll()
        {
#if UNITY_IOS && !UNITY_EDITOR
            if (!_supported) return;
            ShearHaptics_StopAll();
#endif
        }

        public void OnApplicationPause(bool paused)
        {
#if UNITY_IOS && !UNITY_EDITOR
            if (!_supported) return;
            ShearHaptics_SetPaused(paused ? 1 : 0);
#endif
        }

        public void Dispose()
        {
#if UNITY_IOS && !UNITY_EDITOR
            if (!_supported) return;
            ShearHaptics_Shutdown();
            _supported = false;
#endif
        }
    }
}
