using System;
using System.IO;
using UnityEngine;

namespace ShearAndGrow.Economy
{
    public enum WoolGrowthTimeMode
    {
        /// <summary>Wool only grows while the game runs (any scene). Default.</summary>
        GameTime = 0,
        /// <summary>Wool also grows while the app is closed (UTC wall clock via <see cref="GameClock"/>). Offline regrowth.</summary>
        RealTime = 1,
    }

    /// <summary>
    /// Time source for sheep wool regrowth, independent of scenes. GameTime = persistent counter of played seconds
    /// (accumulated by <see cref="WoolGrowthClockDriver"/>, saved atomically every few seconds and on pause/quit);
    /// RealTime = UTC seconds. Sheep saves store a stamp in the same unit and grow by (Now − stamp).
    /// </summary>
    public static class WoolGrowthClock
    {
        [Serializable] sealed class Data { public int schema = 1; public double gameSeconds; public string lastSaveUtc; }

        static double _gameSeconds;
        static bool _loaded;
        static double _lastSavedAt = -1;

        /// <summary>Overrides the save folder (tests). Null = default.</summary>
        public static string RootOverride;
        static string Root => RootOverride ?? Path.Combine(Application.persistentDataPath, "ShearAndGrow", Application.isEditor ? "EditorSaves" : "Saves");
        static string FilePath => Path.Combine(Root, "clock.json");

        public static double GameSeconds { get { EnsureLoaded(); return _gameSeconds; } }

        static readonly DateTime RealTimeEpoch = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        /// <summary>
        /// Current growth clock value. RealTime = seconds since 2025-01-01 UTC from <see cref="GameClock"/> (monotonic,
        /// rollback-protected, dev test timeline aware; swap the GameClock source for trusted server time later).
        /// </summary>
        public static double Now(WoolGrowthTimeMode mode) =>
            mode == WoolGrowthTimeMode.RealTime ? (GameClock.UtcNow - RealTimeEpoch).TotalSeconds : GameSeconds;

        /// <summary>Converts a RealTime clock value back to UTC (diagnostics).</summary>
        public static DateTime RealTimeToUtc(double clockSeconds) => RealTimeEpoch.AddSeconds(clockSeconds);

        static void EnsureLoaded()
        {
            if (_loaded) return;
            _loaded = true;
            _gameSeconds = 0;
            foreach (var p in new[] { FilePath, FilePath + ".bak" })
            {
                try
                {
                    if (!File.Exists(p)) continue;
                    var d = JsonUtility.FromJson<Data>(File.ReadAllText(p));
                    if (d != null && d.gameSeconds >= 0) { _gameSeconds = d.gameSeconds; break; }
                }
                catch (Exception e) { Debug.LogWarning("[Growth] Clock unreadable (" + p + "): " + e.Message); }
            }
            _lastSavedAt = _gameSeconds;
        }

        /// <summary>Advances game time (called by the driver with unscaled delta time).</summary>
        public static void Advance(double seconds)
        {
            if (seconds <= 0 || double.IsNaN(seconds)) return;
            EnsureLoaded();
            _gameSeconds += Math.Min(seconds, 1.0); // a hitch never jumps the clock
        }

        public static void SaveIfDue(double intervalSeconds)
        {
            EnsureLoaded();
            if (_gameSeconds - _lastSavedAt >= intervalSeconds) Save();
        }

        public static void Save()
        {
            EnsureLoaded();
            try
            {
                Directory.CreateDirectory(Root);
                string tmp = FilePath + ".tmp";
                File.WriteAllText(tmp, JsonUtility.ToJson(new Data { gameSeconds = _gameSeconds, lastSaveUtc = DateTime.UtcNow.ToString("o") }));
                if (File.Exists(FilePath)) File.Replace(tmp, FilePath, FilePath + ".bak");
                else File.Move(tmp, FilePath);
                _lastSavedAt = _gameSeconds;
            }
            catch (Exception e) { Debug.LogWarning("[Growth] Clock save failed: " + e.Message); }
        }

        /// <summary>Tests / dev reset: forget the in-memory value (next access reloads from disk).</summary>
        public static void ResetForTests() { _loaded = false; _gameSeconds = 0; _lastSavedAt = -1; }

        #if UNITY_EDITOR || UNITY_ENABLE_CHECKS
                /// <summary>Developer time advance for GameTime mode (no 1 s hitch clamp). Dev builds only.</summary>
                public static void DevAdvanceGameSeconds(double seconds)
                {
                    if (!(seconds > 0) || double.IsInfinity(seconds)) return;
                    EnsureLoaded();
                    _gameSeconds += seconds;
                    Save();
                }
        #endif

                /// <summary>Tests: set game time directly.</summary>
        public static void SetGameSecondsForTests(double s) { EnsureLoaded(); _gameSeconds = s; }

        public static int DeleteAll()
        {
            int n = 0;
            foreach (var p in new[] { FilePath, FilePath + ".bak", FilePath + ".tmp" }) if (File.Exists(p)) { File.Delete(p); n++; }
            ResetForTests();
            return n;
        }
    }
}
