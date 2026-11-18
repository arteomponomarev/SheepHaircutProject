using System.Collections.Generic;
using UnityEngine;

namespace ShearAndGrow.Farm
{
    /// <summary>
    /// Experimental camera angles for the farm (developer comparison only — players always get the controller's own
    /// serialized "Original" angle). The Original preset is captured at runtime from the scene camera, so it always
    /// matches the real shipped values.
    /// </summary>
    [CreateAssetMenu(menuName = "Shear & Grow/Farm/Camera Preset Library", fileName = "FarmCameraPresets")]
    public sealed class FarmCameraPresetLibrary : ScriptableObject
    {
        public List<FarmCameraPreset> Presets = new List<FarmCameraPreset>();

        public FarmCameraPreset Find(string id)
        {
            foreach (var p in Presets) if (p != null && p.Id == id) return p;
            return null;
        }
    }
}
