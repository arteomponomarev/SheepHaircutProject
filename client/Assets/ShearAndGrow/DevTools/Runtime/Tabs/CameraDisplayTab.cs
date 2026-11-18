using System.Globalization;
using System.Text;
using ShearAndGrow.Economy;
using ShearAndGrow.Farm;
using UnityEngine;
using UnityEngine.UI;

namespace ShearAndGrow.DevTools
{
    /// <summary>
    /// Farm camera experiments (live, no scene reload) + orientation + display diagnostics. All camera changes go
    /// through <see cref="FarmCameraController"/> (the only writer of the camera transform) and are clamped there.
    /// </summary>
    public sealed class CameraDisplayTab : DevTab
    {
        public override string Title => "Camera & Display";
        Text _camInfo, _display, _copyText;
        readonly StringBuilder _sb = new StringBuilder(512);

        static FarmCameraController Cam => FarmHubController.Active != null ? FarmHubController.Active.CameraController : null;
        static string FarmOpen() => Cam != null ? null : "Open the Farm Hub first";
        static string F(float v, string fmt = "0.#") => v.ToString(fmt, CultureInfo.InvariantCulture);

        public override void RegisterCommands()
        {
            DevCommands.Register(new DevCommand { Id = "camera.preset", Label = "Camera Preset", Category = Title, Availability = FarmOpen, Execute = id =>
            {
                var p = DevCameraPrefs.Resolve(id, Cam);
                if (p == null) return DevResult.Fail(id == "custom" ? "No custom preset saved yet" : "Unknown preset " + id);
                Cam.ApplySettings(p);
                DevCameraPrefs.SetActive(p.Id);
                return DevResult.Success("Camera: " + p.DisplayName + " (yaw " + F(Cam.Yaw) + "°, pitch " + F(Cam.Pitch) + "°, " + Cam.Projection + ")");
            }});
            DevCommands.Register(new DevCommand { Id = "camera.projection", Label = "Projection", Category = Title, Availability = FarmOpen, Execute = arg =>
            {
                if (arg != "Orthographic" && arg != "Perspective") return DevResult.Fail("Projection must be Orthographic or Perspective");
                Cam.SetProjection(arg == "Perspective" ? FarmCameraProjection.Perspective : FarmCameraProjection.Orthographic);
                return DevResult.Success("Projection: " + Cam.Projection + " (visible area kept)");
            }});
            DevCommands.Register(new DevCommand { Id = "camera.reset", Label = "Reset Camera (Original)", Category = Title, Availability = FarmOpen, Execute = _ =>
            {
                Cam.RestoreOriginal();
                DevCameraPrefs.SetActive("original");
                return DevResult.Success("Original player camera restored");
            }});
            DevCommands.Register(new DevCommand { Id = "camera.saveCustom", Label = "Save Custom Preset", Category = Title, Availability = FarmOpen, Execute = _ =>
            {
                DevCameraPrefs.SaveCustom(Cam.CurrentSettings);
                Cam.ApplySettings(DevCameraPrefs.LoadCustom());
                return DevResult.Success("Saved as local Custom preset (dev-only, not part of the game save)");
            }});
            DevCommands.Register(new DevCommand { Id = "camera.copy", Label = "Copy Camera Settings", Category = Title, Availability = FarmOpen, Execute = _ =>
            {
                string text = Cam.CurrentSettings.ToText(Cam.TargetSize, Cam.SettledFocus);
                GUIUtility.systemCopyBuffer = text;
                if (_copyText != null) _copyText.text = text;
                return DevResult.Success("Camera settings copied to the clipboard (also shown below)");
            }});
            DevCommands.Register(new DevCommand { Id = "display.orientation", Label = "Orientation", Category = Title, Execute = arg =>
            {
                if (!System.Enum.TryParse(arg, out OrientationMode m) || !System.Enum.IsDefined(typeof(OrientationMode), m)) return DevResult.Fail("Unknown orientation " + arg);
                OrientationService.SetMode(m);
                return DevResult.Success("Orientation: " + OrientationService.Label(m) + (Application.isEditor ? " (Editor: pick a matching Game view size)" : ""));
            }});
        }

        public override void Build(DevUi ui)
        {
            var cam = Cam;
            if (cam != null)
            {
                ui.Header("Camera preset");
                var presets = DevCameraPrefs.Presets(cam);
                for (int i = 0; i < presets.Count; i += 3)
                {
                    var items = new System.Collections.Generic.List<(string, System.Action, Color?)>();
                    for (int k = i; k < Mathf.Min(i + 3, presets.Count); k++)
                    {
                        var p = presets[k];
                        string id = p.Id;
                        items.Add((p.DisplayName, () => { Run("camera.preset", id); Panel.Rebuild(); }, cam.ActivePresetId == id ? DevUi.Good : (Color?)null));
                    }
                    ui.Buttons(items.ToArray());
                }
                _camInfo = ui.Label("", 24);

                ui.Header("Live adjust");
                ui.Slider("Yaw (°)", -180f, 180f, cam.Yaw, v => F(v, "0") + "°", v => cam.SetYaw(v), out _);
                ui.Slider("Pitch (° below horizon)", FarmCameraController.MinPitchLimit, FarmCameraController.MaxPitchLimit, cam.Pitch, v => F(v, "0") + "°", v => cam.SetPitch(v), out _);
                ui.Slider("Zoom (in →)", 0f, 1f, cam.Zoom01, v => F(v * 100f, "0") + "%", v => cam.SetZoom01(v), out _);
                ui.Buttons(("Orthographic", () => { Run("camera.projection", "Orthographic"); Panel.Rebuild(); }, cam.Projection == FarmCameraProjection.Orthographic ? DevUi.Good : (Color?)null),
                           ("Perspective", () => { Run("camera.projection", "Perspective"); Panel.Rebuild(); }, cam.Projection == FarmCameraProjection.Perspective ? DevUi.Good : (Color?)null));
                if (cam.Projection == FarmCameraProjection.Perspective)
                    ui.Slider("Field of view (°)", FarmCameraController.MinFovLimit, FarmCameraController.MaxFovLimit, cam.FieldOfView, v => F(v, "0") + "°", v => cam.SetFieldOfView(v), out _);
                else
                    ui.Slider("Orthographic size (zoom value)", cam.MinSize, Mathf.Max(cam.MinSize + 0.1f, cam.MaxSize), cam.TargetSize, v => F(v, "0.0"), v => cam.SetSize(v), out _);
                ui.Buttons(("Reset Camera", () => { Run("camera.reset"); Panel.Rebuild(); }, DevUi.Danger), ("Save Custom", () => { Run("camera.saveCustom"); Panel.Rebuild(); }, DevUi.Good), ("Copy", () => Run("camera.copy"), null));
                _copyText = ui.Label("", 22, Processing.UiKit.TextMuted);
                ui.Label("Experimental presets are developer-only. Players always get the Original camera (no rotation in normal play).", 22, Processing.UiKit.TextMuted);
            }
            else ui.Label("Camera presets: open the Farm Hub.", 26);

            ui.Header("Orientation");
            var mode = OrientationService.Mode;
            ui.Buttons(("Portrait", () => { Run("display.orientation", "Portrait"); Panel.Rebuild(); }, mode == OrientationMode.Portrait ? DevUi.Good : (Color?)null),
                       ("Landscape", () => { Run("display.orientation", "Landscape"); Panel.Rebuild(); }, mode == OrientationMode.Landscape ? DevUi.Good : (Color?)null),
                       ("Auto", () => { Run("display.orientation", "Auto"); Panel.Rebuild(); }, mode == OrientationMode.Auto ? DevUi.Good : (Color?)null));
            ui.Toggle("FPS indicator", Panel.ShowFps, v => Panel.ShowFps = v);
            ui.Toggle("Floating DEV button", Panel.ShowFloatingButton, v => Panel.ShowFloatingButton = v);

            ui.Header("Display diagnostics");
            _display = ui.Label("", 24);
        }

        public override void Refresh()
        {
            var cam = Cam;
            if (_camInfo != null && cam != null)
            {
                var t = cam.transform;
                _camInfo.text = "Active: " + cam.ActivePresetId + "  ·  " + cam.Projection + (cam.Projection == FarmCameraProjection.Perspective ? " FOV " + F(cam.FieldOfView) + "°" : "") +
                                "\nYaw " + F(cam.Yaw) + "°  ·  pitch " + F(cam.Pitch) + "°  ·  zoom " + F(cam.TargetSize, "0.0") + " (" + F(cam.MinSize, "0.0") + "–" + F(cam.MaxSize, "0.0") + ")  ·  distance " + F(cam.CurrentDistance, "0.0");
            }
            if (_display == null) return;
            var sa = Screen.safeArea;
            var main = cam != null ? cam.Camera : Camera.main;
            _sb.Length = 0;
            _sb.Append("Screen ").Append(Screen.width).Append(" × ").Append(Screen.height).Append("  ·  aspect ").Append(F(ScreenLayout.Aspect, "0.000")).Append("  ·  ").Append(Screen.dpi > 0 ? F(Screen.dpi, "0") + " dpi" : "dpi ?").Append('\n');
            _sb.Append("Orientation: ").Append(Screen.orientation).Append("  ·  layout ").Append(ScreenLayout.IsLandscape ? "Landscape" : "Portrait").Append("  ·  setting ").Append(OrientationService.Label(OrientationService.Mode)).Append('\n');
            _sb.Append("Safe area: x ").Append(F(sa.x, "0")).Append(" y ").Append(F(sa.y, "0")).Append(" w ").Append(F(sa.width, "0")).Append(" h ").Append(F(sa.height, "0")).Append('\n');
            if (main != null)
            {
                var t = main.transform;
                _sb.Append("Active camera: ").Append(main.name).Append(main.orthographic ? " (ortho " + F(main.orthographicSize, "0.0") + ")" : " (persp " + F(main.fieldOfView, "0") + "°)").Append('\n');
                _sb.Append("Position ").Append(F(t.position.x, "0.0")).Append(", ").Append(F(t.position.y, "0.0")).Append(", ").Append(F(t.position.z, "0.0"))
                   .Append("  ·  rotation ").Append(F(t.eulerAngles.x, "0")).Append(", ").Append(F(t.eulerAngles.y, "0")).Append(", ").Append(F(t.eulerAngles.z, "0"));
            }
            else _sb.Append("Active camera: none");
            _display.text = _sb.ToString();
        }
    }
}
