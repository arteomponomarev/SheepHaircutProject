using UnityEngine;
using UnityEngine.EventSystems;

namespace ShearAndGrow.DevTools
{
    /// <summary>
    /// Lets the floating DEV button be dragged up / down the left screen edge so it never blocks game UI (catalog,
    /// sheets, mini-game buttons). A drag does not count as a tap. The position is remembered (dev prefs).
    /// </summary>
    public sealed class DevDragHandle : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        const string PosKey = "ShearAndGrow.Dev.DevButtonY";
        RectTransform _rt, _parent;

        void Awake()
        {
            _rt = (RectTransform)transform;
            _parent = (RectTransform)_rt.parent;
            Apply(PlayerPrefs.GetFloat(PosKey, 0.62f));
        }

        void Apply(float y01)
        {
            y01 = Mathf.Clamp(y01, 0.08f, 0.92f);
            _rt.anchorMin = _rt.anchorMax = new Vector2(0f, y01);
            _rt.anchoredPosition = new Vector2(0f, 0f);
        }

        public void OnBeginDrag(PointerEventData e) { }

        public void OnDrag(PointerEventData e)
        {
            if (_parent == null) return;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(_parent, e.position, e.pressEventCamera, out var local)) return;
            var r = _parent.rect;
            Apply(Mathf.InverseLerp(r.yMin, r.yMax, local.y));
        }

        public void OnEndDrag(PointerEventData e) { PlayerPrefs.SetFloat(PosKey, _rt.anchorMin.y); PlayerPrefs.Save(); }
    }
}
