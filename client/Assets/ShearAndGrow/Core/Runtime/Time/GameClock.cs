using System;
using UnityEngine;

namespace ShearAndGrow.Economy
{
    /// <summary>Where "now" comes from. Device UTC today; a trusted server clock (PlayFab GetTime) later.</summary>
    public interface ITimeSource
    {
        string Name { get; }
        /// <summary>True when the value cannot be manipulated by the player (server time).</summary>
        bool IsTrusted { get; }
        DateTime UtcNow { get; }
    }

    /// <summary>Device wall clock (UTC). NOT trusted: the player can move it forward.</summary>
    public sealed class DeviceUtcTimeSource : ITimeSource
    {
        public string Name => "Device UTC";
        public bool IsTrusted => false;
        public DateTime UtcNow => DateTime.UtcNow;
    }

    /// <summary>
    /// Single time authority for wall-clock based game systems (construction timers, offline wool regrowth).
    ///  • Source: pluggable <see cref="ITimeSource"/> (device UTC now; swap in a server source when the backend exists).
    ///  • Rollback protection: time never runs backwards — a persisted high-water mark means turning the device clock
    ///    back (and forward again) cannot replay the same period twice. Moving it forward is still possible with the
    ///    untrusted device source (documented MVP limitation; server time removes it).
    ///  • Developer time offset (Editor / Development builds only, compiled out of release builds): an isolated test
    ///    timeline that shifts "now" for this install only — never the device clock, never a server.
    /// </summary>
    public static class GameClock
    {
        const string HighWaterKey = "ShearAndGrow.Clock.HighWaterTicks";
        const double HighWaterSaveInterval = 30.0;

        static ITimeSource _source = new DeviceUtcTimeSource();
        static long _highWaterTicks = -1;
        static long _lastSavedHighWater;
        static float _lastSaveRealtime = -999f;

        public static ITimeSource Source => _source;

        /// <summary>Replaces the time source (e.g. a server clock after login). Null restores device UTC.</summary>
        public static void SetSource(ITimeSource source) => _source = source ?? new DeviceUtcTimeSource();

        /// <summary>Effective "now" used by game systems (source + dev offset, never earlier than any earlier value).</summary>
        public static DateTime UtcNow
        {
            get
            {
                EnsureLoaded();
                long ticks = _source.UtcNow.Ticks + DevOffsetTicks;
                if (ticks > _highWaterTicks) _highWaterTicks = ticks;
                else ticks = _highWaterTicks; // device clock moved back: hold time until it catches up
                return new DateTime(ticks, DateTimeKind.Utc);
            }
        }

        public static long UtcNowTicks => UtcNow.Ticks;

        /// <summary>Raw source time behind the high-water mark (diagnostics: &gt;0 means the device clock is behind).</summary>
        public static double RollbackHoldSeconds
        {
            get { EnsureLoaded(); return Math.Max(0.0, (_highWaterTicks - (_source.UtcNow.Ticks + DevOffsetTicks)) / (double)TimeSpan.TicksPerSecond); }
        }

        static void EnsureLoaded()
        {
            if (_highWaterTicks >= 0) return;
            _highWaterTicks = 0;
            string s = PlayerPrefs.GetString(HighWaterKey, "");
            if (long.TryParse(s, out long v) && v > 0) _highWaterTicks = v;
            // A stored mark far in the future (clock was wildly wrong once) must not freeze time forever.
            long limit = _source.UtcNow.Ticks + DevOffsetTicks + TimeSpan.TicksPerDay * 2;
            if (_highWaterTicks > limit) _highWaterTicks = limit;
            _lastSavedHighWater = _highWaterTicks;
        }

        /// <summary>Tests: forget the in-memory high-water mark (reloaded from PlayerPrefs on next use).</summary>
        public static void ResetForTests() { _highWaterTicks = -1; }

        /// <summary>Persists the high-water mark (called periodically and on pause / quit by the clock driver).</summary>
        public static void Save(bool force = false)
        {
            EnsureLoaded();
            _ = UtcNow;
            if (!force && (_highWaterTicks == _lastSavedHighWater || Time.realtimeSinceStartup - _lastSaveRealtime < HighWaterSaveInterval)) return;
            PlayerPrefs.SetString(HighWaterKey, _highWaterTicks.ToString());
            PlayerPrefs.Save();
            _lastSavedHighWater = _highWaterTicks;
            _lastSaveRealtime = Time.realtimeSinceStartup;
        }

        // ------------------------------------------------------------------ developer time offset

#if UNITY_EDITOR || UNITY_ENABLE_CHECKS
        const string DevOffsetKey = "ShearAndGrow.Dev.TimeOffsetSeconds";
        static double _devOffsetSeconds = double.NaN;

        /// <summary>Seconds the isolated test timeline is ahead of the source (dev builds only).</summary>
        public static double DevOffsetSeconds
        {
            get
            {
                if (double.IsNaN(_devOffsetSeconds))
                    _devOffsetSeconds = double.TryParse(PlayerPrefs.GetString(DevOffsetKey, "0"), System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out double d) && d > 0 ? d : 0.0;
                return _devOffsetSeconds;
            }
        }

        static long DevOffsetTicks => (long)(DevOffsetSeconds * TimeSpan.TicksPerSecond);

        /// <summary>Moves the local test timeline forward (never backwards). Persisted so saves stay consistent across restarts.</summary>
        public static void DevAdvance(double seconds)
        {
            if (!(seconds > 0) || double.IsInfinity(seconds)) return;
            _devOffsetSeconds = Math.Min(DevOffsetSeconds + seconds, 3650.0 * 86400.0);
            PlayerPrefs.SetString(DevOffsetKey, _devOffsetSeconds.ToString("R", System.Globalization.CultureInfo.InvariantCulture));
            Save(true);
        }

        /// <summary>
        /// Returns the test timeline to real time. Saves stamped in the "future" are then handled by the normal
        /// rollback rules (growth / timers never run backwards). The high-water mark is reset with it.
        /// </summary>
        public static void DevResetOffset()
        {
            _devOffsetSeconds = 0.0;
            PlayerPrefs.DeleteKey(DevOffsetKey);
            _highWaterTicks = _source.UtcNow.Ticks;
            Save(true);
        }
#else
        public static double DevOffsetSeconds => 0.0;
        const long DevOffsetTicks = 0L;
#endif
    }
}
