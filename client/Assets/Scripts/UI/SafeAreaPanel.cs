using UnityEngine;

namespace ShearAndGrow
{
    [RequireComponent(typeof(RectTransform))]
    public sealed class SafeAreaPanel : MonoBehaviour
    {
        private RectTransform panel;
        private Rect previousArea;
        private Vector2Int previousSize;

        private void Awake() => panel = (RectTransform)transform;
        private void OnEnable() => Apply();
        private void Update()
        {
            if (Screen.safeArea != previousArea || Screen.width != previousSize.x || Screen.height != previousSize.y)
                Apply();
        }

        private void Apply()
        {
            if (panel == null || Screen.width <= 0 || Screen.height <= 0) return;
            previousArea = Screen.safeArea;
            previousSize = new Vector2Int(Screen.width, Screen.height);
            panel.anchorMin = new Vector2(previousArea.xMin / Screen.width, previousArea.yMin / Screen.height);
            panel.anchorMax = new Vector2(previousArea.xMax / Screen.width, previousArea.yMax / Screen.height);
            panel.offsetMin = panel.offsetMax = Vector2.zero;
        }
    }
}
