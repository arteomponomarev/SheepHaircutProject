using System;
using UnityEngine;
using UnityEngine.EventSystems;

namespace ShearAndGrow.Shearing
{
    /// <summary>
    /// Touch / mouse gesture state machine (spec §7.2). No mask maths here.
    /// A gesture is classified ONCE at touch-down: started on the sheep = shear, elsewhere = camera orbit, over UI = ignored.
    /// A second finger during a shear safely cancels the stroke and the gesture is ignored until all fingers lift.
    /// </summary>
    public sealed class ShearingInputAdapter : MonoBehaviour
    {
        enum Gesture { None, Shear, Orbit, Pinch, Ignored }

        [SerializeField] private float _doubleTapTime = 0.3f;
        [SerializeField] private float _doubleTapMaxMove = 30f;

        Gesture _gesture;
        Vector2 _lastPos;
        float _lastTapTime = -10f;
        Vector2 _lastTapPos;
        Vector2 _downPos;
        bool _enabled = true;

        /// <summary>Returns true if the screen position hits the shearing collider.</summary>
        public Func<Vector2, bool> HitsShearable;
        /// <summary>Raised every frame while a shear gesture is active (screen position).</summary>
        public event Action<Vector2> ShearMove;
        /// <summary>Raised on release, cancel, multi-touch, focus loss.</summary>
        public event Action ShearEnd;
        public event Action<Vector2> OrbitDelta;
        /// <summary>Zoom ratio (&gt;1 = zoom in): two-finger pinch, or mouse wheel in the Editor/desktop.</summary>
        public event Action<float> ZoomDelta;
        public event Action ResetViewRequested;

        [Tooltip("Zoom ratio per mouse-wheel notch.")]
        [SerializeField] private float _wheelZoomStep = 0.08f;
        float _pinchStartDistance;

        public bool IsShearing => _gesture == Gesture.Shear;

        public void SetInputEnabled(bool enabled)
        {
            _enabled = enabled;
            if (!enabled) CancelAll();
        }

        public void CancelAll()
        {
            if (_gesture == Gesture.Shear) ShearEnd?.Invoke();
            _gesture = _gesture == Gesture.None ? Gesture.None : Gesture.Ignored;
        }

        void OnApplicationFocus(bool focus) { if (!focus) CancelAll(); }
        void OnApplicationPause(bool paused) { if (paused) CancelAll(); }
        void OnDisable() => CancelAll();

        void Update()
        {
            ReadPointer(out bool pressed, out Vector2 pos, out int touchCount, out int pointerId);

            // Mouse wheel zoom (Editor / desktop) – never while cutting, never over UI.
            var mouse = UnityEngine.InputSystem.Mouse.current;
            if (_enabled && mouse != null && _gesture != Gesture.Shear)
            {
                float wheel = mouse.scroll.ReadValue().y;
                if (Mathf.Abs(wheel) > 0.01f && !IsOverUI(-1))
                    ZoomDelta?.Invoke(1f + Mathf.Sign(wheel) * _wheelZoomStep);
            }

            if (!pressed)
            {
                if (_gesture == Gesture.Shear) ShearEnd?.Invoke();
                _gesture = Gesture.None;
                return;
            }

            if (!_enabled) { _gesture = Gesture.Ignored; return; }

            // Two fingers: pinch-zoom. Cancels a running stroke first (no accidental lines), never orbits.
            if (touchCount >= 2 && TryReadTwoTouches(out Vector2 a, out Vector2 b))
            {
                if (_gesture == Gesture.Shear) ShearEnd?.Invoke();
                float dist = Vector2.Distance(a, b);
                if (_gesture != Gesture.Pinch) { _gesture = Gesture.Pinch; _pinchStartDistance = dist; }
                else if (_pinchStartDistance > 1f && dist > 1f)
                {
                    ZoomDelta?.Invoke(dist / _pinchStartDistance);
                    _pinchStartDistance = dist;
                }
                return;
            }
            // Lifting one finger after a pinch must not start a stroke or an orbit with the remaining finger.
            if (_gesture == Gesture.Pinch) { _gesture = Gesture.Ignored; return; }

            if (_gesture == Gesture.None)
            {
                _downPos = pos;
                _lastPos = pos;
                if (touchCount > 1 || IsOverUI(pointerId)) { _gesture = Gesture.Ignored; return; }
                if (HitsShearable != null && HitsShearable(pos))
                {
                    _gesture = Gesture.Shear;
                }
                else
                {
                    _gesture = Gesture.Orbit;
                    if (Time.unscaledTime - _lastTapTime < _doubleTapTime && (pos - _lastTapPos).magnitude < _doubleTapMaxMove)
                    {
                        ResetViewRequested?.Invoke();
                        _lastTapTime = -10f;
                    }
                    else
                    {
                        _lastTapTime = Time.unscaledTime;
                        _lastTapPos = pos;
                    }
                }
            }

            if (touchCount > 1 && _gesture == Gesture.Shear)
            {
                // Explicit rule: second finger cancels the stroke; no unintended lines.
                ShearEnd?.Invoke();
                _gesture = Gesture.Ignored;
                return;
            }

            switch (_gesture)
            {
                case Gesture.Shear:
                    ShearMove?.Invoke(pos);
                    break;
                case Gesture.Orbit:
                    if (touchCount <= 1) OrbitDelta?.Invoke(pos - _lastPos);
                    break;
            }
            _lastPos = pos;
        }

        static bool TryReadTwoTouches(out Vector2 a, out Vector2 b)
        {
            a = b = default;
            var ts = UnityEngine.InputSystem.Touchscreen.current;
            if (ts == null) return false;
            int found = 0;
            for (int i = 0; i < ts.touches.Count && found < 2; i++)
            {
                if (!ts.touches[i].press.isPressed) continue;
                if (found == 0) a = ts.touches[i].position.ReadValue(); else b = ts.touches[i].position.ReadValue();
                found++;
            }
            return found == 2;
        }

        static bool IsOverUI(int pointerId)
        {
            var es = EventSystem.current;
            if (es == null) return false;
            return pointerId >= 0 ? es.IsPointerOverGameObject(pointerId) : es.IsPointerOverGameObject();
        }

        static void ReadPointer(out bool pressed, out Vector2 pos, out int touchCount, out int pointerId)
        {
            pressed = false; pos = default; touchCount = 0; pointerId = -1;
            var ts = UnityEngine.InputSystem.Touchscreen.current;
            if (ts != null)
            {
                int count = 0;
                int primaryIndex = -1;
                for (int i = 0; i < ts.touches.Count; i++)
                {
                    if (ts.touches[i].press.isPressed)
                    {
                        if (primaryIndex < 0) primaryIndex = i;
                        count++;
                    }
                }
                if (count > 0)
                {
                    var t = ts.primaryTouch.press.isPressed ? ts.primaryTouch : ts.touches[primaryIndex];
                    pressed = true;
                    pos = t.position.ReadValue();
                    touchCount = count;
                    pointerId = t.touchId.ReadValue();
                    return;
                }
            }
            var mouse = UnityEngine.InputSystem.Mouse.current;
            if (mouse != null && mouse.leftButton.isPressed)
            {
                pressed = true;
                pos = mouse.position.ReadValue();
                touchCount = 1;
                pointerId = -1;
            }
        }
    }
}
