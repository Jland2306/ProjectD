using UnityEngine;

namespace Touge.ArcadeDrift
{
    /// <summary>
    /// Isometric follow camera for the drift lab. Fixed world yaw, so the car visibly rotates
    /// underneath it - which is the whole point when you are judging a drift.
    ///
    /// Separate from the car on purpose: the brief is one VEHICLE MonoBehaviour, and a camera that
    /// lived inside the car would be handling code you have to read past every time you tune grip.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Camera))]
    public class ArcadeChaseCamera : MonoBehaviour
    {
        [Tooltip("What to follow. Left empty, it finds the car in the scene at startup.")]
        public Transform target;

        [Tooltip("Resting world yaw. At yawFollow 0 the view never rotates and the car turns under it.")]
        public float yaw = 45f;

        [Tooltip("How much the view swings round to follow the car's travel direction, 0-1. " +
                 "0 = pure fixed-yaw isometric, good on an open pad. On a winding descent a little " +
                 "follow keeps the road ahead on screen; too much and you stop reading the drift " +
                 "angle, because the car stays pointed up the screen.")]
        [Range(0f, 1f)]
        public float yawFollow;

        [Tooltip("Lag on that swing. [s] Long on purpose - a camera that snaps round mid-drift is " +
                 "unreadable.")]
        public float yawSmoothTime = 1.2f;

        [Tooltip("Downward tilt. [degrees]")]
        public float pitch = 42f;

        [Tooltip("Distance back along the view axis. [m]")]
        public float distance = 24f;

        [Tooltip("Orthographic half-height. [m] Larger shows more track.")]
        public float viewSize = 14f;

        [Tooltip("Follow lag. [s] Higher trails further behind and reads as more speed.")]
        public float smoothTime = 0.14f;

        private Camera _camera;
        private Vector3 _velocity;
        private float _yaw, _yawRate;
        private Rigidbody _body;

        private void Awake()
        {
            _camera = GetComponent<Camera>();
            if (target == null)
            {
                ArcadeDriftCar car = FindFirstObjectByType<ArcadeDriftCar>();
                if (car != null) target = car.transform;
            }

            _yaw = yaw;
            if (target != null) _body = target.GetComponent<Rigidbody>();
        }

        private void LateUpdate()
        {
            _camera.orthographic = true;
            _camera.orthographicSize = viewSize;

            if (target == null) return;

            // Chase the direction of TRAVEL, not the car's heading. Following the heading would swing
            // the camera round with every drift and hide the very thing you are trying to judge.
            float wanted = yaw;
            if (yawFollow > 0f && _body != null)
            {
                Vector3 flat = Vector3.ProjectOnPlane(_body.linearVelocity, Vector3.up);
                if (flat.sqrMagnitude > 4f)
                {
                    float travel = Mathf.Atan2(flat.x, flat.z) * Mathf.Rad2Deg;
                    wanted = yaw + Mathf.DeltaAngle(yaw, travel) * yawFollow;
                }
            }

            _yaw = Mathf.SmoothDampAngle(_yaw, wanted, ref _yawRate, Mathf.Max(yawSmoothTime, 0.01f));

            Quaternion rotation = Quaternion.Euler(pitch, _yaw, 0f);
            transform.rotation = rotation;

            Vector3 seat = target.position - rotation * Vector3.forward * distance;
            transform.position = Vector3.SmoothDamp(transform.position, seat, ref _velocity, smoothTime);
        }
    }
}
