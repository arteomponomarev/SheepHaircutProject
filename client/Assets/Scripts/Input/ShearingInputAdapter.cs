using System;
using UnityEngine;
using UnityEngine.InputSystem;

namespace ShearAndGrow
{
    /// <summary>Device-independent pointer input. Contains no hit detection or shearing rules.</summary>
    [DisallowMultipleComponent]
    public sealed class ShearingInputAdapter : MonoBehaviour
    {
        [SerializeField] private InputActionAsset inputActions;

        public Vector2 PointerPosition { get; private set; }
        public bool IsHeld { get; private set; }
        public event Action<Vector2> PressBegan;
        public event Action<Vector2> Held;
        public event Action<Vector2> Released;
        public event Action Canceled;

        private InputActionAsset ownedActions;
        private InputAction positionAction;
        private InputAction pressAction;
        private InputDevice activeDevice;
        private bool hasFocus = true;
        private bool isPaused;
        private bool Suspended => !hasFocus || isPaused;

        private void OnEnable()
        {
            if (inputActions == null)
            {
                Debug.LogError("ShearingInputAdapter needs a shearing input actions asset.", this);
                enabled = false;
                return;
            }

            // Each adapter owns its action lifecycle; disabling it cannot disable another consumer.
            ownedActions = Instantiate(inputActions);
            positionAction = ownedActions.FindAction("Shearing/Position", true);
            pressAction = ownedActions.FindAction("Shearing/Press", true);
            positionAction.performed += OnPosition;
            pressAction.performed += OnPress;
            pressAction.canceled += OnActionCanceled;
            InputSystem.onDeviceChange += OnDeviceChange;
            hasFocus = Application.isFocused;
            isPaused = false;
            ownedActions.Enable();
        }

        private void Update()
        {
            if (IsHeld)
                Held?.Invoke(PointerPosition);
        }

        private void OnPosition(InputAction.CallbackContext context)
        {
            if (!Suspended && (!IsHeld || context.control.device == activeDevice))
                PointerPosition = context.ReadValue<Vector2>();
        }

        private void OnPress(InputAction.CallbackContext context)
        {
            if (Suspended)
                return;

            InputDevice device = context.control.device;
            bool pressed = context.ReadValue<float>() >= 0.5f;
            if (pressed)
            {
                if (IsHeld)
                    return;

                activeDevice = device;
                PointerPosition = ReadPosition(device);
                IsHeld = true;
                PressBegan?.Invoke(PointerPosition);
            }
            else if (IsHeld && device == activeDevice)
            {
                PointerPosition = ReadPosition(device);
                if (device is Touchscreen touchscreen &&
                    touchscreen.primaryTouch.phase.ReadValue() == UnityEngine.InputSystem.TouchPhase.Canceled)
                {
                    Cancel();
                    return;
                }

                IsHeld = false;
                activeDevice = null;
                Released?.Invoke(PointerPosition);
            }
        }

        private static Vector2 ReadPosition(InputDevice device)
        {
            if (device is Touchscreen touchscreen)
                return touchscreen.primaryTouch.position.ReadValue();
            return device is Mouse mouse ? mouse.position.ReadValue() : Vector2.zero;
        }

        /// <summary>Ends an interrupted gesture without reporting a normal release.</summary>
        public void Cancel()
        {
            if (!IsHeld)
                return;
            IsHeld = false;
            activeDevice = null;
            Canceled?.Invoke();
        }

        private void OnActionCanceled(InputAction.CallbackContext context) => Cancel();

        private void OnDeviceChange(InputDevice device, InputDeviceChange change)
        {
            if (device == activeDevice && (change == InputDeviceChange.Disconnected ||
                change == InputDeviceChange.Removed || change == InputDeviceChange.Disabled ||
                change == InputDeviceChange.SoftReset || change == InputDeviceChange.HardReset))
                Cancel();
        }

        private void OnApplicationFocus(bool focused)
        {
            hasFocus = focused;
            if (!focused)
                Cancel();
        }

        private void OnApplicationPause(bool paused)
        {
            isPaused = paused;
            if (paused)
                Cancel();
        }

        private void OnDisable()
        {
            Cancel();
            InputSystem.onDeviceChange -= OnDeviceChange;
            if (ownedActions == null)
                return;

            positionAction.performed -= OnPosition;
            pressAction.performed -= OnPress;
            pressAction.canceled -= OnActionCanceled;
            ownedActions.Disable();
            Destroy(ownedActions);
            ownedActions = null;
        }
    }
}
