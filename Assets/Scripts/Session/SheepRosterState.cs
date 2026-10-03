using System;
using System.Collections.Generic;
using UnityEngine;

namespace ShearAndGrow
{
    /// <summary>Owns in-run sheep identities and selection. No economy, scene loading or presentation.</summary>
    [CreateAssetMenu(menuName = "Shear and Grow/Sheep Roster State")]
    public sealed class SheepRosterState : ScriptableObject
    {
        [SerializeField] private GameBalanceData balance;
        [SerializeField] private BarnController barn;
        private static int runVersion;
        [NonSerialized] private int initializedVersion = -1;
        [NonSerialized] private List<PrototypeSheepState> sheep;
        [NonSerialized] private int selectedIndex, initialCount;
        public event Action Changed;
        public BarnController Barn => barn;
        public bool IsConfigured => balance != null && barn != null && barn.IsConfigured && balance.InitialSheepCount <= barn.Capacity;
        public bool HasFreeSpace => IsConfigured && Count < barn.Capacity;
        public int Count { get { EnsureRun(); return sheep == null ? 0 : sheep.Count; } }
        public int PurchasedCount { get { EnsureRun(); return Count - initialCount; } }
        public int SelectedIndex { get { EnsureRun(); return selectedIndex; } }
        public PrototypeSheepState SelectedSheep => GetSheep(SelectedIndex);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void NewRun() => runVersion++;
        private void EnsureRun()
        {
            if (initializedVersion == runVersion || !IsConfigured) return;
            ReleaseOwnedSheep(); initializedVersion = runVersion;
            initialCount = balance.InitialSheepCount; selectedIndex = 0;
            sheep = new List<PrototypeSheepState>(initialCount);
            for (int i = 0; i < initialCount; i++) CreateSheep();
        }
        private PrototypeSheepState CreateSheep()
        {
            var state = CreateInstance<PrototypeSheepState>();
            state.name = "Sheep " + (sheep.Count + 1);
            state.hideFlags = HideFlags.DontSave;
            sheep.Add(state); return state;
        }
        public PrototypeSheepState GetSheep(int index)
        { EnsureRun(); return sheep != null && index >= 0 && index < sheep.Count ? sheep[index] : null; }
        public PrototypeSheepState AddSheep()
        {
            EnsureRun(); if (!HasFreeSpace) return null;
            var state = CreateSheep(); Changed?.Invoke(); return state;
        }
        public bool CanSelect(int index)
        {
            var candidate = GetSheep(index);
            return candidate != null && candidate.Status == PrototypeSheepState.SheepStatus.Ready &&
                (SelectedSheep == null || SelectedSheep.Status != PrototypeSheepState.SheepStatus.Shearing);
        }
        public bool TrySelectReady(int index)
        {
            if (!CanSelect(index)) return false;
            selectedIndex = index; return true;
        }
        private void ReleaseOwnedSheep()
        {
            if (sheep == null) return;
            foreach (var state in sheep)
                if (state != null) { if (Application.isPlaying) Destroy(state); else DestroyImmediate(state); }
            sheep.Clear();
        }
        private void OnDisable() { ReleaseOwnedSheep(); initializedVersion = -1; }
    }
}
