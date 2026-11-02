using System;
using UnityEngine;

namespace ShearAndGrow.Shearing
{
    /// <summary>
    /// Android implementation backed by Assets/Plugins/Android/ShearHaptics.java. The Java class picks the best API
    /// per device (composition primitives / amplitude / predefined / on-off). Method IDs are cached once and calls use
    /// a reused jvalue buffer, so the hot path performs no managed allocations and no reflection.
    /// </summary>
    public sealed class AndroidHaptics : IHapticsPlatform
    {
        const int FlagSupported = 1, FlagAmplitude = 2, FlagComposition = 4, FlagPermission = 64;
        const string JavaClass = "com.shearandgrow.haptics.ShearHaptics";

        readonly bool _respectTouchSetting;
        int _flags;
        bool _supported;
        string _initError = "";
        IntPtr _class;
        IntPtr _midImpact, _midCompletion, _midStartTexture, _midStop, _midTouch;
        readonly jvalue[] _args3 = new jvalue[3];
        readonly jvalue[] _args2 = new jvalue[2];
        readonly jvalue[] _args0 = new jvalue[0];
        bool _textureMode;

        public AndroidHaptics(bool respectTouchFeedbackSetting) { _respectTouchSetting = respectTouchFeedbackSetting; }

        public string PlatformName => "Android";
        public bool IsSupported => _supported;
        /// <summary>Waveform texture only on amplitude devices WITHOUT primitives; primitive devices get crisp scheduled ticks.</summary>
        public bool HasNativeContinuous => _supported && (_flags & FlagAmplitude) != 0 && (_flags & FlagComposition) == 0;
        public bool ImpactInterruptsContinuous => true;

        public string Capabilities
        {
            get
            {
                if (_class == IntPtr.Zero) return "plugin not loaded";
                try { using (var c = new AndroidJavaClass(JavaClass)) return c.CallStatic<string>("info"); }
                catch (Exception e) { return "info failed: " + e.Message; }
            }
        }

        public string LastError
        {
            get
            {
                if (_initError.Length > 0) return _initError;
                if (_class == IntPtr.Zero) return "";
                try { using (var c = new AndroidJavaClass(JavaClass)) return c.CallStatic<string>("lastError"); }
                catch (Exception e) { return e.Message; }
            }
        }

        public int NativeCallCount
        {
            get
            {
                if (_class == IntPtr.Zero) return 0;
                try { using (var c = new AndroidJavaClass(JavaClass)) return c.CallStatic<int>("callCount"); }
                catch { return 0; }
            }
        }

        public void Initialize()
        {
            try
            {
                using (var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                using (var activity = player.GetStatic<AndroidJavaObject>("currentActivity"))
                using (var cls = new AndroidJavaClass(JavaClass))
                {
                    _flags = cls.CallStatic<int>("init", activity);
                    _class = AndroidJNI.NewGlobalRef(cls.GetRawClass());
                }
                _midImpact = AndroidJNIHelper.GetMethodID(_class, "impact", "(IFF)V", true);
                _midCompletion = AndroidJNIHelper.GetMethodID(_class, "completion", "(FFI)V", true);
                _midStartTexture = AndroidJNIHelper.GetMethodID(_class, "startTexture", "(FF)V", true);
                _midStop = AndroidJNIHelper.GetMethodID(_class, "stop", "()V", true);
                _midTouch = AndroidJNIHelper.GetMethodID(_class, "touchFeedbackEnabled", "()Z", true);
                _supported = (_flags & FlagSupported) != 0;
                if (!_supported) _initError = (_flags & FlagPermission) == 0 ? "VIBRATE permission missing" : "Device has no vibrator";
            }
            catch (Exception e)
            {
                _supported = false;
                _initError = "Android haptics init failed: " + e.Message;
            }
        }

        bool Allowed()
        {
            if (!_supported) return false;
            if (_respectTouchSetting && !AndroidJNI.CallStaticBooleanMethod(_class, _midTouch, _args0)) return false;
            return true;
        }

        public void PlayImpact(HapticImpact kind, float intensity, float sharpness)
        {
            if (!Allowed()) return;
            _textureMode = false;
            _args3[0].i = (int)kind;
            _args3[1].f = intensity;
            _args3[2].f = sharpness;
            AndroidJNI.CallStaticVoidMethod(_class, _midImpact, _args3);
        }

        public void PlayCompletion(float intensity, float sharpness, float gapSeconds)
        {
            if (!Allowed()) return;
            _textureMode = false;
            _args3[0].f = intensity;
            _args3[1].f = sharpness;
            _args3[2].i = Mathf.RoundToInt(gapSeconds * 1000f);
            AndroidJNI.CallStaticVoidMethod(_class, _midCompletion, _args3);
        }

        public void StartContinuous(float intensity, float sharpness)
        {
            if (!Allowed() || !HasNativeContinuous) return;
            _textureMode = true;
            _args2[0].f = intensity;
            _args2[1].f = sharpness;
            AndroidJNI.CallStaticVoidMethod(_class, _midStartTexture, _args2);
        }

        // Java side ignores tiny changes; restart only when the amplitude bucket really changes.
        public void UpdateContinuous(float intensity, float sharpness)
        {
            if (!_textureMode) return;
            StartContinuous(intensity, sharpness);
        }

        public void StopContinuous()
        {
            if (!_textureMode) return;
            _textureMode = false;
            StopNative();
        }

        public void StopAll()
        {
            _textureMode = false;
            StopNative();
        }

        void StopNative()
        {
            if (!_supported || _class == IntPtr.Zero) return;
            AndroidJNI.CallStaticVoidMethod(_class, _midStop, _args0);
        }

        public void OnApplicationPause(bool paused) { if (paused) StopAll(); }

        public void Dispose()
        {
            StopAll();
            if (_class != IntPtr.Zero) { AndroidJNI.DeleteGlobalRef(_class); _class = IntPtr.Zero; }
            _supported = false;
        }
    }
}
