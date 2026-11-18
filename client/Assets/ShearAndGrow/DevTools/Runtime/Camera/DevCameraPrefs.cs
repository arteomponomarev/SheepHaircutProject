using System.Collections.Generic;
using ShearAndGrow.Farm;
using UnityEngine;

namespace ShearAndGrow.DevTools
{
    /// <summary>
    /// Developer camera experiments, stored in PlayerPrefs under a dev-only key (never in the game save, never shipped:
    /// this assembly does not exist in release builds). Players always get the controller's serialized Original view.
    /// The selected preset is re-applied whenever the Farm Hub loads, so comparisons survive scene changes / restarts.
    /// </summary>
    public sealed class DevCameraPrefs : MonoBehaviour
    {
        const string ActiveKey = "ShearAndGrow.Dev.Camera.Active";
        const string CustomKey = "ShearAndGrow.Dev.Camera.Custom";

        public static DevCameraPrefs Instance { get; private set; }

        void Awake() { Instance = this; FarmHubController.ActiveChanged += OnHub; }
        void OnDestroy() { FarmHubController.ActiveChanged -= OnHub; if (Instance == this) Instance = null; }

        static FarmCameraController Cam => FarmHubController.Active != null ? FarmHubController.Active.CameraController : null;

        public static string ActiveId => PlayerPrefs.GetString(ActiveKey, "original");

        void OnHub(FarmHubController hub)
        {
            if (hub == null) return;
            var p = Resolve(ActiveId, hub.CameraController);
            if (p != null && p.Id != "original") hub.CameraController.ApplySettings(p);
        }

        /// <summary>All presets offered in the panel: Original (captured), the library presets, Custom (if saved).</summary>
        public static List<FarmCameraPreset> Presets(FarmCameraController cam)
        {
            var list = new List<FarmCameraPreset>();
            if (cam == null) return list;
            list.Add(cam.OriginalPreset);
            if (cam.PresetLibrary != null) foreach (var p in cam.PresetLibrary.Presets) if (p != null) list.Add(p);
            var c = LoadCustom();
            if (c != null) list.Add(c);
            return list;
        }

        public static FarmCameraPreset Resolve(string id, FarmCameraController cam)
        {
            if (cam == null || string.IsNullOrEmpty(id)) return null;
            if (id == "original") return cam.OriginalPreset;
            if (id == "custom") return LoadCustom();
            return cam.PresetLibrary != null ? cam.PresetLibrary.Find(id) : null;
        }

        public static FarmCameraPreset LoadCustom()
        {
            string json = PlayerPrefs.GetString(CustomKey, "");
            if (string.IsNullOrEmpty(json)) return null;
            try { var p = JsonUtility.FromJson<FarmCameraPreset>(json); if (p != null) { p.Id = "custom"; p.DisplayName = "Custom"; } return p; }
            catch { return null; }
        }

        public static void SaveCustom(FarmCameraPreset p)
        {
            if (p == null) return;
            p = p.Clone(); p.Id = "custom"; p.DisplayName = "Custom";
            PlayerPrefs.SetString(CustomKey, JsonUtility.ToJson(p));
            SetActive("custom");
        }

        public static void SetActive(string id) { PlayerPrefs.SetString(ActiveKey, string.IsNullOrEmpty(id) ? "original" : id); PlayerPrefs.Save(); }

        /// <summary>Developer Panel "Reset Camera Preferences": forget the custom preset and the selection.</summary>
        public static void Clear()
        {
            PlayerPrefs.DeleteKey(ActiveKey);
            PlayerPrefs.DeleteKey(CustomKey);
            PlayerPrefs.Save();
            var cam = Cam;
            if (cam != null) cam.RestoreOriginal();
        }
    }
}
