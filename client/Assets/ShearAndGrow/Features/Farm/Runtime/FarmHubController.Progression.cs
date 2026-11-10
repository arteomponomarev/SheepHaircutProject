using System.Collections.Generic;
using System.Text;
using ShearAndGrow.Economy;
using ShearAndGrow.Processing;
using ShearAndGrow.Shearing;
using UnityEngine;

namespace ShearAndGrow.Farm
{
    /// <summary>Farm Hub ↔ Stage 5 progression: Farmer House levels, barn herd & purchase, trimmer workshop, feeding.</summary>
    public sealed partial class FarmHubController
    {
        [Header("Progression")]
        [Tooltip("Farm sheep prefab (FarmSheepView + SheepInteraction + hit area), spawned per herd entry.")]
        [SerializeField] private GameObject _sheepPrefab;
        [SerializeField] private ToolUpgradeConfig _tools;

        HerdService _herd;
        FeedingService _feeding;
        readonly List<FarmPanels.SheetAction> _rowsTmp = new List<FarmPanels.SheetAction>();
        readonly StringBuilder _sb = new StringBuilder(128);
        string _housePurchaseId, _barnPurchaseId, _sheepPurchaseId;

        public HerdService Herd => _herd;
        public FeedingService Feeding => _feeding;
        FarmProgressionConfig Prog => _director.Progression;

        void InitProgression()
        {
            _herd = new HerdService(_director.Service, Prog);
            _feeding = new FeedingService(_director.Service, _states, Prog);
            _feeding.Reconcile(_econ);
            _herd.SheepAdded += OnSheepBought;
            _director.UpgradeStarted += (b, def) => { Toast((def != null ? def.DisplayName : "Building") + " upgrade started"); _labels.RefreshTexts(); };
            _director.Service.LevelChanged += OnLevelChanged;
        }

        // ------------------------------------------------------------------ shared helpers

        public bool CanUpgrade(string instanceId)
        {
            var b = State != null && instanceId != null ? State.Find(instanceId) : null;
            return b != null && _director.Service.CheckUpgrade(b) == PlacementResult.Ok;
        }

        public string HerdText => _herd != null ? _herd.Count + "/" + _herd.Capacity + " sheep" : "";

        string UpgradeReason(BuildingService.UpgradeInfo u)
        {
            switch (u.Result)
            {
                case PlacementResult.Ok: return "Ready to upgrade";
                case PlacementResult.MaxLevel: return "Maximum level for now";
                case PlacementResult.Locked: return "Needs Farmer House Lv " + u.RequiredHouseLevel;
                case PlacementResult.UnderConstruction: return "The builder is working on it";
                case PlacementResult.ConstructionBusy: return "The builder is busy with another building";
                case PlacementResult.NotEnoughCoins: return "Not enough coins: need " + System.Math.Max(0, u.Cost - _econ.Coins) + " more";
                case PlacementResult.RequirementsNotMet:
                    return u.MissingBuildings.Count > 0 ? "Build " + u.MissingBuildings[0].DisplayName + " first" : "Requirements not met";
                default: return "Not available";
            }
        }

        void Feedback(Vector3 at, float half = 1f)
        {
            Play(_finish, 0.55f);
            _haptics.Completed();
            if (_director.Site != null) _director.Site.Sparkle(at, new Vector2(half, half));
        }

        void RefreshAfterChange(FarmInteractable target)
        {
            _labels.RefreshTexts();
            if (target != null && _panels.IsOpen && _panels.OpenId == target.BuildingId) { target.OnTap(this); AppendBuildingRows(target, State.Find(target.InstanceId)); }
        }

        // ------------------------------------------------------------------ Farmer House

        public void ShowHouseProgress(FarmInteractable house)
        {
            var svc = _director.Service;
            var inst = State.Find(house.InstanceId);
            if (inst == null) return;
            var u = svc.GetUpgrade(inst);
            _rowsTmp.Clear();
            _sb.Length = 0;
            _sb.Append("Level ").Append(inst.level);
            if (u.HasNext) _sb.Append("  ·  Next: Lv ").Append(u.NextLevel).Append("  ·  ").Append(u.Cost).Append(" coins  ·  ").Append(PlacementText.Time(u.Seconds));
            else _sb.Append("  ·  More levels in future updates");
            if (u.HasNext)
            {
                _rowsTmp.Add(new FarmPanels.SheetAction("Unlocks at Lv " + u.NextLevel, u.Benefit, ProceduralIcons.Kind.Check, UiKit.Air, false, null));
                // mandatory buildings of the current level: each one listed with its state
                var all = new List<FarmBuildingDefinition>();
                foreach (var d in svc.Catalog.Items)
                    if (d != null && !d.IsDecoration && d.MandatoryForProgression && d.Purchasable && d.RequiredFarmerHouseLevel <= inst.level) all.Add(d);
                foreach (var d in all)
                {
                    bool built = !u.MissingBuildings.Contains(d);
                    _rowsTmp.Add(new FarmPanels.SheetAction((built ? "[ok] " : "[  ] ") + d.DisplayName, built ? "Built" : "Required: build it first", ProceduralIcons.Kind.Check, built ? UiKit.Air : UiKit.Accent, false, null));
                }
                bool ok = u.Result == PlacementResult.Ok;
                string id = inst.instanceId;
                _rowsTmp.Add(new FarmPanels.SheetAction("Upgrade to Lv " + u.NextLevel + " · " + u.Cost, ok ? "Takes " + PlacementText.Time(u.Seconds) + " · the builder will start right away" : UpgradeReason(u),
                    ProceduralIcons.Kind.Coin, UiKit.Coin, ok, () => StartUpgrade(id, ref _housePurchaseId)));
            }
            _panels.ShowActions(house.BuildingId, house.DisplayName, _sb.ToString(), _rowsTmp);
        }

        /// <summary>One purchase id per open sheet: a double tap reuses it → the service charges once.</summary>
        void StartUpgrade(string instanceId, ref string purchaseId)
        {
            if (string.IsNullOrEmpty(purchaseId)) purchaseId = System.Guid.NewGuid().ToString("N");
            if (_director.Service.TryStartUpgrade(instanceId, purchaseId, out var r))
            {
                var it = _director.Presenter.InteractableOf(instanceId);
                Play(_open, 0.6f); _haptics.ToolStarted();
                purchaseId = null;
                if (it != null) { _panels.Close(); Select(it); }
            }
            else Toast(PlacementText.Reason(r, null, _econ.Coins));
        }

        void OnLevelChanged(BuildingInstanceState b, int level)
        {
            var def = _director.Service.Definition(b.typeId);
            Toast((def != null ? def.DisplayName : "Building") + " reached level " + level + "!");
            if (def != null && def.Interaction == BuildingInteraction.FarmerHouse && Prog != null && Prog.TryGetHouseLevel(level, out var h) && !string.IsNullOrEmpty(h.Unlocks))
                Toast("Farmer House Lv " + level + ": " + h.Unlocks + " unlocked!", 3.5f);
            _labels.RefreshTexts();
            if (_buildUI.CatalogOpen) _buildUI.RefreshCards();
        }

        void Toast(string s, float seconds) => _hud.ShowToast(s, seconds);

        // ------------------------------------------------------------------ Barn & herd

        public void ShowBarnHerd(FarmInteractable barn)
        {
            var inst = State.Find(barn.InstanceId);
            if (inst == null) return;
            _rowsTmp.Clear();
            foreach (var info in _sheep)
            {
                string sid = info.SheepId;
                _rowsTmp.Add(new FarmPanels.SheetAction(info.DisplayName + (info.Preserved ? "  [style]" : "") + (info.Boosted ? "  x" + info.BoostMultiplier.ToString("0.#") : ""),
                    "Wool " + info.WoolPercent + "%  ·  " + info.StatusText, ProceduralIcons.Kind.Scissors, UiKit.Accent, true, () => StartShearing(sid)));
            }
            var buy = _herd.CanBuy();
            string buySub = buy == HerdResult.Ok ? "A new sheep with a full fleece · " + (_herd.Capacity - _herd.Count) + " free place(s)"
                          : buy == HerdResult.BarnFull ? "Barn full (" + _herd.Count + "/" + _herd.Capacity + ")" + (inst.level < _barnMaxLevel(inst) ? " – upgrade the barn" : "")
                          : buy == HerdResult.NotEnoughCoins ? "Not enough coins: need " + System.Math.Max(0, _herd.Price - _econ.Coins) + " more" : "Not available";
            _rowsTmp.Add(new FarmPanels.SheetAction("Buy Sheep · " + _herd.Price, buySub, ProceduralIcons.Kind.Wool, UiKit.Coin, buy == HerdResult.Ok, BuySheep));
            var u = _director.Service.GetUpgrade(inst);
            if (u.HasNext)
            {
                bool ok = u.Result == PlacementResult.Ok;
                string id = inst.instanceId;
                int cap = Prog != null ? Prog.BarnCapacity(u.NextLevel) : 4;
                _rowsTmp.Add(new FarmPanels.SheetAction("Upgrade Barn → Lv " + u.NextLevel + " · " + u.Cost, ok ? "Room for " + cap + " sheep · " + PlacementText.Time(u.Seconds) : UpgradeReason(u),
                    ProceduralIcons.Kind.Coin, UiKit.Coin, ok, () => StartUpgrade(id, ref _barnPurchaseId)));
            }
            string state = inst.status == BuildingStatus.Upgrading ? "  ·  upgrading (" + PlacementText.Time(_director.Service.RemainingSeconds(inst)) + ")" : "";
            _panels.ShowActions(barn.BuildingId, barn.DisplayName, "Lv " + inst.level + "  ·  Sheep " + _herd.Count + "/" + _herd.Capacity + state, _rowsTmp);
        }

        int _barnMaxLevel(BuildingInstanceState b) { var d = _director.Service.Definition(b.typeId); return d != null ? d.MaxLevel : 1; }

        void BuySheep()
        {
            if (string.IsNullOrEmpty(_sheepPurchaseId)) _sheepPurchaseId = System.Guid.NewGuid().ToString("N");
            var r = _herd.TryBuySheep(_sheepPurchaseId, out var entry);
            if (r == HerdResult.Ok) { _sheepPurchaseId = null; return; } // OnSheepBought handles views & feedback
            Toast(r == HerdResult.BarnFull ? "The barn is full" : r == HerdResult.NotEnoughCoins ? "Not enough coins" : "Could not buy a sheep right now");
        }

        void OnSheepBought(HerdEntry e)
        {
            _states.Add(e.sheepId, e.displayName);
            RefreshSheepInfos();
            var it = SpawnSheep(e, true);
            if (it != null) { _labels.Add(it); Feedback(it.transform.position, 0.6f); }
            Toast(e.displayName + " joined your flock!");
            var barn = _director.Presenter.InteractableOf(_director.Service.FirstWithInteraction(BuildingInteraction.Barn)?.instanceId);
            RefreshAfterChange(barn);
        }

        // ------------------------------------------------------------------ herd views

        void SpawnHerdViews()
        {
            if (_sheepPrefab == null)
            {
                // legacy scene sheep (scene built before Stage 5)
                foreach (var s in _interactablesRoot.GetComponentsInChildren<SheepInteraction>(true))
                {
                    _interactables.Add(s);
                    _sheepViews[s.SheepId] = s;
                    if (s.View != null) { s.View.Build(); s.RefreshRenderers(); }
                }
                return;
            }
            int max = Prog != null ? Prog.MaxVisibleSheep : 6;
            int n = 0;
            foreach (var e in _director.Service.State.herd) { if (n++ >= max) break; SpawnSheep(e, false); }
        }

        SheepInteraction SpawnSheep(HerdEntry e, bool fresh)
        {
            if (_sheepPrefab == null || _sheepViews.ContainsKey(e.sheepId)) return null;
            if (Prog != null && _sheepViews.Count >= Prog.MaxVisibleSheep) return null;
            var go = Instantiate(_sheepPrefab, _interactablesRoot);
            go.name = "Sheep " + e.displayName;
            var view = go.GetComponent<FarmSheepView>();
            var it = go.GetComponent<SheepInteraction>();
            var anchor = go.transform.Find("LabelAnchor");
            it.Configure(e.sheepId, e.displayName, go.transform, anchor);
            it.ConfigureSheep(e.sheepId, view);
            var pen = PenRect();
            int idx = _sheepViews.Count;
            go.transform.position = new Vector3(pen.xMin + pen.width * (0.25f + 0.5f * (idx % 2)), 0f, pen.yMin + pen.height * (0.3f + 0.4f * ((idx / 2) % 2)));
            view.SetPen(pen, SheepNumberSeed(e.sheepId));
            view.Build();
            it.RefreshRenderers();
            _sheepViews[e.sheepId] = it;
            if (!_interactables.Contains(it)) _interactables.Add(it);
            var st = _states.Find(e.sheepId);
            if (st != null && st.Mask != null) view.SetMask(st.Mask, true); else view.SetRemainingWool(1f);
            if (fresh) go.transform.localScale = Vector3.one * 0.6f; // grows in (pop)
            if (fresh) StartCoroutine(GrowIn(go.transform));
            return it;
        }

        static int SheepNumberSeed(string id) { int n = BuildingService.SheepNumber(id); return n > 0 ? n : id.GetHashCode(); }

        System.Collections.IEnumerator GrowIn(Transform t)
        {
            float a = 0f;
            while (a < 1f && t != null)
            {
                a = Mathf.Min(1f, a + Time.unscaledDeltaTime / 0.45f);
                float s = Mathf.LerpUnclamped(0.6f, 1f, 1f + 2.2f * Mathf.Pow(a - 1f, 3f) + 1.2f * Mathf.Pow(a - 1f, 2f));
                t.localScale = Vector3.one * s;
                yield return null;
            }
            if (t != null) t.localScale = Vector3.one;
        }

        // ------------------------------------------------------------------ Trimmer Workshop

        public bool CanUpgradeTrimmer
        {
            get
            {
                if (_tools == null) return false;
                int lvl = _econ.ToolLevel(_tools.ToolId);
                return lvl < _tools.MaxLevel && _tools.TryGet(lvl + 1, out var next) && _econ.CanAfford(next.Cost);
            }
        }

        public void ShowWorkshop(FarmInteractable ws)
        {
            var inst = State.Find(ws.InstanceId);
            if (inst == null || _tools == null) return;
            if (!inst.IsFunctional) return;
            int lvl = _econ.ToolLevel(_tools.ToolId);
            _tools.TryGet(lvl, out var cur);
            _rowsTmp.Clear();
            _rowsTmp.Add(new FarmPanels.SheetAction(_tools.DisplayName + " · Lv " + lvl, cur.Description + " · cutting width " + Mathf.RoundToInt(cur.RadiusMultiplier * 100f) + "%",
                ProceduralIcons.Kind.Scissors, UiKit.Accent, false, null));
            if (lvl < _tools.MaxLevel && _tools.TryGet(lvl + 1, out var next))
            {
                bool afford = _econ.CanAfford(next.Cost);
                _rowsTmp.Add(new FarmPanels.SheetAction("Upgrade to Lv " + next.Number + " · " + next.Cost,
                    afford ? "Cutting width " + Mathf.RoundToInt(cur.RadiusMultiplier * 100f) + "% → " + Mathf.RoundToInt(next.RadiusMultiplier * 100f) + "% · " + next.Description
                           : "Not enough coins: need " + System.Math.Max(0, next.Cost - _econ.Coins) + " more",
                    ProceduralIcons.Kind.Coin, UiKit.Coin, afford, () => UpgradeTrimmer(ws, lvl, next)));
            }
            else _rowsTmp.Add(new FarmPanels.SheetAction("Maximum level", "More tools and heads in future updates", ProceduralIcons.Kind.Check, UiKit.Air, false, null));
            _panels.ShowActions(ws.BuildingId, ws.DisplayName, "Improve your shearing tools. Upgrades apply to every sheep.", _rowsTmp);
        }

        void UpgradeTrimmer(FarmInteractable ws, int fromLevel, ToolUpgradeConfig.Level next)
        {
            // idempotent: only applies while the owned level is still fromLevel → a double tap never charges twice
            if (_econ.TryUpgradeTool(_tools.ToolId, fromLevel, next.Number, next.Cost))
            {
                Feedback(ws.ViewBounds.center, 1.2f);
                Toast(_tools.DisplayName + " upgraded to Lv " + next.Number + "! Wider cutting head.");
            }
            else if (_econ.ToolLevel(_tools.ToolId) != fromLevel) { /* already applied by the first tap */ }
            else Toast("Not enough coins");
            RefreshAfterChange(ws);
        }

        // ------------------------------------------------------------------ Feeding Station

        string _feedPurchaseId; string _feedSheep;

        public void ShowFeeding(FarmInteractable station)
        {
            var inst = State.Find(station.InstanceId);
            if (inst == null || !inst.IsFunctional) return;
            _rowsTmp.Clear();
            foreach (var info in _sheep)
            {
                string sid = info.SheepId;
                var r = _feeding.CanFeed(sid);
                string sub;
                switch (r)
                {
                    case FeedResult.Ok: sub = "Wool " + info.WoolPercent + "% · x" + _feeding.Multiplier.ToString("0.#") + " regrowth for " + PlacementText.Time(_feeding.Seconds); break;
                    case FeedResult.Preserved: sub = "Hairstyle saved – unlock it to feed (no coins taken)"; break;
                    case FeedResult.FullFleece: sub = "Full fleece – nothing to regrow"; break;
                    case FeedResult.AlreadyBoosted: sub = "x" + info.BoostMultiplier.ToString("0.#") + " active · " + PlacementText.Time(info.BoostSecondsLeft) + " left"; break;
                    case FeedResult.NotEnoughCoins: sub = "Not enough coins (" + _feeding.Price + ")"; break;
                    default: sub = "Not available"; break;
                }
                _rowsTmp.Add(new FarmPanels.SheetAction(info.DisplayName + (r == FeedResult.Ok ? "  · Feed " + _feeding.Price : ""), sub, ProceduralIcons.Kind.Wool, UiKit.Air, r == FeedResult.Ok, () => Feed(station, sid)));
            }
            _panels.ShowActions(station.BuildingId, station.DisplayName, "Optional treat: speeds up wool regrowth of one sheep for a while.", _rowsTmp);
        }

        void Feed(FarmInteractable station, string sheepId)
        {
            if (_feedSheep != sheepId || string.IsNullOrEmpty(_feedPurchaseId)) { _feedPurchaseId = System.Guid.NewGuid().ToString("N"); _feedSheep = sheepId; }
            var r = _feeding.TryFeed(sheepId, _feedPurchaseId);
            if (r == FeedResult.Ok)
            {
                _feedPurchaseId = null;
                RefreshSheepInfos();
                if (_sheepViews.TryGetValue(sheepId, out var it) && it.View != null)
                {
                    it.View.PlayEat(2.2f);
                    Feedback(it.transform.position, 0.5f);
                }
                else Feedback(station.ViewBounds.center, 0.8f);
                Toast(SheepInfo(sheepId).DisplayName + " enjoyed the treat! Wool grows x" + _feeding.Multiplier.ToString("0.#"));
            }
            else Toast(r == FeedResult.Preserved ? "Hairstyle saved – unlock it first" : r == FeedResult.NotEnoughCoins ? "Not enough coins" : "Can't feed right now");
            RefreshAfterChange(station);
        }

        // ------------------------------------------------------------------ QA
        public void QA_UpgradeHouse() { var h = _director.Service.FirstWithInteraction(BuildingInteraction.FarmerHouse); if (h != null) StartUpgrade(h.instanceId, ref _housePurchaseId); }
        public void QA_UpgradeBarn() { var b = _director.Service.FirstWithInteraction(BuildingInteraction.Barn); if (b != null) StartUpgrade(b.instanceId, ref _barnPurchaseId); }
        public void QA_BuySheep() => BuySheep();
        public void QA_Feed(string sheepId) { var st = _director.Service.FirstWithInteraction(BuildingInteraction.FeedingStation); var it = st != null ? _director.Presenter.InteractableOf(st.instanceId) : null; if (it != null) Feed(it, sheepId); }
        public void QA_UpgradeTrimmer() { var w = _director.Service.FirstWithInteraction(BuildingInteraction.TrimmerWorkshop); var it = w != null ? _director.Presenter.InteractableOf(w.instanceId) : null; int l = _econ.ToolLevel(_tools.ToolId); if (it != null && _tools.TryGet(l + 1, out var n)) UpgradeTrimmer(it, l, n); }
        public int VisibleSheepCount => _sheepViews.Count;
    }
}