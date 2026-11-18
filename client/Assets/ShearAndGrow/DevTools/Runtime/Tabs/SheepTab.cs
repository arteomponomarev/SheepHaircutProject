using System.Globalization;
using System.Text;
using ShearAndGrow.Economy;
using ShearAndGrow.Farm;
using UnityEngine.UI;

namespace ShearAndGrow.DevTools
{
    /// <summary>
    /// Per-sheep wool tools through the farm's authoritative <see cref="FarmSheepStates"/> debug interface. The regional
    /// mask stays the source of truth (only cut regions change); no command ever credits wool – payment still happens
    /// only for wool that is really sheared, through the existing per-sheep ledger.
    /// </summary>
    public sealed class SheepTab : DevTab
    {
        public override string Title => "Sheep & Wool";

        string _selected;
        Text _list, _info, _diag;
        readonly StringBuilder _sb = new StringBuilder(512);

        static FarmHubController Hub => FarmHubController.Active;
        static string FarmOpen() => Hub != null && Hub.SheepStates != null ? null : "Open the Farm Hub first";

        string Validate(string id, out FarmSheepStates states)
        {
            states = Hub != null ? Hub.SheepStates : null;
            if (states == null) return "Open the Farm Hub first";
            if (string.IsNullOrEmpty(id)) return "Select a sheep first";
            if (states.Find(id) == null) return "Unknown sheep " + id;
            return null;
        }

        DevResult Done(bool ok, string okText, string error)
        {
            Hub?.RefreshSheepVisuals();
            return ok ? DevResult.Success(okText) : DevResult.Fail(error);
        }

        public override void RegisterCommands()
        {
            DevCommands.Register(new DevCommand { Id = "sheep.restore", Label = "Restore Full Wool", Category = Title, Availability = FarmOpen, Execute = id =>
            {
                string v = Validate(id, out var st); if (v != null) return DevResult.Fail(v);
                return Done(st.DevRestoreFullWool(id, out string err), id + ": cut regions regrown to 100% (nothing paid; shear to earn)", err);
            }});
            foreach (int pct in new[] { 0, 25, 50, 75, 100 })
            {
                int p = pct;
                DevCommands.Register(new DevCommand { Id = "sheep.growth." + p, Label = "Set Growth " + p + "%", Category = Title, Availability = FarmOpen, Execute = id =>
                {
                    string v = Validate(id, out var st); if (v != null) return DevResult.Fail(v);
                    return Done(st.DevSetCutRegionGrowth(id, p / 100f, out string err), id + ": cut regions at " + p + "% length (untouched fleece unchanged)", err);
                }});
            }
            DevCommands.Register(new DevCommand { Id = "sheep.preserve.toggle", Label = "Toggle Preserve Hairstyle", Category = Title, Availability = FarmOpen, Execute = id =>
            {
                string v = Validate(id, out var st); if (v != null) return DevResult.Fail(v);
                var e = st.Find(id);
                if (e.State == null) return DevResult.Fail("Never sheared – nothing to preserve");
                bool target = !e.State.hairstylePreserved;
                Hub.SetHairstylePreserved(id, target); // same path as the farm's own Unlock button
                return e.State.hairstylePreserved == target ? DevResult.Success(id + ": hairstyle " + (target ? "preserved (growth paused)" : "unlocked (regrows)")) : DevResult.Fail("Could not save the sheep");
            }});
            DevCommands.Register(new DevCommand { Id = "sheep.boost.clear", Label = "Clear Growth Boost", Category = Title, Availability = FarmOpen, Execute = id =>
            {
                string v = Validate(id, out var st); if (v != null) return DevResult.Fail(v);
                return Done(st.DevClearBoost(id, out string err), id + ": boost cleared", err);
            }});
            DevCommands.Register(new DevCommand { Id = "sheep.boost.apply", Label = "Apply Test Growth Boost", Category = Title, Availability = FarmOpen, Execute = id =>
            {
                string v = Validate(id, out var st); if (v != null) return DevResult.Fail(v);
                return Done(st.DevApplyBoost(id, 4f, 300f, out string err), id + ": test boost ×4 for 5 min (no coins)", err);
            }});
            DevCommands.Register(new DevCommand { Id = "sheep.refresh", Label = "Refresh Visual State", Category = Title, Availability = FarmOpen, Execute = _ =>
            {
                Hub.RefreshSheepVisuals();
                return DevResult.Success("Sheep coats and labels refreshed");
            }});
        }

        public override void Build(DevUi ui)
        {
            if (FarmOpen() != null) { ui.Label("Open the Farm Hub to edit sheep. (Sheep wool is owned by the farm's sheep states.)", 28); return; }
            var infos = Hub.SheepInfos;
            if (string.IsNullOrEmpty(_selected) && infos.Count > 0) _selected = infos[0].SheepId;

            ui.Header("Sheep (" + infos.Count + ")");
            for (int i = 0; i < infos.Count; i += 2)
            {
                var a = infos[i];
                string ida = a.SheepId;
                if (i + 1 < infos.Count)
                {
                    string idb = infos[i + 1].SheepId;
                    ui.Buttons((a.DisplayName + " · " + ida, () => Select(ida), ida == _selected ? DevUi.Good : (UnityEngine.Color?)null),
                               (infos[i + 1].DisplayName + " · " + idb, () => Select(idb), idb == _selected ? DevUi.Good : (UnityEngine.Color?)null));
                }
                else ui.Buttons((a.DisplayName + " · " + ida, () => Select(ida), ida == _selected ? DevUi.Good : (UnityEngine.Color?)null));
            }
            _list = ui.Label("", 24, Processing.UiKit.TextMuted);
            _info = ui.Label("", 30);

            ui.Header("Wool (cut regions only)");
            ui.Button("Restore Full Wool", () => Run("sheep.restore", _selected), DevUi.Good);
            ui.Buttons(("0%", () => Run("sheep.growth.0", _selected)), ("25%", () => Run("sheep.growth.25", _selected)), ("50%", () => Run("sheep.growth.50", _selected)),
                       ("75%", () => Run("sheep.growth.75", _selected)), ("100%", () => Run("sheep.growth.100", _selected)));
            ui.Label("Growth % sets the length of regions this sheep had cut (seen this session). Untouched fleece is never changed. No Dirty Wool is credited – regrown wool pays only when sheared.", 22, Processing.UiKit.TextMuted);

            ui.Header("Hairstyle & boost");
            ui.Buttons(("Toggle Preserve", () => Run("sheep.preserve.toggle", _selected)), ("Refresh Visuals", () => Run("sheep.refresh", _selected)));
            ui.Buttons(("Apply Test Boost ×4", () => Run("sheep.boost.apply", _selected)), ("Clear Boost", () => Run("sheep.boost.clear", _selected)));

            ui.Header("Growth diagnostics");
            _diag = ui.Label("", 24);
        }

        void Select(string id) { _selected = id; Panel.Rebuild(); }

        public override void Refresh()
        {
            if (_info == null || Hub == null || Hub.SheepStates == null) return;
            var states = Hub.SheepStates;
            _sb.Length = 0;
            foreach (var s in Hub.SheepInfos)
                _sb.Append(s.SheepId).Append("  ").Append(s.DisplayName).Append("  ").Append(s.WoolPercent).Append("%")
                   .Append(s.Preserved ? "  [preserved]" : "").Append(s.Boosted ? "  boost ×" + s.BoostMultiplier.ToString("0.#", CultureInfo.InvariantCulture) : "").Append('\n');
            if (_list != null) _list.text = _sb.ToString().TrimEnd();

            var info = Hub.SheepInfo(_selected);
            var e = states.Find(_selected);
            _info.text = e == null ? "No sheep selected" :
                "<b>" + info.DisplayName + "</b>  (" + _selected + ")\n" +
                "Wool " + info.WoolPercent + "%  ·  " + info.StatusText + "\n" +
                "Preserve: " + (info.Preserved ? "ON (no regrowth)" : "off") + "  ·  Boost: " + (info.Boosted ? "×" + info.BoostMultiplier.ToString("0.#", CultureInfo.InvariantCulture) + ", " + FarmSheepInfo.FormatEta(info.BoostSecondsLeft) + " left" : "none") +
                (e.State != null ? "\nCycle " + Short(e.State.cycleId) + "  ·  removed this cycle " + (e.State.lifetimeRemovedWool * 100f).ToString("0.#", CultureInfo.InvariantCulture) + "%  ·  paid " + (WoolEconomy.IsReady ? WoolEconomy.Instance.GivenUnits(_selected, e.State.cycleId) : 0) + " Dirty" : "");
            _info.supportRichText = true;

            if (_diag == null) return;
            var d = states.DevDiagnostics(_selected);
            if (!d.Known) { _diag.text = "–"; return; }
            _sb.Length = 0;
            _sb.Append("Growth mode: ").Append(d.Mode).Append(d.Mode == FarmSheepStates.WoolGrowthTimeModeView.RealTime ? " (grows while the app is closed)" : " (grows only while the app runs)").Append('\n');
            _sb.Append("Effective current time: ").Append(Clock(d.Now, d.Mode)).Append('\n');
            if (!d.HasSave) { _sb.Append("State: ").Append(d.Reason); _diag.text = _sb.ToString(); return; }
            _sb.Append("Last growth update: ").Append(Clock(d.Stamp, d.Mode)).Append('\n');
            _sb.Append("Elapsed since update: ").Append(d.Elapsed.ToString("0.0", CultureInfo.InvariantCulture)).Append(" s  ·  carry ").Append(d.Carry.ToString("0.0", CultureInfo.InvariantCulture)).Append(" s\n");
            _sb.Append("Growth multiplier: ×").Append(d.EffectiveMultiplier.ToString("0.##", CultureInfo.InvariantCulture)).Append(" (debug)")
               .Append(d.BoostLeft > 0 ? "  · boost ×" + d.BoostMultiplier.ToString("0.#", CultureInfo.InvariantCulture) + " for " + FarmSheepInfo.FormatEta((float)d.BoostLeft) : "").Append('\n');
            _sb.Append("Preserve: ").Append(d.Preserved ? "ON" : "off").Append("  ·  regrowth enabled: ").Append(d.RegrowthEnabled ? "yes" : "NO").Append('\n');
            _sb.Append("Calculated growth delta: ").Append(d.PendingSteps).Append(" step(s) pending (1 step = ").Append(d.SecondsPerStep.ToString("0.00", CultureInfo.InvariantCulture)).Append(" s, 255 = full)\n");
            _sb.Append("Wool ").Append((d.WoolAmount * 100f).ToString("0.0", CultureInfo.InvariantCulture)).Append("%  ·  cut texels ").Append(d.CutTexels).Append(" (known cut region ").Append(d.ObservedCutTexels).Append(")")
               .Append(d.SecondsToFull > 0 ? "  ·  full in " + FarmSheepInfo.FormatEta(d.SecondsToFull) : "").Append('\n');
            _sb.Append("On-disk stamp: ").Append(Clock(d.DiskStamp, d.Mode)).Append(d.DiskElapsed >= 0 ? "  (" + FarmSheepInfo.FormatEta((float)d.DiskElapsed) + " ago – applied on next load)" : "  (other timebase – converted on load)").Append('\n');
            _sb.Append("Why: ").Append(d.Reason);
            _diag.text = _sb.ToString();
        }

        static string Short(string id) => string.IsNullOrEmpty(id) ? "–" : id.Length > 8 ? id.Substring(0, 8) : id;

        static string Clock(double v, FarmSheepStates.WoolGrowthTimeModeView mode)
        {
            if (v < 0) return "unstamped";
            return mode == FarmSheepStates.WoolGrowthTimeModeView.RealTime
                ? WoolGrowthClock.RealTimeToUtc(v).ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) + " UTC"
                : v.ToString("N1", CultureInfo.InvariantCulture) + " game-s";
        }
    }
}
