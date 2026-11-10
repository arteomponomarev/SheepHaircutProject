using System.Collections.Generic;
using ShearAndGrow.Shearing;
using UnityEngine;

namespace ShearAndGrow.Farm
{
    /// <summary>
    /// The farm's view of every sheep's authoritative wool state. Each save is loaded and decoded ONCE; growth is
    /// applied in memory every <see cref="WoolGrowthConfig.RegrowthUpdateInterval"/> through the shared
    /// <see cref="SheepWoolLifecycle"/> (deterministic, clock-based — no need to write the file on every tick).
    /// The file is written only when the player changes something here (hairstyle lock) — the next scene that
    /// opens the sheep derives the same growth from the saved stamp.
    /// </summary>
    public sealed class FarmSheepStates
    {
        public sealed class Entry
        {
            public string SheepId;
            public string DisplayName;
            public SheepWoolState State;      // null = never sheared (default game state: full wool)
            public ShearMaskAuthority Mask;   // null when State is null
            public bool Compatible = true;
        }

        readonly List<Entry> _entries = new List<Entry>();
        readonly WoolGrowthConfig _growth;
        readonly int _epoch, _surfaceVersion;
        float _nextTick;

        public IReadOnlyList<Entry> Entries => _entries;
        public int TickCount { get; private set; }

        public FarmSheepStates(WoolGrowthConfig growth, int saveEpoch, int surfaceVersion)
        {
            _growth = growth; _epoch = saveEpoch; _surfaceVersion = surfaceVersion;
        }

        static readonly SheepBodySurface Surface = new SheepBodySurface();

        double Now => _growth != null ? _growth.Now : ShearAndGrow.Economy.WoolGrowthClock.GameSeconds;

        public Entry Add(string sheepId, string displayName)
        {
            var e = new Entry { SheepId = sheepId, DisplayName = displayName };
            Reload(e);
            _entries.Add(e);
            return e;
        }

        public void Reload(Entry e)
        {
            e.State = null; e.Mask = null; e.Compatible = true;
            if (!SheepWoolStateStore.TryLoad(e.SheepId, _epoch, out var s)) return;
            if (s.woolSurfaceVersion != _surfaceVersion || s.maskWidth != FarmSheepView.MaskW || s.maskHeight != FarmSheepView.MaskH)
            {
                e.Compatible = false; // same rule as shearing: never apply a pattern to another topology
                return;
            }
            var mask = new ShearMaskAuthority(Surface, FarmSheepView.MaskW, FarmSheepView.MaskH);
            if (!mask.Decode(s.cutMask)) return;
            SheepWoolLifecycle.CatchUp(s, mask, _growth, Now);
            e.State = s; e.Mask = mask;
        }

        public Entry Find(string sheepId)
        {
            foreach (var e in _entries) if (e.SheepId == sheepId) return e;
            return null;
        }

        /// <summary>Periodic growth (cheap: one byte pass per sheep). Returns true if any coat changed.</summary>
        public bool Tick(float time)
        {
            if (time < _nextTick) return false;
            _nextTick = time + (_growth != null ? _growth.RegrowthUpdateInterval : 2f);
            TickCount++;
            bool changed = false;
            double now = Now;
            foreach (var e in _entries)
                if (e.State != null && SheepWoolLifecycle.CatchUp(e.State, e.Mask, _growth, now) > 0) changed = true;
            return changed;
        }

        public FarmSheepInfo Info(Entry e)
        {
            var info = new FarmSheepInfo { SheepId = e.SheepId, DisplayName = e.DisplayName, RemainingWool = 1f };
            if (e.State == null) return info;
            info.HasSave = true;
            info.RemainingWool = e.Mask.WoolAmount;
            info.Preserved = e.State.hairstylePreserved;
            double now = Now;
            info.SecondsToFull = SheepWoolLifecycle.SecondsToFull(e.State, e.Mask, _growth, now);
            info.BoostSecondsLeft = (float)SheepWoolLifecycle.BoostSecondsLeft(e.State, now);
            info.BoostMultiplier = e.State.feedBoostMultiplier;
            return info;
        }

        public double ClockNow => Now;

        public double BoostSecondsLeft(string sheepId)
        {
            var e = Find(sheepId);
            return e != null && e.State != null ? SheepWoolLifecycle.BoostSecondsLeft(e.State, Now) : 0.0;
        }

        /// <summary>
        /// Starts a feed boost through the shared lifecycle (catch-up first, then the window) and saves the sheep.
        /// Refused for unknown / never-sheared / preserved sheep. The mask itself is never edited here.
        /// </summary>
        public bool ApplyFeedBoost(string sheepId, float multiplier, float seconds, string purchaseId)
        {
            var e = Find(sheepId);
            if (e == null || e.State == null || e.Mask == null) return false;
            if (!SheepWoolLifecycle.ApplyFeedBoost(e.State, e.Mask, _growth, Now, multiplier, seconds, purchaseId)) return false;
            e.State.cutMask = e.Mask.Encode();
            e.State.woolAmountAtSave = e.Mask.WoolAmount;
            e.State.creditedCutArea = e.Mask.CutFraction;
            try { SheepWoolStateStore.Save(e.State); }
            catch (System.Exception ex)
            {
                Debug.LogError("[Farm] Could not save sheep " + sheepId + ": " + ex.Message);
                e.State.feedBoostUntil = 0; e.State.feedBoostMultiplier = 1f; e.State.feedPurchaseId = null;
                return false;
            }
            return true;
        }

        /// <summary>Feeding receipt applied to this sheep (reconciliation).</summary>
        public bool HasFeedPurchase(string sheepId, string purchaseId)
        {
            var e = Find(sheepId);
            if (e == null) return false;
            if (e.State != null) return e.State.feedPurchaseId == purchaseId;
            return SheepWoolStateStore.TryLoad(sheepId, _epoch, out var s) && s.feedPurchaseId == purchaseId;
        }

        /// <summary>Locks / unlocks a hairstyle and saves immediately (no resources involved).</summary>
        public bool SetPreserved(string sheepId, bool preserved)
        {
            var e = Find(sheepId);
            if (e == null || e.State == null || e.State.hairstylePreserved == preserved) return false;
            SheepWoolLifecycle.SetPreserved(e.State, e.Mask, _growth, Now, preserved);
            e.State.cutMask = e.Mask.Encode();
            e.State.woolAmountAtSave = e.Mask.WoolAmount;
            e.State.creditedCutArea = e.Mask.CutFraction;
            try { SheepWoolStateStore.Save(e.State); }
            catch (System.Exception ex) { Debug.LogError("[Farm] Could not save sheep " + sheepId + ": " + ex.Message); return false; }
            return true;
        }
    }
}
