using System.Collections.Generic;
using ShearAndGrow.Economy;
using ShearAndGrow.Processing;
using UnityEngine;

namespace ShearAndGrow.Farm
{
    /// <summary>Farm Hub ↔ building feature: catalog, placement, edit mode, construction sheets and feedback.</summary>
    public sealed partial class FarmHubController
    {
        bool _editMode;
        bool _devResetting;
        bool _buttonsShown = true;
        readonly List<FarmPanels.SheetAction> _actions = new List<FarmPanels.SheetAction>();

        public bool EditMode => _editMode;

        void WireBuildingEvents()
        {
            var presenter = _director.Presenter;
            presenter.ViewSpawned += (v, it) => { if (it != null && !_interactables.Contains(it)) { _interactables.Add(it); _labels.Add(it); } };
            presenter.ViewDespawned += (v, it) => { if (it != null) { _interactables.Remove(it); _labels.Remove(it); if (_selection.Selected == it) ClosePanels(); } };
            presenter.ViewMoved += (v, it) =>
            {
                _labels.MarkDirty();
                var inst = it != null ? State.Find(it.InstanceId) : null;
                if (inst != null && _director.Service.Definition(inst.typeId)?.Interaction == BuildingInteraction.Barn) PlaceSheepInPen(false);
            };
            _director.ConstructionStarted += (b, def) =>
            {
                Toast("Construction started · " + (def != null ? def.DisplayName : ""));
                _labels.RefreshTexts();
            };
            _director.ConstructionCompleted += OnConstructionCompleted;
            _director.Placement.Finished += OnPlacementFinished;

            // finished while the app was closed: one quiet toast, no animation replay
            var away = _director.Service.CompletedWhileAway;
            if (away.Count > 0)
            {
                var def = _director.Service.Definition(away[0].typeId);
                Toast((def != null ? def.DisplayName : "Building") + " finished while you were away!");
                foreach (var b in away) _director.Service.MarkCompletionSeen(b.instanceId);
            }
        }

        // ------------------------------------------------------------------ sheep pen follows the barn

        Rect PenRect()
        {
            foreach (var b in State.instances)
            {
                if (!b.IsPlaced) continue;
                var def = _director.Service.Definition(b.typeId);
                if (def == null || def.Interaction != BuildingInteraction.Barn) continue;
                var c = _director.Service.WorldCenter(b);
                var p = _layout.BarnPenLocal;
                return new Rect(c.x + p.x, c.z + p.y, p.width, p.height);
            }
            return new Rect(-2f, -2f, 4f, 3f);
        }

        void PlaceSheepInPen(bool initial)
        {
            var pen = PenRect();
            int seed = 1;
            foreach (var kv in _sheepViews)
            {
                var view = kv.Value.View;
                if (view == null) continue;
                if (initial) view.SetPen(pen, seed++);
                view.MoveToPen(pen);
                kv.Value.RefreshRenderers();
            }
        }

        // ------------------------------------------------------------------ helpers

        public void Toast(string text) => _hud.ShowToast(text);

        public bool IsInstanceComplete(string instanceId)
        {
            var b = State != null && instanceId != null ? State.Find(instanceId) : null;
            return b == null || b.status == BuildingStatus.Completed;
        }

        void UpdateBuildButtons()
        {
            if (_buildUI == null || _director == null || _director.Placement == null) return;
            bool show = !_leaving && !_editMode && !_director.Placement.IsActive && !_buildUI.CatalogOpen && !_panels.IsOpen && !_sell.IsOpen;
            if (show == _buttonsShown) return;
            _buttonsShown = show;
            _buildUI.SetMainButtonsVisible(show);
        }

        // ------------------------------------------------------------------ catalog & placement

        public void OpenCatalog()
        {
            if (_leaving) return;
            ClosePanels();
            if (_editMode) SetEditMode(false);
            Play(_open, 0.35f);
            _haptics.ToolStarted();
            _buildUI.OpenCatalog();
        }

        public void OnCatalogClosed() { }

        public void BeginPlacement(FarmBuildingDefinition def, bool fromInventory)
        {
            if (_leaving || def == null) return;
            _buildUI.CloseCatalog();
            ClosePanels();
            if (_director.Placement.BeginPlace(def, fromInventory)) { Play(_tap, 0.7f); _haptics.ToolStarted(); }
            else Toast("Can't place " + def.DisplayName + " right now");
        }

        public void ConfirmPlacement()
        {
            var p = _director.Placement;
            if (!p.IsActive) return;
            var def = p.Definition;
            var mode = p.Mode;
            if (p.Confirm())
            {
                Play(mode == PlacementMode.Move ? _tap : _open, 0.6f);
                _haptics.ToolStarted();
                if (mode == PlacementMode.Move) Toast(def.DisplayName + " moved");
                else if (def.IsDecoration) Toast(def.DisplayName + " placed");
            }
            else Toast(PlacementText.Reason(p.Result, def, _econ.Coins));
        }

        public void CancelPlacement()
        {
            if (!_director.Placement.IsActive) return;
            _director.Placement.Cancel();
            Play(_tap, 0.5f);
        }

        void OnPlacementFinished(bool confirmed, BuildingInstanceState inst)
        {
            if (_director.Placement.IsActive) return; // chain placement continues
            if (_editMode) _director.Overlay.SetVisible(true); // keep the grid while editing
        }

        // ------------------------------------------------------------------ edit mode

        public void SetEditMode(bool on)
        {
            if (on == _editMode) return;
            if (_director.Placement.IsActive) _director.Placement.Cancel();
            _editMode = on;
            ClosePanels();
            if (_buildUI.CatalogOpen) _buildUI.CloseCatalog();
            _buildUI.SetBannerVisible(on);
            _director.Overlay.SetVisible(on);
            Play(_tap, 0.6f);
        }

        void EditSelect(FarmInteractable target)
        {
            if (target is SheepInteraction || target.InstanceId == null) { Toast("Tap a building or decoration to move it"); return; }
            var inst = State.Find(target.InstanceId);
            var r = _director.Service.CheckMove(inst, inst != null ? inst.cell : default);
            if (r == PlacementResult.NotMovable || r == PlacementResult.UnderConstruction)
            {
                Toast(PlacementText.Reason(r, _director.Service.Definition(inst.typeId), _econ.Coins));
                _selection.Pulse(target);
                return;
            }
            StartMove(target.InstanceId);
        }

        void StartMove(string instanceId)
        {
            ClosePanels();
            if (_director.Placement.BeginMove(instanceId)) { Play(_tap, 0.7f); _haptics.ToolStarted(); }
        }

        // ------------------------------------------------------------------ sheets

        void ShowConstruction(BuildingInstanceState inst)
        {
            var svc = _director.Service;
            var def = svc.Definition(inst.typeId);
            var it = _director.Presenter.InteractableOf(inst.instanceId);
            string id = it != null ? it.BuildingId : inst.instanceId;
            string verb = inst.status == BuildingStatus.Upgrading ? " · Upgrading to Lv " + inst.upgradeTargetLevel : " · Building…";
            _panels.ShowConstruction(id, (def != null ? def.DisplayName : "Building") + verb,
                () => svc.Progress(inst), () => svc.RemainingSeconds(inst),
                "The builder is working on it. Moving is possible after it is finished.");
        }

        /// <summary>Building info + Move row under a completed building's own panel.</summary>
        void AppendBuildingRows(FarmInteractable target, BuildingInstanceState inst)
        {
            if (inst == null || !_panels.IsOpen || _panels.OpenId != target.BuildingId) return;
            var def = _director.Service.Definition(inst.typeId);
            if (def == null) return;
            if (def.Interaction != BuildingInteraction.None && def.Interaction != BuildingInteraction.Trader && inst.status == BuildingStatus.Completed)
            {
                if (def.Movable)
                    _panels.AppendAction(new FarmPanels.SheetAction("Move", "Free · keeps level and contents", ProceduralIcons.Kind.Check, UiKit.Air, true, () => StartMove(inst.instanceId)));
            }
        }

        /// <summary>Decoration tap: Move / Store.</summary>
        public void ShowBuildingActions(FarmInteractable target)
        {
            var inst = target.InstanceId != null ? State.Find(target.InstanceId) : null;
            if (inst == null) return;
            var def = _director.Service.Definition(inst.typeId);
            _actions.Clear();
            string id = inst.instanceId;
            if (def.Movable) _actions.Add(new FarmPanels.SheetAction("Move", "Free", ProceduralIcons.Kind.Check, UiKit.Air, true, () => StartMove(id)));
            if (def.Storable) _actions.Add(new FarmPanels.SheetAction("Store", "Put into the inventory · place again for free", ProceduralIcons.Kind.Wool, UiKit.Accent, true, () => StoreDecoration(id)));
            _panels.ShowActions(target.BuildingId, def.DisplayName, def.SafeFootprint.x + "×" + def.SafeFootprint.y + " decoration · " + def.Description, _actions);
        }

        void StoreDecoration(string instanceId)
        {
            var b = State.Find(instanceId);
            var def = b != null ? _director.Service.Definition(b.typeId) : null;
            ClosePanels();
            if (_director.Service.TryStore(instanceId)) { Toast((def != null ? def.DisplayName : "Decoration") + " stored in the inventory"); Play(_tap, 0.6f); }
        }

        public void FocusActiveConstruction()
        {
            var b = _director.Service.ActiveConstruction;
            if (b == null) return;
            var it = _director.Presenter.InteractableOf(b.instanceId);
            if (it != null) Select(it);
        }

        // ------------------------------------------------------------------ completion feedback

        void OnConstructionCompleted(BuildingInstanceState b, FarmBuildingDefinition def)
        {
            bool wasUpgrade = !string.IsNullOrEmpty(b.upgradePurchaseId) && b.level > 1;
            if (!wasUpgrade) Toast((def != null ? def.DisplayName : "Building") + " is ready!");
            Play(_finish, 0.7f);
            _haptics.Completed();
            _labels.RefreshTexts();
            // refresh an open construction sheet into the functional panel
            var it = _director.Presenter.InteractableOf(b.instanceId);
            if (it != null && _panels.IsOpen && _panels.OpenId == it.BuildingId) { _panels.Close(); Select(it); }
        }

        // ------------------------------------------------------------------ QA
        public void QA_OpenCatalog() => OpenCatalog();
        public void QA_Confirm() => ConfirmPlacement();
        public void QA_Cancel() => CancelPlacement();
    }
}