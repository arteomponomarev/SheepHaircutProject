using System.Text;
using ShearAndGrow.Economy;
using ShearAndGrow.Farm;
using UnityEngine.UI;

namespace ShearAndGrow.DevTools
{
    /// <summary>
    /// Construction / upgrade tools through <see cref="BuildingService"/>'s normal completion path (Tick): one completion,
    /// no second charge, no copy, no new timer; builder NPC, site dressing, labels and sheets refresh via the usual events.
    /// </summary>
    public sealed class BuildingsTab : DevTab
    {
        public override string Title => "Buildings";

        Text _jobs, _house;
        readonly StringBuilder _sb = new StringBuilder(512);

        static FarmHubController Hub => FarmHubController.Active;
        static BuildingService Svc => Hub != null && Hub.Buildings != null ? Hub.Buildings.Service : null;
        static string FarmOpen() => Svc != null ? null : "Open the Farm Hub first";

        static DevResult AfterJobChange(int n, string what)
        {
            if (Hub != null) { Hub.Buildings.RefreshConstructionVisuals(); Hub.RefreshBuildingUi(); }
            return n > 0 ? DevResult.Success(what + " (" + n + ")") : DevResult.Fail("Nothing to do");
        }

        public override void RegisterCommands()
        {
            DevCommands.Register(new DevCommand { Id = "buildings.finishConstruction", Label = "Finish Current Construction", Category = Title, Availability = FarmOpen,
                Execute = _ => AfterJobChange(Svc.DevFinishJobs(BuildingStatus.UnderConstruction), "Construction finished") });
            DevCommands.Register(new DevCommand { Id = "buildings.finishUpgrade", Label = "Finish Current Upgrade", Category = Title, Availability = FarmOpen,
                Execute = _ => AfterJobChange(Svc.DevFinishJobs(BuildingStatus.Upgrading), "Upgrade finished") });
            DevCommands.Register(new DevCommand { Id = "buildings.advance", Label = "Advance Construction Time", Category = Title, Availability = FarmOpen, Execute = arg =>
            {
                if (!DevCommands.TryParseDuration(arg, out double s, out string err)) return DevResult.Fail(err);
                return AfterJobChange(Svc.DevAdvanceJobs(s), "Builder job advanced by " + PlacementText.Time(s));
            }});
            DevCommands.Register(new DevCommand { Id = "buildings.refresh", Label = "Refresh Building Visuals", Category = Title, Availability = FarmOpen, Execute = _ =>
            {
                Hub.Buildings.RefreshConstructionVisuals();
                Hub.RefreshBuildingUi();
                return DevResult.Success("Building visuals, site and labels refreshed");
            }});
            DevCommands.Register(new DevCommand { Id = "buildings.house.levelUp", Label = "Farmer House +1 (rules-checked)", Category = Title, Availability = FarmOpen, Execute = _ =>
            {
                // Safe test operation: the REAL upgrade flow (requirements, builder queue, coin receipt) followed by an
                // instant finish – the house level and every derived unlock stay consistent with the progression rules.
                var svc = Svc;
                var house = svc.FirstWithInteraction(BuildingInteraction.FarmerHouse);
                if (house == null) return DevResult.Fail("No Farmer House");
                var u = svc.GetUpgrade(house);
                if (u.Result != PlacementResult.Ok) return DevResult.Fail("Not allowed by progression rules: " + u.Result +
                    (u.MissingBuildings.Count > 0 ? " (build " + u.MissingBuildings[0].DisplayName + ")" : "") + (u.Result == PlacementResult.NotEnoughCoins ? " – add coins in Economy" : ""));
                if (!svc.TryStartUpgrade(house.instanceId, System.Guid.NewGuid().ToString("N"), out var r)) return DevResult.Fail("Upgrade refused: " + r);
                svc.DevFinishJobs(BuildingStatus.Upgrading);
                if (Hub != null) { Hub.Buildings.RefreshConstructionVisuals(); Hub.RefreshBuildingUi(); }
                return DevResult.Success("Farmer House is now Lv " + svc.FarmerHouseLevel + " (paid " + u.Cost + " coins through the normal receipt)");
            }});
        }

        public override void Build(DevUi ui)
        {
            if (FarmOpen() != null) { ui.Label("Open the Farm Hub to use building tools.", 28); return; }
            ui.Header("Construction & upgrades");
            _jobs = ui.Label("", 28);
            ui.Buttons(("Finish Construction", () => Run("buildings.finishConstruction")), ("Finish Upgrade", () => Run("buildings.finishUpgrade")));
            ui.Buttons(("+1 min", () => Run("buildings.advance", "1m")), ("+10 min", () => Run("buildings.advance", "10m")), ("+1 h", () => Run("buildings.advance", "1h")));
            ui.InputWithButton("Duration e.g. 90s, 15m, 2h", InputField.ContentType.Standard, "Advance", v => Run("buildings.advance", v));
            ui.Button("Refresh Building Visuals", () => Run("buildings.refresh"));

            ui.Header("Overlays");
            ui.Toggle("Show building grid + occupied cells", DevWorldOverlay.ShowGrid, v => DevWorldOverlay.ShowGrid = v);
            ui.Toggle("Show building IDs", DevWorldOverlay.ShowIds, v => DevWorldOverlay.ShowIds = v);
            ui.Toggle("Show building levels", DevWorldOverlay.ShowLevels, v => DevWorldOverlay.ShowLevels = v);
            ui.Toggle("Show construction timers", DevWorldOverlay.ShowTimers, v => DevWorldOverlay.ShowTimers = v);

            ui.Header("Farmer House");
            _house = ui.Label("", 26);
            ui.Button("Farmer House +1 level (real upgrade rules, instant finish)", () => Run("buildings.house.levelUp"), DevUi.Good);
            ui.Label("Uses the normal upgrade flow: requirements and coins are checked and charged once; dependent unlocks follow automatically. Lowering the level is not offered (would contradict built content).", 22, Processing.UiKit.TextMuted);
        }

        public override void Refresh()
        {
            var svc = Svc;
            if (svc == null || _jobs == null) return;
            _sb.Length = 0;
            int n = 0;
            foreach (var b in svc.State.instances)
            {
                if (!b.IsBuilderBusy) continue;
                n++;
                var d = svc.Definition(b.typeId);
                _sb.Append(b.status == BuildingStatus.Upgrading ? "Upgrading " : "Building ").Append(d != null ? d.DisplayName : b.typeId)
                   .Append(b.status == BuildingStatus.Upgrading ? " → Lv " + b.upgradeTargetLevel : "")
                   .Append("  ·  ").Append(UnityEngine.Mathf.RoundToInt(svc.Progress(b) * 100f)).Append("%  ·  ").Append(PlacementText.Time(svc.RemainingSeconds(b))).Append(" left\n");
            }
            if (n == 0) _sb.Append("Builder idle – no construction or upgrade running.");
            _jobs.text = _sb.ToString().TrimEnd();

            if (_house == null) return;
            _sb.Length = 0;
            var house = svc.FirstWithInteraction(BuildingInteraction.FarmerHouse);
            int lvl = svc.FarmerHouseLevel;
            _sb.Append("Current level: ").Append(lvl);
            if (svc.Progression != null) _sb.Append(" / max ").Append(svc.Progression.MaxHouseLevel);
            _sb.Append('\n');
            if (house != null)
            {
                var u = svc.GetUpgrade(house);
                if (u.HasNext)
                {
                    _sb.Append("Next: Lv ").Append(u.NextLevel).Append(" · ").Append(u.Cost).Append(" coins · ").Append(PlacementText.Time(u.Seconds)).Append(" · status ").Append(u.Result).Append('\n');
                    if (!string.IsNullOrEmpty(u.Benefit)) _sb.Append("Unlocks: ").Append(u.Benefit).Append('\n');
                    foreach (var m in u.MissingBuildings) _sb.Append("  missing: ").Append(m.DisplayName).Append('\n');
                }
                else _sb.Append("No further level configured.\n");
            }
            _sb.Append("Unlocked buildings: ");
            bool first = true;
            if (svc.Catalog != null)
                foreach (var d in svc.Catalog.Items)
                    if (d != null && d.Purchasable && d.IsUnlocked(lvl)) { _sb.Append(first ? "" : ", ").Append(d.DisplayName); first = false; }
            _sb.Append("\nLocked: ");
            first = true;
            if (svc.Catalog != null)
                foreach (var d in svc.Catalog.Items)
                    if (d != null && d.Purchasable && !d.IsUnlocked(lvl)) { _sb.Append(first ? "" : ", ").Append(d.DisplayName).Append(" (Lv ").Append(d.RequiredFarmerHouseLevel).Append(")"); first = false; }
            if (first) _sb.Append("none");
            _house.text = _sb.ToString();
        }
    }
}
