using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace ShearAndGrow.DevTools
{
    /// <summary>Outcome of a developer command (shown in the panel status line and the diagnostic log).</summary>
    public readonly struct DevResult
    {
        public readonly bool Ok;
        public readonly string Message;
        DevResult(bool ok, string message) { Ok = ok; Message = message ?? ""; }
        public static DevResult Success(string message) => new DevResult(true, message);
        public static DevResult Fail(string message) => new DevResult(false, message);
    }

    /// <summary>
    /// One developer operation. Rules every command follows (enforced by <see cref="DevCommands.Run"/>):
    /// access check → availability check (e.g. "only in the Farm Hub") → argument validation inside Execute →
    /// execution through the owning game service (never UI text or raw save files) → result → UI refresh event.
    /// </summary>
    public sealed class DevCommand
    {
        public string Id;
        public string Label;
        public string Category;
        /// <summary>Null when the command can run now, otherwise the reason it cannot.</summary>
        public Func<string> Availability;
        /// <summary>Executes with an optional text argument (validated by the command itself).</summary>
        public Func<string, DevResult> Execute;
        /// <summary>Destructive commands need an explicit confirmation token as argument (see Reset Farm).</summary>
        public bool Destructive;
    }

    /// <summary>
    /// Registry + runner for developer commands. New features add commands with <see cref="Register"/> and get the
    /// shared guarantees for free: one command at a time (no re-entrancy), repeated taps within a short window are
    /// ignored, exceptions become failures, every run is written to the diagnostic log.
    /// </summary>
    public static class DevCommands
    {
        const float RepeatGuardSeconds = 0.35f;

        static readonly Dictionary<string, DevCommand> _all = new Dictionary<string, DevCommand>();
        static readonly Dictionary<string, float> _lastRun = new Dictionary<string, float>();
        static bool _running;

        /// <summary>(command, result) after every attempt – panels refresh their values here.</summary>
        public static event Action<DevCommand, DevResult> Executed;

        /// <summary>
        /// Enter Play Mode without domain reload keeps statics alive: repeat-guard timestamps from the previous session
        /// (Time.unscaledTime restarts at 0) would block the first taps, so per-session state is cleared here.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetSession()
        {
            _lastRun.Clear();
            _running = false;
            Executed = null;
        }

        public static void Register(DevCommand c)
        {
            if (c == null || string.IsNullOrEmpty(c.Id) || c.Execute == null) throw new ArgumentException("Invalid dev command");
            _all[c.Id] = c;
        }

        public static bool TryGet(string id, out DevCommand c) => _all.TryGetValue(id, out c);
        public static IEnumerable<DevCommand> All => _all.Values;

        public static string AvailabilityOf(string id)
        {
            if (!_all.TryGetValue(id, out var c)) return "unknown command";
            if (!DevAccess.IsAllowed(out string why)) return why;
            return c.Availability != null ? c.Availability() : null;
        }

        public static DevResult Run(string id, string arg = null)
        {
            DevResult r;
            DevCommand c = null;
            if (!DevAccess.IsAllowed(out string why)) r = DevResult.Fail(why);
            else if (!_all.TryGetValue(id, out c)) r = DevResult.Fail("Unknown command " + id);
            else if (_running) r = DevResult.Fail("Another command is running");
            else if (_lastRun.TryGetValue(id, out float t) && Time.unscaledTime - t < RepeatGuardSeconds) return DevResult.Fail("Ignored repeated tap");
            else
            {
                string na = c.Availability != null ? c.Availability() : null;
                if (na != null) r = DevResult.Fail(na);
                else
                {
                    _running = true;
                    try { r = c.Execute(arg); }
                    catch (Exception e) { r = DevResult.Fail(e.GetType().Name + ": " + e.Message); Debug.LogException(e); }
                    finally { _running = false; }
                    // double-tap guard: a SUCCESSFUL run blocks the same command briefly; a rejected one can be retried at once
                    if (r.Ok) _lastRun[id] = Time.unscaledTime;
                }
            }
            DevLog.Add((r.Ok ? "OK   " : "FAIL ") + (c != null ? c.Label : id) + (string.IsNullOrEmpty(arg) || (c != null && c.Destructive) ? "" : " [" + arg + "]") + " → " + r.Message);
            Executed?.Invoke(c, r);
            return r;
        }

        // ------------------------------------------------------------------ argument helpers

        public static bool TryParseLong(string s, long min, long max, out long v, out string error)
        {
            error = null;
            if (!long.TryParse((s ?? "").Trim().Replace(",", "").Replace(" ", ""), NumberStyles.Integer, CultureInfo.InvariantCulture, out v))
            { error = "Enter a whole number"; return false; }
            if (v < min || v > max) { error = "Allowed range " + min.ToString("N0", CultureInfo.InvariantCulture) + "…" + max.ToString("N0", CultureInfo.InvariantCulture); return false; }
            return true;
        }

        /// <summary>Durations like "90", "90s", "15m", "2h", "1d", "1h30m".</summary>
        public static bool TryParseDuration(string s, out double seconds, out string error)
        {
            seconds = 0; error = null;
            s = (s ?? "").Trim().ToLowerInvariant().Replace(" ", "");
            if (s.Length == 0) { error = "Enter a duration (e.g. 90s, 15m, 2h, 1d)"; return false; }
            double total = 0, num = 0; bool hasNum = false; int frac = 0;
            foreach (char ch in s)
            {
                if (ch >= '0' && ch <= '9') { num = num * 10 + (ch - '0'); hasNum = true; if (frac > 0) frac++; continue; }
                if (ch == '.' && frac == 0) { frac = 1; continue; }
                double unit = ch == 's' ? 1 : ch == 'm' ? 60 : ch == 'h' ? 3600 : ch == 'd' ? 86400 : -1;
                if (unit < 0 || !hasNum) { error = "Unknown duration format"; return false; }
                if (frac > 1) num /= Math.Pow(10, frac - 1);
                total += num * unit; num = 0; hasNum = false; frac = 0;
            }
            if (hasNum) { if (frac > 1) num /= Math.Pow(10, frac - 1); total += num; }
            if (!(total > 0) || total > 3650.0 * 86400.0) { error = "Duration must be > 0 and ≤ 10 years"; return false; }
            seconds = total;
            return true;
        }
    }

    /// <summary>Small in-memory diagnostic journal of developer operations (Development builds / Editor only).</summary>
    public static class DevLog
    {
        const int Max = 80;
        static readonly List<string> _lines = new List<string>(Max);
        public static IReadOnlyList<string> Lines => _lines;
        public static event Action Changed;

        public static void Add(string line)
        {
            string entry = DateTime.Now.ToString("HH:mm:ss", CultureInfo.InvariantCulture) + "  " + line;
            if (_lines.Count >= Max) _lines.RemoveAt(0);
            _lines.Add(entry);
            Debug.Log("[DevTools] " + line);
            Changed?.Invoke();
        }
    }
}
