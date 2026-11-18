using System.Text;
using ShearAndGrow.Economy;
using ShearAndGrow.Farm;
using ShearAndGrow.Shearing;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ShearAndGrow.DevTools
{
    /// <summary>Compact state overview for bug reports + the developer operation journal.</summary>
    public sealed class DiagnosticsTab : DevTab
    {
        public override string Title => "Diagnostics";
        UnityEngine.UI.Text _info, _log;
        readonly StringBuilder _sb = new StringBuilder(1024);

        public override void RegisterCommands()
        {
            DevCommands.Register(new DevCommand { Id = "diag.copy", Label = "Copy Diagnostics", Category = Title, Execute = _ =>
            {
                GUIUtility.systemCopyBuffer = BuildInfo();
                return DevResult.Success("Diagnostics copied to the clipboard");
            }});
        }

        public override void Build(DevUi ui)
        {
            ui.Header("Build & state");
            _info = ui.Label("", 24);
            ui.Button("Copy diagnostics", () => Run("diag.copy"));
            ui.Header("Developer log (this session)");
            _log = ui.Label("", 20, Processing.UiKit.TextMuted);
        }

        string BuildInfo()
        {
            _sb.Length = 0;
            var hub = FarmHubController.Active;
            var svc = hub != null && hub.Buildings != null ? hub.Buildings.Service : null;
            var fps = Panel.Fps;
            _sb.Append("App ").Append(Application.productName).Append(' ').Append(Application.version).Append("  ·  Unity ").Append(Application.unityVersion).Append('\n');
            _sb.Append("Build: ").Append(DevAccess.BuildType).Append("  ·  ").Append(Application.platform).Append("  ·  ").Append(SystemInfo.deviceModel).Append('\n');
            _sb.Append("Active scene: ").Append(SceneManager.GetActiveScene().name).Append('\n');
            _sb.Append("FPS: ").Append(fps != null && fps.enabled ? Mathf.RoundToInt(fps.Fps).ToString() : "–").Append("  ·  target ").Append(Application.targetFrameRate).Append('\n');
            _sb.Append("Orientation: ").Append(OrientationService.Label(OrientationService.Mode)).Append(" setting · screen ").Append(Screen.width).Append('×').Append(Screen.height)
               .Append(ScreenLayout.IsLandscape ? " (landscape layout)" : " (portrait layout)").Append('\n');
            _sb.Append("Camera preset: ").Append(hub != null ? hub.CameraController.ActivePresetId + " · " + hub.CameraController.Projection : "– (not in Farm Hub)").Append('\n');
            if (svc != null)
                _sb.Append("Farm level: Farmer House Lv ").Append(svc.FarmerHouseLevel).Append("  ·  sheep ").Append(svc.State.herd.Count)
                   .Append("  ·  active constructions ").Append(svc.ActiveConstructionCount).Append("  ·  buildings ").Append(svc.State.instances.Count).Append('\n');
            else _sb.Append("Farm: – (open the Farm Hub for farm data)\n");
            var e = WoolEconomy.Instance;
            _sb.Append("Save versions: economy ").Append(e != null ? e.DebugState.schemaVersion.ToString() : "–").Append(" · farm ")
               .Append(svc != null ? svc.State.schemaVersion.ToString() : FarmStateStore.CurrentSchema + "*").Append(" · sheep ").Append(SheepWoolStateStore.CurrentSchema).Append('\n');
            _sb.Append("Backend: ").Append(BackendStatus.Kind).Append(BackendStatus.IsConnected ? " – connected" : " – not connected").Append(" · ").Append(BackendStatus.Detail).Append('\n');
            _sb.Append("Time source: ").Append(GameClock.Source.Name).Append(GameClock.DevOffsetSeconds > 0 ? " · test offset " + PlacementText.Time(GameClock.DevOffsetSeconds) : "").Append('\n');
            _sb.Append("Economy backend: ").Append(DevBackends.Economy.Name).Append(" · reset backend: ").Append(DevBackends.FarmReset.Name);
            return _sb.ToString();
        }

        public override void Refresh()
        {
            if (_info != null) _info.text = BuildInfo();
            if (_log == null) return;
            _sb.Length = 0;
            var lines = DevLog.Lines;
            for (int i = lines.Count - 1; i >= 0 && i >= lines.Count - 30; i--) _sb.Append(lines[i]).Append('\n');
            _log.text = lines.Count == 0 ? "No developer operations yet." : _sb.ToString();
        }
    }
}
