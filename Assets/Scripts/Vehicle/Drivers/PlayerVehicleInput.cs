using Touge.Core;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Touge.Vehicle.Drivers
{
    /// <summary>
    /// Reads the local player's devices and presents them through <see cref="IVehicleInput"/>.
    ///
    /// Actions are resolved by NAME from a serialized <see cref="InputActionAsset"/> rather than
    /// through a generated wrapper class. That keeps the asset as the single source of truth - you
    /// can rebind or add devices in the Input Actions editor without regenerating or recompiling
    /// anything.
    ///
    /// Sampling contract: continuous axes are polled in Update and cached; momentary buttons are
    /// LATCHED on press and only cleared by <see cref="ConsumeEdges"/> from the physics step. With a
    /// 200 Hz physics rate and a 60 Hz display there are several physics steps per frame, so without
    /// latching a shift request would either be missed or applied several times.
    /// </summary>
    [DisallowMultipleComponent]
    public class PlayerVehicleInput : MonoBehaviour, IVehicleInput
    {
        [Header("Input asset")]
        [Tooltip("TougeControls.inputactions. The 'Driving' action map is enabled automatically.")]
        [SerializeField] private InputActionAsset actionAsset;

        [Tooltip("Name of the action map to read driving controls from.")]
        [SerializeField] private string actionMapName = "Driving";

        [Header("Feel")]
        [Tooltip("Deadzone applied to the steering axis after the device's own processing. " +
                 "Only affects analog sticks; keyboard input is already discrete.")]
        [Range(0f, 0.4f)]
        [SerializeField] private float steerDeadzone = 0.05f;

        [Tooltip("Invert the steering axis (for unusual controller setups).")]
        [SerializeField] private bool invertSteer;

        private InputActionMap _map;
        private InputAction _throttle;
        private InputAction _brake;
        private InputAction _steer;
        private InputAction _handbrake;
        private InputAction _clutch;
        private InputAction _shiftUp;
        private InputAction _shiftDown;
        private InputAction _toggleTransmission;

        public float Throttle { get; private set; }
        public float Brake { get; private set; }
        public float Steer { get; private set; }
        public float Handbrake { get; private set; }
        public float Clutch { get; private set; }
        public bool ShiftUpPressed { get; private set; }
        public bool ShiftDownPressed { get; private set; }
        public bool ToggleTransmissionModePressed { get; private set; }

        /// <summary>Exposed so debug UI and the camera can read their own maps from the same asset.</summary>
        public InputActionAsset ActionAsset => actionAsset;

        private void Awake()
        {
            if (actionAsset == null)
            {
                Debug.LogError($"{nameof(PlayerVehicleInput)} on '{name}' has no InputActionAsset assigned. " +
                               "Assign TougeControls.inputactions.", this);
                enabled = false;
                return;
            }

            _map = actionAsset.FindActionMap(actionMapName, throwIfNotFound: false);
            if (_map == null)
            {
                Debug.LogError($"Action map '{actionMapName}' not found in '{actionAsset.name}'.", this);
                enabled = false;
                return;
            }

            _throttle = _map.FindAction("Throttle", throwIfNotFound: false);
            _brake = _map.FindAction("Brake", throwIfNotFound: false);
            _steer = _map.FindAction("Steer", throwIfNotFound: false);
            _handbrake = _map.FindAction("Handbrake", throwIfNotFound: false);
            _clutch = _map.FindAction("Clutch", throwIfNotFound: false);
            _shiftUp = _map.FindAction("ShiftUp", throwIfNotFound: false);
            _shiftDown = _map.FindAction("ShiftDown", throwIfNotFound: false);
            _toggleTransmission = _map.FindAction("ToggleTransmission", throwIfNotFound: false);
        }

        private void OnEnable() => _map?.Enable();

        private void OnDisable()
        {
            _map?.Disable();
            // Leave the car with no input rather than whatever was held when control was removed.
            Throttle = Brake = Steer = Handbrake = Clutch = 0f;
            ShiftUpPressed = ShiftDownPressed = ToggleTransmissionModePressed = false;
        }

        private void Update() => Sample(Time.deltaTime);

        /// <inheritdoc />
        public void Sample(float deltaTime)
        {
            if (_map == null) return;

            Throttle = Mathf.Clamp01(ReadAxis(_throttle));
            Brake = Mathf.Clamp01(ReadAxis(_brake));
            Handbrake = Mathf.Clamp01(ReadAxis(_handbrake));
            Clutch = Mathf.Clamp01(ReadAxis(_clutch));

            float rawSteer = Mathf.Clamp(ReadAxis(_steer), -1f, 1f);
            Steer = TougeMath.ApplyDeadzone(rawSteer, steerDeadzone) * (invertSteer ? -1f : 1f);

            // OR into the latch: a press survives until the physics step consumes it.
            ShiftUpPressed |= WasPressed(_shiftUp);
            ShiftDownPressed |= WasPressed(_shiftDown);
            ToggleTransmissionModePressed |= WasPressed(_toggleTransmission);
        }

        /// <inheritdoc />
        public void ConsumeEdges()
        {
            ShiftUpPressed = false;
            ShiftDownPressed = false;
            ToggleTransmissionModePressed = false;
        }

        private static float ReadAxis(InputAction action) => action?.ReadValue<float>() ?? 0f;

        private static bool WasPressed(InputAction action) => action != null && action.WasPressedThisFrame();
    }
}
