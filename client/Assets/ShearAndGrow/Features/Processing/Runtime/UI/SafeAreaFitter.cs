using UnityEngine;

namespace ShearAndGrow.Processing
{
    /// <summary>Keeps a RectTransform inside Screen.safeArea (notches, home indicator). Re-applies on change only.</summary>
    public sealed class SafeAreaFitter : MonoBehaviour
    {
        public RectTransform Target;
        Rect _last;
        Vector2Int _lastScreen;

        void Update()
        {
            if (Target == null) return;
            Rect sa = Screen.safeArea;
            var screen = new Vector2Int(Screen.width, Screen.height);
            if (sa == _last && screen == _lastScreen) return;
            _last = sa;
            _lastScreen = screen;
            float w = Mathf.Max(1, Screen.width), h = Mathf.Max(1, Screen.height);
            Target.anchorMin = new Vector2(sa.xMin / w, sa.yMin / h);
            Target.anchorMax = new Vector2(sa.xMax / w, sa.yMax / h);
            Target.offsetMin = Target.offsetMax = Vector2.zero;
        }
    }
}
