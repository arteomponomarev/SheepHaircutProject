using UnityEngine;

namespace ShearAndGrow
{
    /// <summary>Farm sheep presentation only; reads state and never changes it.</summary>
    public sealed class PrototypeSheepVisual : MonoBehaviour
    {
        [SerializeField] private PrototypeSheepState sheep;
        [SerializeField] private GameObject woolVisual;
        [SerializeField, Range(0.01f, 0.4f)] private float shortWoolAt = 0.1f;
        [SerializeField, Range(0.4f, 0.9f)] private float mediumWoolAt = 0.5f;
        [SerializeField] private Vector3 shortWoolScale = new Vector3(0.95f, 0.91f, 0.89f);
        [SerializeField] private Vector3 mediumWoolScale = new Vector3(0.975f, 0.955f, 0.945f);
        private Vector3 fullScale;
        private int visibleStage = -1;
        private bool scaleCached;
        private void CacheScale()
        {
            if (scaleCached || woolVisual == null) return;
            fullScale = woolVisual.transform.localScale; scaleCached = true;
        }
        private void Awake() => CacheScale();
        public void Bind(PrototypeSheepState state)
        {
            if (sheep != null) sheep.Changed -= Refresh;
            sheep = state; visibleStage = -1; CacheScale();
            if (isActiveAndEnabled && sheep != null) { sheep.Changed += Refresh; Refresh(); }
        }
        private void OnEnable()
        {
            CacheScale();
            if (sheep == null || woolVisual == null)
            { Debug.LogError("Sheep visual requires state and wool references.", this); enabled = false; return; }
            sheep.Changed += Refresh; Refresh();
        }
        private void Refresh()
        {
            float growth = sheep.WoolGrowth01;
            int stage = growth >= 1f ? 3 : growth >= mediumWoolAt ? 2 : growth >= shortWoolAt ? 1 : 0;
            if (visibleStage == stage) return;
            visibleStage = stage;
            woolVisual.SetActive(stage > 0);
            woolVisual.transform.localScale = Vector3.Scale(fullScale,
                stage == 1 ? shortWoolScale : stage == 2 ? mediumWoolScale : Vector3.one);
        }
        private void OnDisable() { if (sheep != null) sheep.Changed -= Refresh; }
    }
}
