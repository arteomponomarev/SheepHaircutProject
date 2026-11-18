using ShearAndGrow.Economy;
using UnityEditor;

namespace ShearAndGrow.Farm.Editor
{
    /// <summary>Tools ▸ Shear &amp; Grow ▸ Developer Panel (Play Mode). Ctrl/Cmd+Shift+D.</summary>
    public static class DevToolsMenu
    {
        [MenuItem("Tools/Shear & Grow/Developer Panel %#d")]
        static void Open()
        {
            if (!EditorApplication.isPlaying) { EditorUtility.DisplayDialog("Developer Panel", "Enter Play Mode first – the panel is part of the running game.", "OK"); return; }
            DevToolsGate.Open();
        }
    }
}
