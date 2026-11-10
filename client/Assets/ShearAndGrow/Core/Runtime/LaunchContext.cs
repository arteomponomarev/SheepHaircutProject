using UnityEngine;

namespace ShearAndGrow.Economy
{
    /// <summary>
    /// One-shot handoff between scenes (in memory only, never saved): which sheep the shearing scene should load,
    /// which farm panel to reopen on return, and the farm camera view to restore. Consumers clear what they read.
    /// </summary>
    public static class LaunchContext
    {
        public static string SheepId;
        public static string ReturnPanel;
        public static bool HasFarmView;
        public static Vector3 FarmCameraFocus;
        public static float FarmCameraZoom;

        public static string ConsumeSheepId() { var v = SheepId; SheepId = null; return v; }
        public static string ConsumeReturnPanel() { var v = ReturnPanel; ReturnPanel = null; return v; }

        public static bool ConsumeFarmView(out Vector3 focus, out float zoom)
        {
            focus = FarmCameraFocus; zoom = FarmCameraZoom;
            bool had = HasFarmView;
            HasFarmView = false;
            return had;
        }

        public static void StoreFarmView(Vector3 focus, float zoom) { FarmCameraFocus = focus; FarmCameraZoom = zoom; HasFarmView = true; }
    }
}
