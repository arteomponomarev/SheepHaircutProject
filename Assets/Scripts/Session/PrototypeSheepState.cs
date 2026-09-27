using System;
using UnityEngine;

namespace ShearAndGrow
{
    /// <summary>Explicitly shared per-sheep lifecycle and result. No rendering, input, inventory or disk saving.</summary>
    [CreateAssetMenu(menuName = "Shear and Grow/Prototype Sheep State")]
    public sealed class PrototypeSheepState : ScriptableObject
    {
        public enum SheepStatus { Ready, Shearing, RecentlySheared, Growing }
        private static int runVersion;
        [NonSerialized] private int initializedVersion = -1;
        [NonSerialized] private SheepStatus status;
        [NonSerialized] private float resultCoverage, resultWool;
        [NonSerialized] private SheepWoolState wool;
        [NonSerialized] private double lastGrowthTime;
        [NonSerialized] private int lastNotifiedGrowthPercent;
        public SheepStatus Status { get { EnsureRun(); return status; } }
        public float WoolGrowth01 { get { EnsureRun(); return wool.WoolGrowth01; } }
        public float ResultCoverage { get { EnsureRun(); return resultCoverage; } }
        public float ResultWool { get { EnsureRun(); return resultWool; } }
        public event Action Changed;

        // A run token resets nonserialized state even with Editor domain reload disabled.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void NewRun() => runVersion++;
        private void EnsureRun()
        {
            if (initializedVersion == runVersion) return;
            initializedVersion = runVersion; status = SheepStatus.Ready; resultCoverage = resultWool = 0;
            wool = new SheepWoolState(); lastGrowthTime = Time.timeAsDouble; lastNotifiedGrowthPercent = 100;
        }
        public bool TryBeginShearing()
        {
            if (Status != SheepStatus.Ready) return false;
            status = SheepStatus.Shearing; Changed?.Invoke(); return true;
        }
        public bool TryCompleteShearing(float coverage, float wool)
        {
            if (Status != SheepStatus.Shearing || float.IsNaN(coverage) || float.IsInfinity(coverage) ||
                float.IsNaN(wool) || float.IsInfinity(wool) || coverage < 0 || coverage > 1 || wool < 0) return false;
            resultCoverage = coverage; resultWool = wool;
            this.wool.ResetAfterShearing(); lastGrowthTime = Time.timeAsDouble; lastNotifiedGrowthPercent = 0;
            status = SheepStatus.RecentlySheared; Changed?.Invoke(); return true;
        }
        /// <summary>Absolute game time makes scene transitions and duplicate clocks idempotent.</summary>
        public void AdvanceGrowth(double gameTime, GameBalanceData balance)
        {
            EnsureRun();
            if (balance == null || double.IsNaN(gameTime) || double.IsInfinity(gameTime) || gameTime <= lastGrowthTime) return;
            double elapsed = gameTime - lastGrowthTime;
            lastGrowthTime = gameTime;
            if (status == SheepStatus.Shearing || wool.WoolGrowth01 >= 1f) return;
            wool.Advance(elapsed, balance.WoolGrowthSeconds);
            SheepStatus next = wool.WoolGrowth01 >= balance.ReadyToShearThreshold ? SheepStatus.Ready : SheepStatus.Growing;
            int percent = Mathf.FloorToInt(wool.WoolGrowth01 * 100f);
            bool changed = next != status || percent != lastNotifiedGrowthPercent;
            status = next; lastNotifiedGrowthPercent = percent;
            // Presentation only needs whole-percent changes; avoid rebuilding UI every frame.
            if (changed) Changed?.Invoke();
        }
        public void CancelUnfinishedShearing()
        {
            if (Status != SheepStatus.Shearing) return;
            status = SheepStatus.Ready; Changed?.Invoke();
        }
    }
}
