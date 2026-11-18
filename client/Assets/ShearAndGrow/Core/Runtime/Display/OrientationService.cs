using System;
using UnityEngine;

namespace ShearAndGrow.Economy
{
    public enum OrientationMode { Portrait = 0, Landscape = 1, Auto = 2 }

    /// <summary>
    /// Player setting for screen orientation. Persisted in PlayerPrefs (display preference, not game progress).
    /// Default (first launch / missing key) = Portrait, i.e. the behaviour the game always had.
    ///  • Portrait: portrait only (upside-down excluded – not supported on modern iPhones).
    ///  • Landscape: landscape left + right (the player may flip the phone 180°).
    ///  • Auto: follows the device (portrait + both landscapes).
    /// Requires Player Settings "Default Orientation = Auto Rotation" so iOS lists every orientation in Info.plist and
    /// Android uses a sensor-based activity orientation; this service then restricts it at runtime before the first
    /// scene loads. In the Editor Screen.orientation has no effect – the Game view size decides (layouts still adapt).
    /// </summary>
    public static class OrientationService
    {
        public const string PrefKey = "ShearAndGrow.Settings.Orientation";
        public const OrientationMode DefaultMode = OrientationMode.Portrait;

        static OrientationMode _mode = DefaultMode;
        static bool _initialised;

        public static OrientationMode Mode { get { Init(); return _mode; } }
        public static event Action<OrientationMode> ModeChanged;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Bootstrap()
        {
            Init();
            Apply(_mode, true);
            ScreenLayout.EnsureDriver();
        }

        static void Init()
        {
            if (_initialised) return;
            _initialised = true;
            int v = PlayerPrefs.GetInt(PrefKey, (int)DefaultMode);
            _mode = Enum.IsDefined(typeof(OrientationMode), v) ? (OrientationMode)v : DefaultMode;
        }

        public static void SetMode(OrientationMode mode)
        {
            Init();
            if (!Enum.IsDefined(typeof(OrientationMode), mode)) return;
            bool changed = mode != _mode;
            _mode = mode;
            PlayerPrefs.SetInt(PrefKey, (int)mode);
            PlayerPrefs.Save();
            Apply(mode, false);
            if (changed) ModeChanged?.Invoke(mode);
        }

        /// <summary>Removes the stored preference (Developer Panel → Reset UI Preferences). Back to the default.</summary>
        public static void ResetToDefault()
        {
            PlayerPrefs.DeleteKey(PrefKey);
            _initialised = false;
            Init();
            Apply(_mode, false);
            ModeChanged?.Invoke(_mode);
        }

        static void Apply(OrientationMode mode, bool startup)
        {
            switch (mode)
            {
                case OrientationMode.Portrait:
                    Screen.autorotateToPortrait = true;
                    Screen.autorotateToPortraitUpsideDown = false;
                    Screen.autorotateToLandscapeLeft = false;
                    Screen.autorotateToLandscapeRight = false;
                    Screen.orientation = ScreenOrientation.Portrait;
                    break;
                case OrientationMode.Landscape:
                    Screen.autorotateToPortrait = false;
                    Screen.autorotateToPortraitUpsideDown = false;
                    Screen.autorotateToLandscapeLeft = true;
                    Screen.autorotateToLandscapeRight = true;
                    // force the rotation now (device may be held upright), then let the sensor pick left / right
                    Screen.orientation = ScreenOrientation.LandscapeLeft;
                    ScreenLayout.RequestAutoRotationNextFrame();
                    break;
                default:
                    Screen.autorotateToPortrait = true;
                    Screen.autorotateToPortraitUpsideDown = false;
                    Screen.autorotateToLandscapeLeft = true;
                    Screen.autorotateToLandscapeRight = true;
                    Screen.orientation = ScreenOrientation.AutoRotation;
                    break;
            }
        }

        public static string Label(OrientationMode m) => m == OrientationMode.Portrait ? "Portrait" : m == OrientationMode.Landscape ? "Landscape" : "Auto";
    }

    /// <summary>
    /// Screen geometry change notifications (size, safe area). Orientation changes, foldables and Editor Game view
    /// resizes all arrive here once per change, so UI and cameras re-layout event-driven instead of every frame.
    /// </summary>
    public static class ScreenLayout
    {
        static Vector2Int _size;
        static Rect _safe;
        static bool _pendingAutoRotation;

        /// <summary>Raised after the screen size or safe area changed (main thread, before other Updates).</summary>
        public static event Action Changed;
        public static int Version { get; private set; }

        public static bool IsLandscape => Screen.width > Screen.height;
        public static float Aspect => Screen.height > 0 ? Screen.width / (float)Screen.height : 1f;

        internal static void RequestAutoRotationNextFrame() => _pendingAutoRotation = true;

        public static void EnsureDriver()
        {
            if (!Application.isPlaying || Driver.Instance != null) return;
            var go = new GameObject("[ScreenLayout]");
            UnityEngine.Object.DontDestroyOnLoad(go);
            go.hideFlags = HideFlags.HideInHierarchy;
            Driver.Instance = go.AddComponent<Driver>();
            _size = new Vector2Int(Screen.width, Screen.height);
            _safe = Screen.safeArea;
        }

        static void Poll()
        {
            if (_pendingAutoRotation)
            {
                _pendingAutoRotation = false;
                if (OrientationService.Mode == OrientationMode.Landscape) Screen.orientation = ScreenOrientation.AutoRotation;
            }
            var size = new Vector2Int(Screen.width, Screen.height);
            var safe = Screen.safeArea;
            if (size == _size && safe == _safe) return;
            _size = size; _safe = safe;
            Version++;
            Changed?.Invoke();
        }

        [DefaultExecutionOrder(-1000)]
        sealed class Driver : MonoBehaviour
        {
            public static Driver Instance;
            void Update() => Poll();
            void OnDestroy() { if (Instance == this) Instance = null; }
        }
    }
}
