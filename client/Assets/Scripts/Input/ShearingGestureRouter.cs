using System;
using UnityEngine;

namespace ShearAndGrow
{
    /// <summary>Locks each pointer gesture to shearing, orbit, or UI until release/cancel.</summary>
    public sealed class ShearingGestureRouter : MonoBehaviour
    {
        [SerializeField] private ShearingInputAdapter input;
        [SerializeField] private ShearingSurfaceRaycaster surface;
        [SerializeField] private PointerUiBlocker ui;
        public bool IsShearing => mode == Gesture.Shearing;
        public bool IsOrbiting => mode == Gesture.Orbit;
        public event Action<Vector2> ShearPosition;
        public event Action<Vector2> OrbitDelta;
        public event Action GestureEnded;
        private enum Gesture { None, Shearing, Orbit, Blocked }
        private Gesture mode;
        private Vector2 previousPosition;

        private void OnEnable()
        {
            if (input == null || surface == null || ui == null)
            { Debug.LogError("Gesture router requires input, surface and UI references.", this); enabled = false; return; }
            input.PressBegan += Begin; input.Held += Move;
            input.Released += Release; input.Canceled += End;
        }
        private void Begin(Vector2 position)
        {
            End(); previousPosition = position;
            if (ui.Contains(position)) mode = Gesture.Blocked;
            else if (surface.TryGetHit(position, out _)) mode = Gesture.Shearing;
            else mode = surface.IsOverSheep(position) ? Gesture.Blocked : Gesture.Orbit;
            if (IsShearing) ShearPosition?.Invoke(position);
        }
        private void Move(Vector2 position)
        {
            if (IsShearing) ShearPosition?.Invoke(position);
            else if (IsOrbiting) OrbitDelta?.Invoke(position - previousPosition);
            previousPosition = position;
        }
        private void Release(Vector2 position) => End();
        private void End() { mode = Gesture.None; GestureEnded?.Invoke(); }
        private void OnDisable()
        {
            if (input != null)
            { input.PressBegan -= Begin; input.Held -= Move; input.Released -= Release; input.Canceled -= End; }
            End();
        }
    }
}
