using System;

namespace ShearAndGrow.Farm
{
    /// <summary>Time source for construction timers (UTC ticks). Swappable for tests or a future server clock.</summary>
    public interface IBuildingClock
    {
        long UtcNowTicks { get; }
    }

    /// <summary>
    /// Device UTC clock. MVP limitation: the device clock is trusted — moving it forward finishes constructions
    /// early; moving it back never extends a construction beyond its configured duration (see BuildingService).
    /// </summary>
    public sealed class SystemBuildingClock : IBuildingClock
    {
        public long UtcNowTicks => DateTime.UtcNow.Ticks;
    }

    /// <summary>Manually driven clock (tests / QA).</summary>
    public sealed class ManualBuildingClock : IBuildingClock
    {
        public long Ticks;
        public ManualBuildingClock(long ticks) { Ticks = ticks; }
        public long UtcNowTicks => Ticks;
        public void Advance(double seconds) => Ticks += (long)(seconds * TimeSpan.TicksPerSecond);
    }
}