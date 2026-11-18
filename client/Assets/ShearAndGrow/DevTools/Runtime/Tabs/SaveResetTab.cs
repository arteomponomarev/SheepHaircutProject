using System;
using System.Globalization;
using System.IO;
using System.Text;
using ShearAndGrow.Economy;
using ShearAndGrow.Farm;
using ShearAndGrow.Shearing;
using UnityEngine;
using UnityEngine.UI;

namespace ShearAndGrow.DevTools
{
    /// <summary>
    /// Save tools for the LOCAL test save of this install (PlayFab is not integrated – there is no server account to
    /// touch). Reset Farm needs two steps: a warning and typing RESET.
    /// </summary>
    public sealed class SaveResetTab : DevTab
    {
        public const string ConfirmToken = "RESET";
        public override string Title => "Save & Reset";

        bool _confirming;
        InputField _confirmField;
        Text _status, _exportInfo;
        readonly StringBuilder _sb = new StringBuilder(1024);

        static FarmHubController Hub => FarmHubController.Active;

        public override void RegisterCommands()
        {
            DevCommands.Register(new DevCommand { Id = "save.resetFarm", Label = "Reset Farm To Fresh Save", Category = Title, Destructive = true,
                Availability = () => DevBackends.FarmReset.Availability(), Execute = arg =>
            {
                if (!string.Equals((arg ?? "").Trim(), ConfirmToken, StringComparison.Ordinal)) return DevResult.Fail("Type " + ConfirmToken + " to confirm");
                _confirming = false;
                return DevBackends.FarmReset.ResetToFreshSave();
            }});
            DevCommands.Register(new DevCommand { Id = "save.reload", Label = "Reload Current Save", Category = Title, Availability = () => Hub != null ? null : "Open the Farm Hub first", Execute = _ =>
                Hub.DevReloadFromSave() ? DevResult.Success("Farm reloaded from the saved files") : DevResult.Fail("Farm is busy") });
            DevCommands.Register(new DevCommand { Id = "save.export", Label = "Export Debug Save", Category = Title, Execute = _ => Export() });
            DevCommands.Register(new DevCommand { Id = "save.resetUiPrefs", Label = "Reset UI Preferences", Category = Title, Execute = _ =>
            {
                foreach (var k in new[] { "ShearAndGrow.Hint.Wash", "ShearAndGrow.Hint.Dry", "ShearAndGrow.Dev.ShowDevButton", "ShearAndGrow.Dev.ShowFps", "ShearAndGrow.Dev.LastTab" }) PlayerPrefs.DeleteKey(k);
                OrientationService.ResetToDefault();
                PlayerPrefs.Save();
                return DevResult.Success("UI preferences reset (orientation → " + OrientationService.Label(OrientationService.DefaultMode) + ", hints, dev panel options)");
            }});
            DevCommands.Register(new DevCommand { Id = "save.resetCameraPrefs", Label = "Reset Camera Preferences", Category = Title, Execute = _ =>
            {
                DevCameraPrefs.Clear();
                return DevResult.Success("Developer camera preferences cleared – Original camera");
            }});
        }

        public override void Build(DevUi ui)
        {
            ui.Header("Reset test farm");
            if (!_confirming)
            {
                ui.Label("Restarts this install's farm as a brand-new player using the normal new-player initialisation (starting buildings, two starting sheep with full wool, starting coins, no purchases, timers or upgrades).", 24);
                ui.Button("Reset Farm To Fresh Save…", () => { _confirming = true; Panel.Rebuild(); }, DevUi.Danger);
            }
            else
            {
                ui.Label("⚠ This deletes ALL local test progress on this device: coins, wool, buildings, decorations, upgrades, timers, sheep and hairstyles. It cannot be undone.\nScope: local device saves only (" + DevBackends.FarmReset.Name + "). No server account exists or is touched.", 26, new Color(1f, 0.7f, 0.6f, 1f));
                _confirmField = ui.InputWithButton("Type " + ConfirmToken + " to confirm", InputField.ContentType.Alphanumeric, "Confirm Reset", v => Run("save.resetFarm", v), DevUi.Danger);
                ui.Button("Cancel", () => { _confirming = false; Panel.Rebuild(); });
            }

            ui.Header("Save tools");
            ui.Buttons(("Reload Current Save", () => Run("save.reload")), ("Export Debug Save", () => Run("save.export")));
            _exportInfo = ui.Label("", 22, Processing.UiKit.TextMuted);
            ui.Buttons(("Reset UI Prefs", () => { Run("save.resetUiPrefs"); Panel.Rebuild(); }), ("Reset Camera Prefs", () => { Run("save.resetCameraPrefs"); Panel.Rebuild(); }));

            ui.Header("Save status");
            _status = ui.Label("", 22);
        }

        public override void Refresh()
        {
            if (_status == null) return;
            _sb.Length = 0;
            var e = WoolEconomy.Instance;
            _sb.Append("Economy: ").Append(FileInfoText(EconomyStore.FilePath));
            if (e != null) _sb.Append("\n  schema ").Append(e.DebugState.schemaVersion).Append(" · epoch ").Append(e.DebugState.saveEpoch).Append(" · ops ").Append(e.DebugState.operationCount)
                              .Append(" · pending receipts ").Append(e.PendingPurchases.Count).Append(" · ").Append(e.LoadInfo);
            _sb.Append("\nFarm: ").Append(FileInfoText(FarmStateStore.FilePath));
            var st = Hub != null ? Hub.State : null;
            if (st != null) _sb.Append("\n  schema ").Append(st.schemaVersion).Append(" (current ").Append(FarmStateStore.CurrentSchema).Append(") · layout v").Append(st.layoutVersion)
                               .Append(" · ").Append(st.instances.Count).Append(" instances · ").Append(st.herd.Count).Append(" sheep · ").Append(FarmStateStore.LastLoadInfo);
            int sheepFiles = 0;
            try { if (Directory.Exists(SheepWoolStateStore.Directory)) sheepFiles = Directory.GetFiles(SheepWoolStateStore.Directory, "*.json").Length; } catch { }
            _sb.Append("\nSheep saves: ").Append(sheepFiles).Append(" file(s) · schema ").Append(SheepWoolStateStore.CurrentSchema).Append("\n  ").Append(SheepWoolStateStore.Directory);
            _sb.Append("\nStorage: ").Append(Application.isEditor ? "Editor saves (separate from device builds)" : "Device saves").Append(" · backend: ").Append(BackendStatus.Kind).Append(BackendStatus.IsConnected ? " (connected)" : " (offline/local)");
            _status.text = _sb.ToString();
        }

        static string FileInfoText(string path)
        {
            try
            {
                if (!File.Exists(path)) return "no file (new player)";
                var fi = new FileInfo(path);
                return (fi.Length / 1024f).ToString("0.0", CultureInfo.InvariantCulture) + " KB · saved " + fi.LastWriteTime.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
            }
            catch (Exception ex) { return "unreadable (" + ex.GetType().Name + ")"; }
        }

        /// <summary>
        /// Writes economy / farm / sheep saves into one text file (test environments only). The saves contain no
        /// authentication tokens or secrets (none exist in this offline prototype); nothing else is exported.
        /// </summary>
        DevResult Export()
        {
            var sb = new StringBuilder(8192);
            sb.Append("Shear & Grow debug save export\n").Append("created ").Append(DateTime.UtcNow.ToString("o")).Append(" · app ").Append(Application.version)
              .Append(" · unity ").Append(Application.unityVersion).Append(" · ").Append(DevAccess.BuildType).Append(" · ").Append(Application.platform).Append("\n\n");
            AppendFile(sb, "economy.json", EconomyStore.FilePath);
            AppendFile(sb, "farm.json", FarmStateStore.FilePath);
            try
            {
                if (Directory.Exists(SheepWoolStateStore.Directory))
                    foreach (var f in Directory.GetFiles(SheepWoolStateStore.Directory, "*.json")) AppendFile(sb, "sheep/" + Path.GetFileName(f), f);
            }
            catch (Exception ex) { sb.Append("sheep folder unreadable: ").Append(ex.Message).Append('\n'); }
            string dir = Path.Combine(Application.persistentDataPath, "ShearAndGrow", "DebugExports");
            Directory.CreateDirectory(dir);
            string path = Path.Combine(dir, "export_" + DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture) + ".txt");
            File.WriteAllText(path, sb.ToString());
            GUIUtility.systemCopyBuffer = sb.ToString();
            if (_exportInfo != null) _exportInfo.text = "Exported to: " + path + "\n(also copied to the clipboard)";
            return DevResult.Success("Debug save exported (" + (sb.Length / 1024) + " KB)");
        }

        static void AppendFile(StringBuilder sb, string name, string path)
        {
            sb.Append("===== ").Append(name).Append(" =====\n");
            try { sb.Append(File.Exists(path) ? File.ReadAllText(path) : "(missing)"); }
            catch (Exception ex) { sb.Append("(unreadable: ").Append(ex.Message).Append(')'); }
            sb.Append("\n\n");
        }
    }
}
