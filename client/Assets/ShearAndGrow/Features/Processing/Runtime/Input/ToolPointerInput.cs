using System;
using UnityEngine;
using UnityEngine.EventSystems;

namespace ShearAndGrow.Processing
{
    /// <summary>
    /// Single-pointer tool input for the processing mini-games (New Input System, touch + mouse).
    /// Press starts the tool, drag moves it, release stops it in the same frame. Presses over UI are ignored;
    /// a second finger cancels the stroke until all fingers are lifted. Focus loss / pause release immediately.
    /// </summary>
    public sealed class ToolPointerInput : MonoBehaviour
    {
        public event Action<Vector2> Pressed;
        public event Action<Vector2> Moved;
        public event Action Released;

        bool _active, _ignoreUntilUp, _enabled = true;
        public bool IsActive => _active;
        public Vector2 Position { get; private set; }

        public void SetEnabled(bool enabled)
        {
            _enabled = enabled;
            if (!enabled) Cancel();
        }

        public void Cancel()
        {
            if (_active) { _active = false; Released?.Invoke(); }
            _ignoreUntilUp = true;
        }

        void OnApplicationFocus(bool focus) { if (!focus) Cancel(); }
        void OnApplicationPause(bool paused) { if (paused) Cancel(); }
        void OnDisable() { if (_active) { _active = false; Released?.Invoke(); } }

        void Update()
        {
            Read(out bool pressed, out Vector2 pos, out int count, out int id);
            if (!pressed)
            {
                if (_active) { _active = false; Released?.Invoke(); }
                _ignoreUntilUp = false;
                return;
            }
            if (!_enabled || _ignoreUntilUp) return;
            if (count > 1) { Cancel(); return; }
            Position = pos;
            if (!_active)
            {
                if (IsOverUI(id)) { _ignoreUntilUp = true; return; }
                _active = true;
                Pressed?.Invoke(pos);
                return;
            }
            Moved?.Invoke(pos);
        }

        static bool IsOverUI(int pointerId)
        {
            var es = EventSystem.current;
            if (es == null) return false;
            return pointerId >= 0 ? es.IsPointerOverGameObject(pointerId) : es.IsPointerOverGameObject();
        }

        static void Read(out bool pressed, out Vector2 pos, out int count, out int id)
        {
            pressed = false; pos = default; count = 0; id = -1;
            var ts = UnityEngine.InputSystem.Touchscreen.current;
            if (ts != null)
            {
                int first = -1;
                for (int i = 0; i < ts.touches.Count; i++)
                    if (ts.touches[i].press.isPressed) { if (first < 0) first = i; count++; }
                if (count > 0)
                {
                    var t = ts.primaryTouch.press.isPressed ? ts.primaryTouch : ts.touches[first];
                    pressed = true; pos = t.position.ReadValue(); id = t.touchId.ReadValue();
                    return;
                }
            }
            var mouse = UnityEngine.InputSystem.Mouse.current;
            if (mouse != null && mouse.leftButton.isPressed) { pressed = true; pos = mouse.position.ReadValue(); count = 1; }
        }
    }
}
