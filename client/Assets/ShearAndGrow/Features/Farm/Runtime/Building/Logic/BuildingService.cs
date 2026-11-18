using System;
using System.Collections.Generic;
using ShearAndGrow.Economy;
using UnityEngine;

namespace ShearAndGrow.Farm
{
    /// <summary>
    /// Authority over the farm's buildings and decorations (pure logic, no scene objects). Owns the saved
    /// <see cref="FarmState"/> and the logical <see cref="FarmGrid"/>; validates and performs purchases (atomic across
    /// economy.json + farm.json via receipts), moves, storing, and construction timers. Views listen to its events.
    /// </summary>
    public sealed class BuildingService
    {
        readonly FarmLayout _layout;
        readonly BuildingCatalog _catalog;
        readonly ConstructionConfig _config;
        readonly WoolEconomy _econ;
        readonly IBuildingClock _clock;
        readonly Func<FarmState, bool> _saver;
        readonly int _epoch;
        FarmState _state;
        FarmGrid _grid;
        bool _busy;

        public event Action<BuildingInstanceState> InstanceAdded, InstanceMoved, InstanceRemoved, StatusChanged;
        /// <summary>Something in the farm data changed (counts, statuses) — UI refresh hint.</summary>
        public event Action Changed;

        public FarmState State => _state;
        public FarmGrid Grid => _grid;
        public FarmLayout Layout => _layout;
        public BuildingCatalog Catalog => _catalog;
        public ConstructionConfig Config => _config;
        public IBuildingClock Clock => _clock;
        public string LastSaveError { get; private set; }
        /// <summary>Instances completed while the app was closed (filled by <see cref="Load"/>).</summary>
        public readonly List<BuildingInstanceState> CompletedWhileAway = new List<BuildingInstanceState>();

        public BuildingService(FarmLayout layout, BuildingCatalog catalog, ConstructionConfig config, WoolEconomy economy,
                               IBuildingClock clock = null, Func<FarmState, bool> saver = null, int epoch = 0, FarmProgressionConfig progression = null)
        {
            _layout = layout; _catalog = catalog; _config = config; _econ = economy;
            _clock = clock ?? new SystemBuildingClock();
            _saver = saver ?? DefaultSave;
            _epoch = epoch;
            _progression = progression;
        }

        readonly FarmProgressionConfig _progression;
        public FarmProgressionConfig Progression => _progression;
        public WoolEconomy Economy => _econ;

        /// <summary>An upgrade finished: (instance, new level).</summary>
        public event Action<BuildingInstanceState, int> LevelChanged;
        public event Action<BuildingInstanceState> UpgradeStarted;

        /// <summary>Receipt item ids: building purchases use the type id; these prefixes mark other farm purchases.</summary>
        public const string UpgradeItemPrefix = "upgrade:", SheepItemPrefix = "sheep:", FeedItemPrefix = "feed:";

        /// <summary>Persists the farm state (used by services sharing farm.json, e.g. the herd).</summary>
        public bool SaveState() => Save();
        public void RaiseChanged() => Changed?.Invoke();

        bool DefaultSave(FarmState s)
        {
            try { FarmStateStore.Save(s); LastSaveError = null; return true; }
            catch (Exception e) { LastSaveError = e.Message; Debug.LogError("[Farm] Save failed: " + e.Message); return false; }
        }

        public FarmBuildingDefinition Definition(string typeId)
        {
            var d = _catalog != null ? _catalog.Find(typeId) : null;
            return d != null ? d : _layout.Definition(typeId);
        }

        // ------------------------------------------------------------------ load

        /// <summary>Loads (or creates) the farm, rebuilds the grid, reconciles pending purchases, applies offline time.</summary>
        public void Load(FarmState loaded)
        {
            CompletedWhileAway.Clear();
            bool dirty = false;
            if (loaded == null) { loaded = FarmState.NewPlayer(_layout, _epoch); dirty = true; }
            _state = loaded;
            _state.saveEpoch = Mathf.Max(_state.saveEpoch, _epoch);
            dirty |= Sanitize();
            RebuildGrid();
            dirty |= Reconcile();
            long now = _clock.UtcNowTicks;
            if (_state.lastSeenUtcTicks > 0 && now + (long)(_config.ClockRollbackLogSeconds * TimeSpan.TicksPerSecond) < _state.lastSeenUtcTicks)
                Debug.LogWarning("[Farm] Device clock is earlier than the last session (rollback). Construction times stay capped at their duration.");
            foreach (var inst in _state.instances)
                if (inst.IsBuilderBusy && RemainingSeconds(inst) <= 0.0)
                {
                    FinishWork(inst);
                    CompletedWhileAway.Add(inst);
                    dirty = true;
                }
            _state.lastSeenUtcTicks = now;
            if (dirty) Save();
        }

        /// <summary>Removes broken / duplicate entries and guarantees the starting buildings exist.</summary>
        bool Sanitize()
        {
            bool changed = false;
            var seen = new HashSet<string>();
            for (int i = _state.instances.Count - 1; i >= 0; i--)
            {
                var b = _state.instances[i];
                if (b == null || string.IsNullOrEmpty(b.instanceId) || Definition(b.typeId) == null || !seen.Add(b.instanceId))
                {
                    Debug.LogWarning("[Farm] Dropping invalid building entry " + (b != null ? b.instanceId + "/" + b.typeId : "null"));
                    _state.instances.RemoveAt(i); changed = true;
                }
            }
            foreach (var sb in _layout.StartingBuildings)
                if (_state.Find(sb.InstanceId) == null && _state.FirstOfType(sb.TypeId) == null)
                {
                    var def = Definition(sb.TypeId);
                    _state.instances.Add(new BuildingInstanceState { instanceId = sb.InstanceId, typeId = sb.TypeId, cell = sb.Cell, level = def != null ? def.StartLevel : 1, status = BuildingStatus.Completed });
                    changed = true;
                }
            // flock: a farm always has its starting sheep (new player or schema-2 migration seeds them once)
            if (_state.herd == null) _state.herd = new List<HerdEntry>();
            if (_state.herd.Count == 0)
            {
                foreach (var sh in _layout.Sheep)
                    _state.herd.Add(new HerdEntry { sheepId = sh.SheepId, displayName = sh.DisplayName, acquiredUtc = DateTime.UtcNow.ToString("o") });
                changed = true;
            }
            int next = 1;
            foreach (var h in _state.herd) next = Math.Max(next, SheepNumber(h.sheepId) + 1);
            if (_state.nextSheepNumber < next) { _state.nextSheepNumber = next; changed = true; }
            return changed;
        }

        /// <summary>Number in "sheep_NNN" (0 if not in that format).</summary>
        public static int SheepNumber(string sheepId)
        {
            if (string.IsNullOrEmpty(sheepId) || !sheepId.StartsWith("sheep_")) return 0;
            return int.TryParse(sheepId.Substring(6), out int n) ? n : 0;
        }

        void RebuildGrid()
        {
            _grid = new FarmGrid(_layout.SizeCells.x, _layout.SizeCells.y, _layout.ReservedRects);
            foreach (var b in _state.instances)
            {
                if (!b.IsPlaced) continue;
                var def = Definition(b.typeId);
                var r = FarmGrid.RectOf(b.cell, def.SafeFootprint);
                if (_grid.CheckArea(r, null, out _) != PlacementResult.Ok)
                    Debug.LogWarning("[Farm] Saved instance " + b.instanceId + " overlaps or is outside the grid; kept at its cell.");
                _grid.Occupy(b.instanceId, b.typeId, r);
            }
        }

        /// <summary>Closes economy receipts: confirmed if the building exists in the farm save, refunded otherwise.</summary>
        bool Reconcile()
        {
            if (_econ == null) return false;
            var pending = new List<PendingPurchase>(_econ.PendingPurchases);
            foreach (var p in pending)
            {
                string item = p.itemId ?? "";
                bool exists;
                if (item.StartsWith(UpgradeItemPrefix)) exists = _state.instances.Exists(b => b.upgradePurchaseId == p.purchaseId);
                else if (item.StartsWith(SheepItemPrefix)) exists = _state.herd.Exists(h => h.purchaseId == p.purchaseId);
                else if (Definition(item) != null) exists = _state.instances.Exists(b => b.purchaseId == p.purchaseId);
                else continue; // not a farm.json purchase (e.g. feeding → FeedingService)
                if (exists) _econ.ConfirmPurchase(p.purchaseId);
                else { _econ.RefundPurchase(p.purchaseId); Debug.LogWarning("[Farm] Refunded unfinished purchase " + p.itemId + " (" + p.price + " coins)."); }
            }
            return false;
        }

        public bool Save()
        {
            _state.lastSeenUtcTicks = Math.Max(_state.lastSeenUtcTicks, _clock.UtcNowTicks);
            return _saver(_state);
        }

        // ------------------------------------------------------------------ queries

        public int FarmerHouseLevel
        {
            get
            {
                foreach (var b in _state.instances)
                {
                    var d = Definition(b.typeId);
                    if (d != null && d.Interaction == BuildingInteraction.FarmerHouse) return Mathf.Max(1, b.level);
                }
                return 1;
            }
        }

        public int CountOf(string typeId) { int n = 0; foreach (var b in _state.instances) if (b.typeId == typeId) n++; return n; }
        public int StoredCount(string typeId) { int n = 0; foreach (var b in _state.instances) if (b.typeId == typeId && b.status == BuildingStatus.Stored) n++; return n; }

        public BuildingInstanceState FirstStored(string typeId)
        {
            foreach (var b in _state.instances) if (b.typeId == typeId && b.status == BuildingStatus.Stored) return b;
            return null;
        }

        /// <summary>Builder jobs running (construction + upgrades share ONE queue).</summary>
        public int ActiveConstructionCount { get { int n = 0; foreach (var b in _state.instances) if (b.IsBuilderBusy) n++; return n; } }

        /// <summary>The instance the builder works on (construction or upgrade).</summary>
        public BuildingInstanceState ActiveConstruction
        {
            get { foreach (var b in _state.instances) if (b.IsBuilderBusy) return b; return null; }
        }

        /// <summary>True when a completed (or upgrading, still working) instance offers this interaction.</summary>
        public bool IsFunctional(BuildingInteraction kind)
        {
            foreach (var b in _state.instances)
                if (b.IsFunctional && Definition(b.typeId)?.Interaction == kind) return true;
            return false;
        }

        /// <summary>First placed instance offering an interaction (e.g. the barn).</summary>
        public BuildingInstanceState FirstWithInteraction(BuildingInteraction kind)
        {
            foreach (var b in _state.instances)
                if (b.IsPlaced && Definition(b.typeId)?.Interaction == kind) return b;
            return null;
        }

        public double RemainingSeconds(BuildingInstanceState b)
        {
            if (b == null || !b.IsBuilderBusy) return 0.0;
            double duration = Math.Max(0.0, b.constructionSeconds);
            double left = (b.constructionEndUtcTicks - _clock.UtcNowTicks) / (double)TimeSpan.TicksPerSecond;
            if (double.IsNaN(left)) left = 0.0;
            return Math.Max(0.0, Math.Min(duration, left)); // clock rollback never extends beyond the duration
        }

        public float Progress(BuildingInstanceState b)
        {
            if (b == null) return 0f;
            if (!b.IsBuilderBusy) return 1f;
            if (b.constructionSeconds <= 0.001f) return 1f;
            return Mathf.Clamp01(1f - (float)(RemainingSeconds(b) / b.constructionSeconds));
        }

        // ------------------------------------------------------------------ validation

        /// <summary>Validates buying a NEW instance at <paramref name="cell"/>.</summary>
        public PlacementResult CheckPurchase(FarmBuildingDefinition def, Vector2Int cell, bool includeCoins = true)
        {
            var r = CheckPurchaseRules(def, includeCoins);
            if (r != PlacementResult.Ok) return r;
            return _grid.CheckArea(FarmGrid.RectOf(cell, def.SafeFootprint), null, out _);
        }

        /// <summary>Rules that do not depend on the position (catalog status).</summary>
        public PlacementResult CheckPurchaseRules(FarmBuildingDefinition def, bool includeCoins = true)
        {
            if (def == null) return PlacementResult.Unknown;
            if (!def.Purchasable) return PlacementResult.NotPurchasable;
            if (!def.IsUnlocked(FarmerHouseLevel)) return PlacementResult.Locked;
            if (def.MaxInstances > 0 && CountOf(def.Id) >= def.MaxInstances) return PlacementResult.MaxInstances;
            if (def.NeedsConstruction && ActiveConstructionCount >= Mathf.Max(1, _config.MaxConcurrentConstructions)) return PlacementResult.ConstructionBusy;
            if (includeCoins && _econ != null && !_econ.CanAfford(def.Price)) return PlacementResult.NotEnoughCoins;
            return PlacementResult.Ok;
        }

        public PlacementResult CheckMove(BuildingInstanceState b, Vector2Int cell)
        {
            if (b == null) return PlacementResult.Unknown;
            var def = Definition(b.typeId);
            if (def == null) return PlacementResult.Unknown;
            if (!def.Movable) return PlacementResult.NotMovable;
            if (b.IsBuilderBusy) return PlacementResult.UnderConstruction;
            return _grid.CheckArea(FarmGrid.RectOf(cell, def.SafeFootprint), b.IsPlaced ? b.instanceId : null, out _);
        }

        // ------------------------------------------------------------------ operations

        /// <summary>
        /// Atomic purchase: validate → spend coins with a receipt (economy save) → add the instance (farm save) →
        /// confirm the receipt. Any failure after spending refunds. Re-calling with the same purchase id never charges
        /// or builds twice.
        /// </summary>
        public bool TryPurchase(FarmBuildingDefinition def, Vector2Int cell, string purchaseId, out BuildingInstanceState inst, out PlacementResult result)
        {
            inst = null;
            if (_busy) { result = PlacementResult.Busy; return false; }
            if (string.IsNullOrEmpty(purchaseId)) { result = PlacementResult.Unknown; return false; }
            var existing = _state.instances.Find(b => b.purchaseId == purchaseId);
            if (existing != null) { inst = existing; result = PlacementResult.Ok; _econ?.ConfirmPurchase(purchaseId); return true; }
            if (_econ != null && _econ.IsPurchaseKnown(purchaseId) && !_econ.IsPurchasePending(purchaseId)) { result = PlacementResult.Busy; return false; }
result = CheckPurchase(def, cell);
            if (result != PlacementResult.Ok) return false;

            _busy = true;
            try
            {
                if (_econ != null && def.Price > 0 && !_econ.TrySpend(purchaseId, def.Price, def.Id)) { result = PlacementResult.NotEnoughCoins; return false; }
                long now = _clock.UtcNowTicks;
                inst = new BuildingInstanceState
                {
                    instanceId = NewInstanceId(def.Id), typeId = def.Id, cell = cell, level = def.StartLevel,
                    purchaseId = purchaseId,
                };
                if (def.NeedsConstruction)
                {
                    inst.status = BuildingStatus.UnderConstruction;
                    inst.constructionSeconds = def.ConstructionSeconds;
                    inst.constructionStartUtcTicks = now;
                    inst.constructionEndUtcTicks = now + (long)(def.ConstructionSeconds * TimeSpan.TicksPerSecond);
                    inst.completionSeen = false;
                }
                else inst.status = BuildingStatus.Completed;

                _state.instances.Add(inst);
                _grid.Occupy(inst.instanceId, inst.typeId, FarmGrid.RectOf(cell, def.SafeFootprint));
                if (!Save())
                {
                    _state.instances.Remove(inst);
                    _grid.Release(inst.instanceId);
                    if (_econ != null && def.Price > 0) _econ.RefundPurchase(purchaseId);
                    inst = null;
                    result = PlacementResult.Unknown;
                    return false;
                }
                if (_econ != null && def.Price > 0) _econ.ConfirmPurchase(purchaseId);
            }
            finally { _busy = false; }

            InstanceAdded?.Invoke(inst);
            Changed?.Invoke();
            return true;
        }

        public bool TryMove(string instanceId, Vector2Int cell, out PlacementResult result)
        {
            var b = _state.Find(instanceId);
            result = CheckMove(b, cell);
            if (result != PlacementResult.Ok) return false;
            if (b.cell == cell) return true;
            var def = Definition(b.typeId);
            var old = b.cell;
            b.cell = cell;
            _grid.Occupy(b.instanceId, b.typeId, FarmGrid.RectOf(cell, def.SafeFootprint));
            if (!Save())
            {
                b.cell = old;
                _grid.Occupy(b.instanceId, b.typeId, FarmGrid.RectOf(old, def.SafeFootprint));
                result = PlacementResult.Unknown;
                return false;
            }
            InstanceMoved?.Invoke(b);
            Changed?.Invoke();
            return true;
        }

        /// <summary>Puts a storable decoration into the inventory (keeps the instance, frees its cells).</summary>
        public bool TryStore(string instanceId)
        {
            var b = _state.Find(instanceId);
            var def = b != null ? Definition(b.typeId) : null;
            if (def == null || !def.Storable || b.status != BuildingStatus.Completed) return false;
            b.status = BuildingStatus.Stored;
            _grid.Release(b.instanceId);
            if (!Save())
            {
                b.status = BuildingStatus.Completed;
                _grid.Occupy(b.instanceId, b.typeId, FarmGrid.RectOf(b.cell, def.SafeFootprint));
                return false;
            }
            InstanceRemoved?.Invoke(b);
            Changed?.Invoke();
            return true;
        }

        /// <summary>Places a stored decoration again (free).</summary>
        public bool TryPlaceFromInventory(string typeId, Vector2Int cell, out BuildingInstanceState inst, out PlacementResult result)
        {
            inst = FirstStored(typeId);
            if (inst == null) { result = PlacementResult.Unknown; return false; }
            var def = Definition(typeId);
            result = _grid.CheckArea(FarmGrid.RectOf(cell, def.SafeFootprint), null, out _);
            if (result != PlacementResult.Ok) { inst = null; return false; }
            var oldCell = inst.cell;
            inst.cell = cell;
            inst.status = BuildingStatus.Completed;
            _grid.Occupy(inst.instanceId, inst.typeId, FarmGrid.RectOf(cell, def.SafeFootprint));
            if (!Save())
            {
                _grid.Release(inst.instanceId);
                inst.cell = oldCell; inst.status = BuildingStatus.Stored;
                inst = null; result = PlacementResult.Unknown;
                return false;
            }
            InstanceAdded?.Invoke(inst);
            Changed?.Invoke();
            return true;
        }

        /// <summary>Completes due constructions and upgrades (idempotent). Returns how many completed now.</summary>
        public int Tick()
        {
            int n = 0;
            List<BuildingInstanceState> leveled = null;
            for (int i = 0; i < _state.instances.Count; i++)
            {
                var b = _state.instances[i];
                if (!b.IsBuilderBusy || RemainingSeconds(b) > 0.0) continue;
                if (FinishWork(b)) (leveled ??= new List<BuildingInstanceState>()).Add(b);
                n++;
            }
            if (n == 0) return 0;
            Save();
            for (int i = 0; i < _state.instances.Count; i++)
            {
                var b = _state.instances[i];
                string key = b.instanceId + "@" + b.level;
                if (b.status == BuildingStatus.Completed && !b.completionSeen && !_announced.Contains(key))
                {
                    _announced.Add(key);
                    StatusChanged?.Invoke(b);
                }
            }
            if (leveled != null) foreach (var b in leveled) LevelChanged?.Invoke(b, b.level);
            Changed?.Invoke();
            return n;
        }

        /// <summary>Ends the builder job of an instance. Returns true when it was an upgrade (level changed).</summary>
        bool FinishWork(BuildingInstanceState b)
        {
            bool upgrade = b.status == BuildingStatus.Upgrading;
            if (upgrade && b.upgradeTargetLevel > b.level) b.level = b.upgradeTargetLevel;
            b.upgradeTargetLevel = 0;
            b.status = BuildingStatus.Completed;
            b.completionSeen = false;
            return upgrade;
        }

        readonly HashSet<string> _announced = new HashSet<string>();

        // ------------------------------------------------------------------ upgrades

        /// <summary>Everything the UI needs about the next level of an instance.</summary>
        public struct UpgradeInfo
        {
            public bool HasNext;
            public int CurrentLevel, NextLevel, Cost, RequiredHouseLevel;
            public float Seconds;
            public string Benefit;
            public PlacementResult Result;
            /// <summary>House only: mandatory functional buildings of the current level that are not completed yet.</summary>
            public List<FarmBuildingDefinition> MissingBuildings;
        }

        public UpgradeInfo GetUpgrade(BuildingInstanceState b)
        {
            var info = new UpgradeInfo { Result = PlacementResult.Unknown, MissingBuildings = new List<FarmBuildingDefinition>() };
            var def = b != null ? Definition(b.typeId) : null;
            if (def == null) return info;
            info.CurrentLevel = b.level;
            info.NextLevel = b.level + 1;
            if (def.Interaction == BuildingInteraction.FarmerHouse)
            {
                if (_progression != null && _progression.TryGetHouseLevel(info.NextLevel, out var h))
                {
                    info.HasNext = true; info.Cost = h.UpgradeCost; info.Seconds = h.UpgradeSeconds; info.Benefit = h.Unlocks; info.RequiredHouseLevel = b.level;
                }
                MissingMandatory(b.level, info.MissingBuildings);
            }
            else if (info.NextLevel <= def.MaxLevel && def.TryGetUpgrade(info.NextLevel, out var step))
            {
                info.HasNext = true; info.Cost = step.Cost; info.Seconds = step.Seconds; info.Benefit = step.Benefit; info.RequiredHouseLevel = step.RequiredFarmerHouseLevel;
            }
            info.Result = CheckUpgrade(b, info);
            return info;
        }

        /// <summary>Mandatory functional buildings unlocked up to <paramref name="houseLevel"/> without a functional instance.</summary>
        public void MissingMandatory(int houseLevel, List<FarmBuildingDefinition> into)
        {
            into.Clear();
            if (_catalog == null) return;
            foreach (var d in _catalog.Items)
            {
                if (d == null || d.IsDecoration || !d.MandatoryForProgression || !d.Purchasable) continue;
                if (d.RequiredFarmerHouseLevel > houseLevel) continue;
                bool built = false;
                foreach (var b in _state.instances) if (b.typeId == d.Id && b.IsFunctional) { built = true; break; }
                if (!built) into.Add(d);
            }
        }

        public PlacementResult CheckUpgrade(BuildingInstanceState b) => b == null ? PlacementResult.Unknown : GetUpgrade(b).Result;

        PlacementResult CheckUpgrade(BuildingInstanceState b, UpgradeInfo info)
        {
            if (b.status == BuildingStatus.UnderConstruction || b.status == BuildingStatus.Upgrading) return PlacementResult.UnderConstruction;
            if (b.status != BuildingStatus.Completed) return PlacementResult.Unknown;
            if (!info.HasNext) return PlacementResult.MaxLevel;
            if (FarmerHouseLevel < info.RequiredHouseLevel) return PlacementResult.Locked;
            if (info.MissingBuildings != null && info.MissingBuildings.Count > 0) return PlacementResult.RequirementsNotMet;
            if (ActiveConstructionCount >= Mathf.Max(1, _config.MaxConcurrentConstructions)) return PlacementResult.ConstructionBusy;
            if (_econ != null && !_econ.CanAfford(info.Cost)) return PlacementResult.NotEnoughCoins;
            return PlacementResult.Ok;
        }

        /// <summary>
        /// Starts an upgrade with the same atomic receipt flow as purchases: spend (receipt) → instance Upgrading
        /// (farm save) → confirm. A repeated purchase id is a no-op; any failure after spending refunds.
        /// </summary>
        public bool TryStartUpgrade(string instanceId, string purchaseId, out PlacementResult result)
        {
            var b = _state.Find(instanceId);
            if (b == null || string.IsNullOrEmpty(purchaseId)) { result = PlacementResult.Unknown; return false; }
            if (b.upgradePurchaseId == purchaseId) { result = PlacementResult.Ok; _econ?.ConfirmPurchase(purchaseId); return true; }
            if (_busy) { result = PlacementResult.Busy; return false; }
            if (_econ != null && _econ.IsPurchaseKnown(purchaseId) && !_econ.IsPurchasePending(purchaseId)) { result = PlacementResult.Busy; return false; }
            var info = GetUpgrade(b);
            result = info.Result;
            if (result != PlacementResult.Ok) return false;
            _busy = true;
            try
            {
                if (_econ != null && info.Cost > 0 && !_econ.TrySpend(purchaseId, info.Cost, UpgradeItemPrefix + instanceId)) { result = PlacementResult.NotEnoughCoins; return false; }
                var prev = b.Clone();
                long now = _clock.UtcNowTicks;
                b.status = BuildingStatus.Upgrading;
                b.upgradeTargetLevel = info.NextLevel;
                b.upgradePurchaseId = purchaseId;
                b.constructionSeconds = Mathf.Max(0.01f, info.Seconds);
                b.constructionStartUtcTicks = now;
                b.constructionEndUtcTicks = now + (long)(b.constructionSeconds * TimeSpan.TicksPerSecond);
                if (!Save())
                {
                    b.status = prev.status; b.upgradeTargetLevel = prev.upgradeTargetLevel; b.upgradePurchaseId = prev.upgradePurchaseId;
                    b.constructionSeconds = prev.constructionSeconds; b.constructionStartUtcTicks = prev.constructionStartUtcTicks; b.constructionEndUtcTicks = prev.constructionEndUtcTicks;
                    if (_econ != null && info.Cost > 0) _econ.RefundPurchase(purchaseId);
                    result = PlacementResult.Unknown;
                    return false;
                }
                if (_econ != null && info.Cost > 0) _econ.ConfirmPurchase(purchaseId);
            }
            finally { _busy = false; }
            UpgradeStarted?.Invoke(b);
            Changed?.Invoke();
            return true;
        }

        public void MarkCompletionSeen(string instanceId)
        {
            var b = _state.Find(instanceId);
            if (b == null || b.completionSeen) return;
            b.completionSeen = true;
            Save();
        }

        /// <summary>Development: finishes every running construction now.</summary>
        public void DevFinishConstructions()
        {
            foreach (var b in _state.instances)
                if (b.IsBuilderBusy) b.constructionEndUtcTicks = _clock.UtcNowTicks;
            Tick();
        }

        #if UNITY_EDITOR || UNITY_ENABLE_CHECKS
                // ------------------------------------------------------------------ developer interface (compiled out of release builds)

                /// <summary>
                /// Finishes running builder jobs of one kind (construction or upgrade) NOW through the normal completion path
                /// (<see cref="Tick"/>): the instance completes once, the level is applied once, events refresh views, builder
                /// and site, and the farm is saved. Nothing is charged again, no copy is made, no new timer starts.
                /// Returns how many jobs completed.
                /// </summary>
                public int DevFinishJobs(BuildingStatus kind)
                {
                    long now = _clock.UtcNowTicks;
                    int n = 0;
                    foreach (var b in _state.instances)
                        if (b.status == kind && b.IsBuilderBusy) { b.constructionEndUtcTicks = Math.Min(b.constructionEndUtcTicks, now); n++; }
                    if (n == 0) return 0;
                    Tick();
                    return n;
                }

                /// <summary>Moves the running builder job(s) forward by <paramref name="seconds"/> (completes through Tick when due).</summary>
                public int DevAdvanceJobs(double seconds)
                {
                    if (!(seconds > 0)) return 0;
                    long d = (long)(Math.Min(seconds, 3650.0 * 86400.0) * TimeSpan.TicksPerSecond);
                    int n = 0;
                    foreach (var b in _state.instances)
                        if (b.IsBuilderBusy) { b.constructionEndUtcTicks -= d; b.constructionStartUtcTicks -= d; n++; }
                    if (n == 0) return 0;
                    if (Tick() == 0) Save(); // nothing completed yet: persist the shifted timer
                    Changed?.Invoke();
                    return n;
                }
        #endif

                string NewInstanceId(string typeId)
        {
            string id;
            do id = typeId + "_" + Guid.NewGuid().ToString("N").Substring(0, 10);
            while (_state.Find(id) != null);
            return id;
        }

        /// <summary>World centre of an instance's footprint (positions derive from the grid only).</summary>
        public Vector3 WorldCenter(BuildingInstanceState b)
        {
            var def = Definition(b.typeId);
            return _layout.FootprintCenterWorld(b.cell, def != null ? def.SafeFootprint : Vector2Int.one);
        }
    }
}