using System.Text;
using ShearAndGrow.Farm;
using UnityEngine;

namespace ShearAndGrow.DevTools
{
    /// <summary>
    /// Building debug overlay for the Farm Hub: grid with occupied cells (the game's own GridOverlay) and IMGUI tags with
    /// instance id, level and construction timer above each building. Does nothing (and draws nothing) while all
    /// switches are off. Read-only: never changes farm data.
    /// </summary>
    public sealed class DevWorldOverlay : MonoBehaviour
    {
        public static bool ShowGrid { get => _grid; set { _grid = value; if (!value) RestoreGrid(); } }
        public static bool ShowIds, ShowLevels, ShowTimers;
        static bool _grid;

        static bool AnyLabel => ShowIds || ShowLevels || ShowTimers;
        GUIStyle _style;
        readonly StringBuilder _sb = new StringBuilder(96);

        static void RestoreGrid()
        {
            var hub = FarmHubController.Active;
            if (hub == null || hub.Buildings == null || hub.Buildings.Overlay == null) return;
            hub.Buildings.Overlay.SetVisible(hub.EditMode || (hub.Buildings.Placement != null && hub.Buildings.Placement.IsActive));
        }

        void LateUpdate()
        {
            if (!_grid) return;
            var hub = FarmHubController.Active;
            if (hub == null || hub.Buildings == null || hub.Buildings.Overlay == null) return;
            if (!hub.Buildings.Overlay.Visible) hub.Buildings.Overlay.SetVisible(true);
        }

        void OnGUI()
        {
            if (!AnyLabel || Event.current.type != EventType.Repaint) return;
            var hub = FarmHubController.Active;
            var svc = hub != null && hub.Buildings != null ? hub.Buildings.Service : null;
            if (svc == null) return;
            var cam = hub.CameraController.Camera;
            float scale = Mathf.Max(1f, Mathf.Min(Screen.width, Screen.height) / 1080f);
            if (_style == null) _style = new GUIStyle(GUI.skin.box) { alignment = TextAnchor.MiddleCenter, wordWrap = false, richText = false };
            _style.fontSize = Mathf.RoundToInt(22 * scale);
            foreach (var b in svc.State.instances)
            {
                if (!b.IsPlaced) continue;
                Vector3 sp = cam.WorldToScreenPoint(svc.WorldCenter(b) + Vector3.up * 2.6f);
                if (sp.z <= 0f || sp.x < 0 || sp.x > Screen.width || sp.y < 0 || sp.y > Screen.height) continue;
                _sb.Length = 0;
                if (ShowIds) _sb.Append(b.instanceId);
                if (ShowLevels) _sb.Append(_sb.Length > 0 ? "  " : "").Append("Lv ").Append(b.level).Append(b.status != BuildingStatus.Completed ? " [" + b.status + "]" : "");
                if (ShowTimers && b.IsBuilderBusy) _sb.Append(_sb.Length > 0 ? "  " : "").Append(PlacementText.Time(svc.RemainingSeconds(b)));
                if (_sb.Length == 0) continue;
                string text = _sb.ToString();
                Vector2 size = _style.CalcSize(new GUIContent(text));
                GUI.Box(new Rect(sp.x - size.x * 0.5f, Screen.height - sp.y - size.y, size.x, size.y), text, _style);
            }
        }
    }
}
