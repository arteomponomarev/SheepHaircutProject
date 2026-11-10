using System;
using UnityEngine;
using UnityEngine.EventSystems;

namespace ShearAndGrow.Farm
{
    /// <summary>
    /// Single place that turns touches / mouse into farm gestures:
    ///   tap (short, small movement) → <see cref="Tapped"/>; one-finger drag → camera pan; two-finger pinch → zoom at
    ///   the midpoint; mouse wheel → zoom at the cursor. Gestures starting on UI are ignored; a second finger turns a
    ///   pending tap into a pinch. Releases cleanly on focus loss / pause.
    /// </summary>
    public sealed class FarmInputRouter : MonoBehaviour
    {
        [SerializeField] private FarmCameraController _camera;
        [Tooltip("Max movement (px, at 1080 reference width) for a press to count as a tap.")]
        [SerializeField] private float _tapSlop = 14f;
        [SerializeField] private float _tapMaxSeconds = 0.4f;

        enum Mode { None, Pending, Drag, Pinch, Blocked, Intercepted }

        Mode _mode;
        IFarmDragInterceptor _interceptor;

        /// <summary>Placement mode: a drag that starts on the ghost goes to the interceptor instead of the camera.</summary>
        public void SetInterceptor(IFarmDragInterceptor interceptor) { if (_mode == Mode.Intercepted) Release(); _interceptor = interceptor; }
        Vector2 _downPos, _lastPos;
        float _downTime;
        float _pinchDist;
        Vector2 _pinchMid;
        bool _enabled = true;

        public event Action<Vector2> Tapped;
        /// <summary>Any camera gesture started (used to close tooltips etc.).</summary>
        public event Action GestureStarted;

        public void SetEnabled(bool on) { _enabled = on; if (!on) Release(); }

        void Release()
        {
            if (_mode == Mode.Drag) _camera.EndDrag();
            if (_mode == Mode.Intercepted) _interceptor?.EndDrag();
            _mode = Mode.None;
        }

        bool _pendingIntercept;

        void OnApplicationFocus(bool f) { if (!f) Release(); }
        void OnApplicationPause(bool p) { if (p) Release(); }

        float Slop => _tapSlop * Mathf.Max(0.5f, Screen.width / 1080f);

        void Update()
        {
            float dt = Time.unscaledDeltaTime;
            var mouse = UnityEngine.InputSystem.Mouse.current;
            if (_enabled && mouse != null && _mode == Mode.None)
            {
                float wheel = mouse.scroll.ReadValue().y;
                if (Mathf.Abs(wheel) > 0.01f && !OverUI(-1)) { _camera.Wheel(mouse.position.ReadValue(), wheel); GestureStarted?.Invoke(); }
            }

            int count = ReadTouches(out Vector2 p0, out Vector2 p1, out int id0);
            if (count == 0)
            {
                if (_mode == Mode.Pending && Time.unscaledTime - _downTime <= _tapMaxSeconds)
                {
                    if (_interceptor == null || !_interceptor.HandleTap(_lastPos)) Tapped?.Invoke(_lastPos);
                }
                Release();
                return;
            }
            if (!_enabled) { _mode = Mode.Blocked; return; }

            switch (_mode)
            {
                case Mode.None:
                    if (OverUI(id0)) { _mode = Mode.Blocked; return; }
                    _downPos = _lastPos = p0;
                    _downTime = Time.unscaledTime;
                    if (count < 2 && _interceptor != null && _interceptor.TryBeginDrag(p0)) { _mode = Mode.Intercepted; GestureStarted?.Invoke(); return; }
                    _mode = count >= 2 ? StartPinch(p0, p1) : Mode.Pending;
                    return;
                case Mode.Blocked:
                    return;
                case Mode.Intercepted:
                    if (count >= 2) { _interceptor?.EndDrag(); _mode = StartPinch(p0, p1); return; }
                    if (p0 != _lastPos) { _interceptor?.Drag(p0); _lastPos = p0; }
                    return;
                case Mode.Pending:
                    if (count >= 2) { _mode = StartPinch(p0, p1); return; }
                    _lastPos = p0;
                    if ((p0 - _downPos).magnitude > Slop || Time.unscaledTime - _downTime > _tapMaxSeconds)
                    {
                        _mode = Mode.Drag;
                        _camera.BeginDrag();
                        GestureStarted?.Invoke();
                        _camera.Drag(_downPos, p0, dt);
                    }
                    return;
                case Mode.Drag:
                    if (count >= 2) { _camera.EndDrag(); _mode = StartPinch(p0, p1); return; }
                    _camera.Drag(_lastPos, p0, dt);
                    _lastPos = p0;
                    return;
                case Mode.Pinch:
                    if (count < 2)
                    {
                        // continue as a drag with the remaining finger (no jump: re-anchor)
                        _mode = Mode.Drag; _camera.BeginDrag(); _lastPos = p0; return;
                    }
                    float d = Vector2.Distance(p0, p1);
                    Vector2 mid = (p0 + p1) * 0.5f;
                    _camera.Drag(_pinchMid, mid, dt);            // two-finger pan
                    if (_pinchDist > 1f && d > 1f) _camera.ZoomAt(mid, d / _pinchDist, true);
                    _pinchDist = d; _pinchMid = mid;
                    return;
            }
        }

        Mode StartPinch(Vector2 a, Vector2 b)
        {
            _pinchDist = Vector2.Distance(a, b);
            _pinchMid = (a + b) * 0.5f;
            _camera.StopMotion();
            GestureStarted?.Invoke();
            return Mode.Pinch;
        }

        static bool OverUI(int pointerId)
        {
            var es = EventSystem.current;
            if (es == null) return false;
            return pointerId >= 0 ? es.IsPointerOverGameObject(pointerId) : es.IsPointerOverGameObject();
        }

        static int ReadTouches(out Vector2 p0, out Vector2 p1, out int id0)
        {
            p0 = p1 = default; id0 = -1;
            var ts = UnityEngine.InputSystem.Touchscreen.current;
            int n = 0;
            if (ts != null)
            {
                for (int i = 0; i < ts.touches.Count && n < 2; i++)
                {
                    var t = ts.touches[i];
                    if (!t.press.isPressed) continue;
                    if (n == 0) { p0 = t.position.ReadValue(); id0 = t.touchId.ReadValue(); } else p1 = t.position.ReadValue();
                    n++;
                }
                if (n > 0) return n;
            }
            var mouse = UnityEngine.InputSystem.Mouse.current;
            if (mouse != null && mouse.leftButton.isPressed) { p0 = mouse.position.ReadValue(); return 1; }
            return 0;
        }

        // QA hooks (deterministic tests drive the same code paths without devices)
        public void QA_Tap(Vector2 p) => Tapped?.Invoke(p);
    }
}
