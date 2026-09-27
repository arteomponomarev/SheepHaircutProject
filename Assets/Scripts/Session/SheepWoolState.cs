using System;

namespace ShearAndGrow
{
    /// <summary>Per-sheep growth value, independent of Unity clocks, presentation and shearing.</summary>
    public sealed class SheepWoolState
    {
        private double growth = 1;
        public float WoolGrowth01 => (float)growth;

        public void ResetAfterShearing() => growth = 0;

        public void Advance(double seconds, float fullGrowthSeconds)
        {
            if (double.IsNaN(seconds) || double.IsInfinity(seconds) || seconds <= 0 ||
                float.IsNaN(fullGrowthSeconds) || float.IsInfinity(fullGrowthSeconds) || fullGrowthSeconds <= 0)
                return;
            growth = Math.Min(1, growth + seconds / fullGrowthSeconds);
        }
    }
}
