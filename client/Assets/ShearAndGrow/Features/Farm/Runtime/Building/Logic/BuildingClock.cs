using System;

namespace ShearAndGrow.Farm
{
    /// <summary>Time source for construction timers (UTC ticks). Swappable for tests or a future server clock.</summary>
    public interface IBuildingClock
    {
        long UtcNowTicks { get; }
    }

    /// <summary>
    /// Game clock (<see cref="ShearAndGrow.Economy.GameClock"/>): device UTC today, monotonic (moving the device clock
    /// back holds time instead of replaying it), plus the developer test timeline in dev builds. MVP limitation: the
    /// device source is not trusted — moving it forward finishes constructions early until a server time source is set.
    /// </summary>
    public sealed class SystemBuildingClock : IBuildingClock
    {
        public long UtcNowTicks => ShearAndGrow.Economy.GameClock.UtcNowTicks;
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