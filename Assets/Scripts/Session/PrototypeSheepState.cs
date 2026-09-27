using System;
using UnityEngine;

namespace ShearAndGrow
{
    /// <summary>Explicitly shared per-sheep runtime state. No disk saving, inventory or growth timer.</summary>
    [CreateAssetMenu(menuName = "Shear and Grow/Prototype Sheep State")]
    public sealed class PrototypeSheepState : ScriptableObject
    {
        public enum SheepStatus { Ready, Shearing, RecentlySheared }
        private static int runVersion;
        [NonSerialized] private int initializedVersion = -1;
        [NonSerialized] private SheepStatus status;
        [NonSerialized] private float resultCoverage, resultWool;
        public SheepStatus Status { get { EnsureRun(); return status; } }
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
            status = SheepStatus.RecentlySheared; Changed?.Invoke(); return true;
        }
        public void CancelUnfinishedShearing()
        {
            if (Status != SheepStatus.Shearing) return;
            status = SheepStatus.Ready; Changed?.Invoke();
        }
    }
}
