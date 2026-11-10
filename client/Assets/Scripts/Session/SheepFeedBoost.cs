using System;

namespace ShearAndGrow
{
    /// <summary>One non-stacking timed growth modifier. Pure time math; no economy, UI or Unity clock.</summary>
    public sealed class SheepFeedBoost
    {
        private double startsAt, endsAt;
        public float Multiplier { get; private set; } = 1;
        public double Remaining(double time) => Math.Max(0, endsAt - time);
        public bool IsActive(double time) => time >= startsAt && time < endsAt;
        public bool TryStart(double time, float duration, float multiplier)
        {
            if (double.IsNaN(time) || double.IsInfinity(time) || float.IsNaN(duration) || float.IsInfinity(duration) ||
                float.IsNaN(multiplier) || float.IsInfinity(multiplier) || duration <= 0 || multiplier <= 1 || IsActive(time)) return false;
            startsAt = time; endsAt = time + duration; Multiplier = multiplier; return true;
        }
        public double GrowthSeconds(double from, double to)
        {
            if (double.IsNaN(from) || double.IsNaN(to) || double.IsInfinity(from) || double.IsInfinity(to) || to <= from) return 0;
            double boosted = Math.Max(0, Math.Min(to, endsAt) - Math.Max(from, startsAt));
            return to - from + boosted * (Multiplier - 1);
        }
        public void Clear() { startsAt = endsAt = 0; Multiplier = 1; }
    }
}
