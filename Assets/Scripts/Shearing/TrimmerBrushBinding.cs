using UnityEngine;

namespace ShearAndGrow
{
    /// <summary>Applies trimmer configuration to stroke painting, without changing mask/reward rules.</summary>
    [DisallowMultipleComponent]
    public sealed class TrimmerBrushBinding : MonoBehaviour
    {
        [SerializeField] private TrimmerUpgradeState upgrades;
        [SerializeField] private WoolStrokePainter strokes;
        private void OnEnable()
        {
            if (upgrades == null || !upgrades.IsConfigured || strokes == null)
            { Debug.LogError("Trimmer brush binding requires valid upgrade state and stroke painter.", this); enabled = false; return; }
            upgrades.Changed += Apply; Apply();
        }
        private void Apply() => strokes.SetBrushRadius(upgrades.BrushRadius);
        private void OnDisable() { if (upgrades != null) upgrades.Changed -= Apply; }
    }
}
