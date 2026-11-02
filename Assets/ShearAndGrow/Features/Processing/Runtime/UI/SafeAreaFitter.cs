using UnityEngine;

namespace ShearAndGrow.Processing
{
    /// <summary>Keeps a RectTransform inside Screen.safeArea (notches, home indicator). Re-applies on change only.</summary>
    public sealed class SafeAreaFitter : MonoBehaviour
    {
        public RectTransform Target;
        Rect _last;

        void Update()
        {
            if (Target == null) return;
            Rect sa = Screen.safeArea;
            if (sa == _last) return;
            _last = sa;
            float w = Mathf.Max(1, Screen.width), h = Mathf.Max(1, Screen.height);
            Target.anchorMin = new Vector2(sa.xMin / w, sa.yMin / h);
            Target.anchorMax = new Vector2(sa.xMax / w, sa.yMax / h);
            Target.offsetMin = Target.offsetMax = Vector2.zero;
        }
    }
}
