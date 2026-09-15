using UnityEngine;
using UnityEngine.InputSystem;

namespace Touge.Vehicle.Drivers
{
    /// <summary>
    /// Recovery control: puts the car back on its wheels where it currently sits.
    ///
    /// Resets in place rather than teleporting to the spawn point, because while tuning you almost
    /// always want to carry on from where you went wrong rather than drive back out to the same
    /// corner again.
    ///
    /// This is a development convenience. Once checkpoints exist, a game mode will own respawning
    /// properly (last checkpoint, facing down the road, with a countdown) and this can be retired or
    /// folded into it.
    /// </summary>
    [RequireComponent(typeof(CarController))]
    [DisallowMultipleComponent]
    public class CarResetHandler : MonoBehaviour
    {
        [Tooltip("TougeControls.inputactions. Reads ResetCar (R / gamepad north) from the Debug map.")]
        [SerializeField] private InputActionAsset actionAsset;

        [Tooltip("How far above the current position to place the car when recovering. [m]\n" +
                 "Needs to clear whatever it landed on, without dropping it from a height.")]
        [SerializeField] private float recoveryHeight = 0.8f;

        private CarController _car;
        private InputAction _resetAction;
        private Vector3 _spawnPosition;
        private Quaternion _spawnRotation;

        private void Awake()
        {
            _car = GetComponent<CarController>();
            _spawnPosition = transform.position;
            _spawnRotation = transform.rotation;

            InputActionMap debugMap = actionAsset != null
                ? actionAsset.FindActionMap("Debug", throwIfNotFound: false)
                : null;

            if (debugMap != null)
            {
                debugMap.Enable();
                _resetAction = debugMap.FindAction("ResetCar", throwIfNotFound: false);
            }
        }

        private void Update()
        {
            if (_resetAction != null && _resetAction.WasPressedThisFrame()) RecoverInPlace();
        }

        /// <summary>Set the car upright at its current location, preserving which way it was facing.</summary>
        public void RecoverInPlace()
        {
            // Keep only the yaw component: flattening pitch and roll is the whole point of a recovery.
            float yaw = transform.eulerAngles.y;
            _car.ResetTo(transform.position + Vector3.up * recoveryHeight, Quaternion.Euler(0f, yaw, 0f));
        }

        /// <summary>Return the car to where it started the scene.</summary>
        public void ReturnToSpawn() => _car.ResetTo(_spawnPosition, _spawnRotation);
    }
}
