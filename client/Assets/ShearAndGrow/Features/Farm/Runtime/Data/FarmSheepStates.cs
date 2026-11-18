using System;
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
#if UNITY_EDITOR || UNITY_ENABLE_CHECKS
            /// <summary>Dev only: texels seen shorter than full this session (the sheep's cut regions, even after regrowth).</summary>
            public bool[] DevCutRegion;
#endif
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
#if UNITY_EDITOR || UNITY_ENABLE_CHECKS
            e.Mask = mask; DevObserveCutRegion(e); e.Mask = null;
#endif
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
            {
                if (e.State == null) continue;
#if UNITY_EDITOR || UNITY_ENABLE_CHECKS
                DevObserveCutRegion(e);
#endif
                if (SheepWoolLifecycle.CatchUp(e.State, e.Mask, _growth, now) > 0) changed = true;
            }
            return changed;
        }

        /// <summary>Applies growth now regardless of the update interval (e.g. after the clock jumped).</summary>
        public bool TickNow(float time) { _nextTick = 0f; return Tick(time); }

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

        #if UNITY_EDITOR || UNITY_ENABLE_CHECKS
                // ------------------------------------------------------------------ developer interface (compiled out of release builds)
                //
                // Every operation goes through the same lifecycle as real play (catch-up first, then the change, restamp, save).
                // The REGIONAL MASK stays authoritative: only already-cut texels are changed, untouched fleece never is. No
                // operation touches the economy, the payment ledger, lifetimeRemovedWool or the wool cycle, so artificially
                // restored wool is paid exactly like naturally regrown wool – only when it is really sheared.

                public struct GrowthDiagnostics
                {
                    public bool Known, HasSave, RegrowthEnabled, Preserved, Compatible;
                    public WoolGrowthTimeModeView Mode;
                    public double Now, Stamp, Elapsed, Carry, SecondsPerStep, BoostUntil, BoostLeft, DiskStamp, DiskElapsed;
                    public float EffectiveMultiplier, BoostMultiplier, WoolAmount, SecondsToFull;
                    public int PendingSteps, CutTexels, ObservedCutTexels;
                    public string Reason;
                }

                public enum WoolGrowthTimeModeView { GameTime = 0, RealTime = 1 }

                void DevObserveCutRegion(Entry e)
                {
                    if (e == null || e.Mask == null) return;
                    var o = e.Mask.Occupancy;
                    if (e.DevCutRegion == null || e.DevCutRegion.Length != o.Length) e.DevCutRegion = new bool[o.Length];
                    for (int i = 0; i < o.Length; i++) if (o[i] < ShearMaskAuthority.Full) e.DevCutRegion[i] = true;
                }

                bool DevSave(Entry e, out string error)
                {
                    error = null;
                    e.State.cutMask = e.Mask.Encode();
                    e.State.woolAmountAtSave = e.Mask.WoolAmount;
                    e.State.creditedCutArea = e.Mask.CutFraction;
                    try { SheepWoolStateStore.Save(e.State); return true; }
                    catch (System.Exception ex) { error = "Save failed: " + ex.Message; Debug.LogError("[Farm] " + error); return false; }
                }

                bool DevPrepare(string sheepId, out Entry e, out string error, bool refusePreserved)
                {
                    e = Find(sheepId); error = null;
                    if (e == null) { error = "Unknown sheep " + sheepId; return false; }
                    if (!e.Compatible) { error = "Save uses another wool surface version – not editable"; return false; }
                    if (e.State == null || e.Mask == null) { error = "Never sheared – full fleece, no cut regions"; return false; }
                    if (refusePreserved && e.State.hairstylePreserved) { error = "Hairstyle preserved – toggle Preserve off first"; return false; }
                    DevObserveCutRegion(e);
                    SheepWoolLifecycle.CatchUp(e.State, e.Mask, _growth, Now); // bring growth up to now before editing
                    return true;
                }

                /// <summary>Regrows every cut region to full (like completed natural regrowth). Untouched wool unchanged; nothing is paid.</summary>
                public bool DevRestoreFullWool(string sheepId, out string error)
                {
                    if (!DevPrepare(sheepId, out var e, out error, true)) return false;
                    e.Mask.ApplyGrowth(ShearMaskAuthority.Full);
                    e.State.growthCarrySeconds = 0;
                    SheepWoolLifecycle.Restamp(e.State, Now);
                    return DevSave(e, out error);
                }

                /// <summary>
                /// Sets the wool length of the CUT regions (texels seen shorter than full during this session) to
                /// <paramref name="growth01"/>; never-cut fleece is not touched. 1 = fully regrown, 0 = freshly cut.
                /// </summary>
                public bool DevSetCutRegionGrowth(string sheepId, float growth01, out string error)
                {
                    if (!DevPrepare(sheepId, out var e, out error, true)) return false;
                    DevObserveCutRegion(e);
                    byte len = (byte)Mathf.Clamp(Mathf.RoundToInt(Mathf.Clamp01(growth01) * ShearMaskAuthority.Full), 0, ShearMaskAuthority.Full);
                    var o = e.Mask.Occupancy;
                    int n = 0;
                    for (int i = 0; i < o.Length; i++) if (e.DevCutRegion[i]) { o[i] = len; n++; }
                    if (n == 0) { error = "No cut regions known for this sheep (shear it first)"; return false; }
                    e.Mask.RecomputeCutWeight();
                    e.Mask.MarkAllDirty();
                    e.State.growthCarrySeconds = 0;
                    SheepWoolLifecycle.Restamp(e.State, Now);
                    return DevSave(e, out error);
                }

                /// <summary>Removes an active feed boost (receipt id kept, so purchase reconciliation is unaffected).</summary>
                public bool DevClearBoost(string sheepId, out string error)
                {
                    if (!DevPrepare(sheepId, out var e, out error, false)) return false;
                    e.State.feedBoostUntil = 0;
                    e.State.feedBoostMultiplier = 1f;
                    return DevSave(e, out error);
                }

                /// <summary>Starts a test growth boost through the lifecycle (refused for preserved hairstyles). No coins move.</summary>
                public bool DevApplyBoost(string sheepId, float multiplier, float seconds, out string error)
                {
                    if (!DevPrepare(sheepId, out var e, out error, true)) return false;
                    if (!SheepWoolLifecycle.ApplyFeedBoost(e.State, e.Mask, _growth, Now, multiplier, seconds, e.State.feedPurchaseId))
                    { error = "Boost refused by the wool lifecycle"; return false; }
                    return DevSave(e, out error);
                }

                /// <summary>Everything needed to see why a sheep does (not) regrow.</summary>
                public GrowthDiagnostics DevDiagnostics(string sheepId)
                {
                    var d = new GrowthDiagnostics { Now = Now, RegrowthEnabled = _growth != null && _growth.RegrowthEnabled };
                    var e = Find(sheepId);
                    if (e == null) { d.Reason = "unknown sheep"; return d; }
                    d.Known = true; d.Compatible = e.Compatible;
                    d.Mode = _growth != null ? (WoolGrowthTimeModeView)(int)_growth.TimeMode : WoolGrowthTimeModeView.GameTime;
                    d.EffectiveMultiplier = _growth != null ? _growth.EffectiveMultiplier : 1f;
                    d.SecondsPerStep = _growth != null ? _growth.SecondsPerStep : 1.0;
                    if (e.State == null) { d.WoolAmount = 1f; d.Reason = e.Compatible ? "never sheared – full fleece" : "incompatible save ignored"; return d; }
                    DevObserveCutRegion(e);
                    var s = e.State;
                    d.HasSave = true;
                    d.Preserved = s.hairstylePreserved;
                    d.Stamp = s.growthClockStamp; d.Elapsed = Math.Max(0.0, d.Now - s.growthClockStamp); d.Carry = s.growthCarrySeconds;
                    d.BoostUntil = s.feedBoostUntil; d.BoostMultiplier = s.feedBoostMultiplier; d.BoostLeft = SheepWoolLifecycle.BoostSecondsLeft(s, d.Now);
                    d.WoolAmount = e.Mask.WoolAmount;
                    d.SecondsToFull = SheepWoolLifecycle.SecondsToFull(s, e.Mask, _growth, d.Now);
                    double boosted = SheepWoolLifecycle.BoostedSeconds(s, s.growthClockStamp, d.Now);
                    double grow = (d.Elapsed + boosted * (Math.Max(1.0, s.feedBoostMultiplier) - 1.0)) * d.EffectiveMultiplier + s.growthCarrySeconds;
                    d.PendingSteps = d.Preserved || !d.RegrowthEnabled ? 0 : (int)Math.Floor(grow / Math.Max(1e-6, d.SecondsPerStep));
                    var o = e.Mask.Occupancy;
                    for (int i = 0; i < o.Length; i++) { if (o[i] < ShearMaskAuthority.Full) d.CutTexels++; if (e.DevCutRegion != null && e.DevCutRegion[i]) d.ObservedCutTexels++; }
                    if (SheepWoolStateStore.TryLoad(sheepId, _epoch, out var disk))
                    {
                        d.DiskStamp = disk.growthClockStamp;
                        d.DiskElapsed = disk.growthClockMode == (int)d.Mode ? Math.Max(0.0, d.Now - disk.growthClockStamp) : -1.0;
                    }
                    d.Reason = !d.RegrowthEnabled ? "regrowth disabled in WoolGrowthConfig" :
                               d.Preserved ? "hairstyle preserved – growth paused" :
                               d.CutTexels == 0 ? "full fleece – nothing to regrow" :
                               d.EffectiveMultiplier <= 0f ? "debug multiplier is 0" : "growing";
                    return d;
                }
        #endif

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
