using Touge.Core;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Touge.CameraRig
{
    /// <summary>
    /// Isometric-style chase camera.
    ///
    /// Sits at a fixed elevation angle and a diagonal offset, which is what gives the touge descent
    /// its readable, map-like presentation. Two yaw behaviours are available:
    ///
    ///   FixedYaw   - the camera angle never changes. Maximum readability and a consistent mental map
    ///                of which way "down the hill" is, at the cost of the car sometimes travelling
    ///                toward the bottom of the screen on a hairpin.
    ///   SoftFollow - yaw drifts slowly toward the direction of travel, so a long winding descent
    ///                keeps the road ahead on screen. Deliberately heavily damped: snapping to the
    ///                car's heading would make a drift unreadable, because the car's heading and its
    ///                direction of travel are precisely what differ during one.
    ///
    /// Runs in LateUpdate so it sees the interpolated rigidbody position for the current frame.
    /// </summary>
    [RequireComponent(typeof(Camera))]
    [DisallowMultipleComponent]
    public class IsoFollowCamera : MonoBehaviour
    {
        [Header("Target")]
        [Tooltip("Transform to follow. Usually the car's root.")]
        [SerializeField] private Transform target;

        [Tooltip("Rigidbody used to read velocity for soft-follow yaw and speed zoom. " +
                 "Optional - without it the camera still follows, just without those two features.")]
        [SerializeField] private Rigidbody targetBody;

        [Tooltip("Point the camera aims at, offset from the target in its local space. " +
                 "Raising Y keeps the car in the lower half of the frame, showing more of the road ahead.")]
        [SerializeField] private Vector3 lookAtOffset = new Vector3(0f, 1.0f, 0f);

        [Header("Framing")]
        [Tooltip("Elevation angle above the horizon. 35-45 reads as isometric; higher is more top-down. [degrees]")]
        [Range(15f, 80f)]
        [SerializeField] private float pitch = 40f;

        [Tooltip("Compass angle used in FixedYaw mode. 45 gives the classic diagonal isometric view. [degrees]")]
        [SerializeField] private float fixedYaw = 45f;

        [Tooltip("Distance from the look-at point back along the view direction. [m]")]
        [SerializeField] private float distance = 15f;

        [Header("Follow mode")]
        [SerializeField] private CameraFollowMode mode = CameraFollowMode.FixedYaw;

        [Tooltip("Smoothing time for camera position. Higher = looser, laggier chase. [s]")]
        [Range(0f, 1f)]
        [SerializeField] private float positionSmoothTime = 0.12f;

        [Tooltip("Smoothing time for yaw in SoftFollow mode. Deliberately large so the camera " +
                 "reads the road rather than chasing every slide. [s]")]
        [Range(0f, 4f)]
        [SerializeField] private float yawSmoothTime = 1.2f;

        [Tooltip("Below this speed the yaw holds still, since the direction of travel is meaningless " +
                 "when nearly stationary. [m/s]")]
        [SerializeField] private float yawMinSpeed = 4f;

        [Header("Projection")]
        [Tooltip("Orthographic reads as true isometric; perspective gives a better sense of gradient.")]
        [SerializeField] private bool orthographic = true;

        [Tooltip("Orthographic half-height at a standstill. [m]")]
        [SerializeField] private float baseOrthoSize = 9f;

        [Tooltip("Field of view used in perspective mode. [degrees]")]
        [SerializeField] private float perspectiveFov = 40f;

        [Header("Speed zoom")]
        [Tooltip("How much the view pulls back per km/h of speed, so fast sections show more road.")]
        [SerializeField] private float zoomPerKph = 0.035f;

        [Tooltip("Maximum additional pull-back from speed zoom. [m]")]
        [SerializeField] private float maxSpeedZoom = 6f;

        [Tooltip("Smoothing time for the speed zoom, so it does not pump under braking. [s]")]
        [Range(0f, 2f)]
        [SerializeField] private float zoomSmoothTime = 0.5f;

        [Header("Input")]
        [Tooltip("TougeControls.inputactions. Reads ToggleCameraMode (F2) and ToggleProjection (F3) " +
                 "from the Debug map. Optional.")]
        [SerializeField] private InputActionAsset actionAsset;

        private Camera _camera;
        private float _currentYaw;
        private float _currentZoom;
        private Vector3 _currentPosition;
        private bool _initialised;

        private InputAction _toggleModeAction;
        private InputAction _toggleProjectionAction;

        /// <summary>Current yaw behaviour. Settable so a game mode can force a view.</summary>
        public CameraFollowMode Mode
        {
            get => mode;
            set => mode = value;
        }

        /// <summary>Assign the followed car at runtime (used by the scene builder and future spawners).</summary>
        public void SetTarget(Transform newTarget, Rigidbody newBody = null)
        {
            target = newTarget;
            targetBody = newBody;
            _initialised = false;
        }

        private void Awake()
        {
            _camera = GetComponent<Camera>();
            _currentYaw = fixedYaw;

            InputActionMap debugMap = actionAsset != null
                ? actionAsset.FindActionMap("Debug", throwIfNotFound: false)
                : null;

            if (debugMap != null)
            {
                debugMap.Enable();
                _toggleModeAction = debugMap.FindAction("ToggleCameraMode", throwIfNotFound: false);
                _toggleProjectionAction = debugMap.FindAction("ToggleProjection", throwIfNotFound: false);
            }
        }

        private void LateUpdate()
        {
            HandleToggles();

            if (target == null) return;

            float dt = Time.deltaTime;
            Vector3 velocity = targetBody != null ? targetBody.linearVelocity : Vector3.zero;
            float speed = velocity.magnitude;

            // ---- Yaw ----------------------------------------------------------------------------
            float desiredYaw = fixedYaw;
            if (mode == CameraFollowMode.SoftFollow && speed >= yawMinSpeed)
            {
                // Track the VELOCITY direction, not the car's facing. During a drift those differ by
                // the drift angle, and following the facing would swing the camera with every slide.
                Vector3 flatVelocity = new Vector3(velocity.x, 0f, velocity.z);
                if (flatVelocity.sqrMagnitude > TougeMath.Epsilon)
                {
                    // Offset by fixedYaw so the view stays diagonal relative to travel rather than
                    // sitting directly behind the car - otherwise it stops reading as isometric.
                    desiredYaw = Quaternion.LookRotation(flatVelocity).eulerAngles.y + fixedYaw;
                }
            }
            else if (mode == CameraFollowMode.SoftFollow)
            {
                desiredYaw = _currentYaw;   // Too slow to have a meaningful heading: hold.
            }

            _currentYaw = TougeMath.DampAngle(_currentYaw, desiredYaw, yawSmoothTime, dt);

            // ---- Speed zoom ---------------------------------------------------------------------
            float targetZoom = Mathf.Min(TougeMath.MpsToKph(speed) * zoomPerKph, maxSpeedZoom);
            _currentZoom = TougeMath.Damp(_currentZoom, targetZoom, zoomSmoothTime, dt);

            // ---- Position -----------------------------------------------------------------------
            Vector3 lookAtPoint = target.TransformPoint(lookAtOffset);
            Quaternion orbit = Quaternion.Euler(pitch, _currentYaw, 0f);
            Vector3 desiredPosition = lookAtPoint + orbit * Vector3.back * (distance + _currentZoom);

            if (!_initialised)
            {
                // Snap on the first frame and after a target change, so respawning does not produce
                // a long sweep across the level.
                _currentPosition = desiredPosition;
                _initialised = true;
            }
            else
            {
                _currentPosition = TougeMath.Damp(_currentPosition, desiredPosition, positionSmoothTime, dt);
            }

            transform.SetPositionAndRotation(_currentPosition, Quaternion.LookRotation(lookAtPoint - _currentPosition));

            // ---- Projection ---------------------------------------------------------------------
            _camera.orthographic = orthographic;
            if (orthographic)
                _camera.orthographicSize = baseOrthoSize + _currentZoom;
            else
                _camera.fieldOfView = perspectiveFov;
        }

        private void HandleToggles()
        {
            if (_toggleModeAction != null && _toggleModeAction.WasPressedThisFrame())
            {
                mode = mode == CameraFollowMode.FixedYaw
                    ? CameraFollowMode.SoftFollow
                    : CameraFollowMode.FixedYaw;
            }

            if (_toggleProjectionAction != null && _toggleProjectionAction.WasPressedThisFrame())
                orthographic = !orthographic;
        }
    }
}
