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

        [Tooltip("World yaw. Fixed, so rotation reads as the car turning rather than the view turning.")]
        public float yaw = 45f;

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

        private void Awake()
        {
            _camera = GetComponent<Camera>();
            if (target == null)
            {
                ArcadeDriftCar car = FindFirstObjectByType<ArcadeDriftCar>();
                if (car != null) target = car.transform;
            }
        }

        private void LateUpdate()
        {
            _camera.orthographic = true;
            _camera.orthographicSize = viewSize;

            Quaternion rotation = Quaternion.Euler(pitch, yaw, 0f);
            transform.rotation = rotation;

            if (target == null) return;

            Vector3 wanted = target.position - rotation * Vector3.forward * distance;
            transform.position = Vector3.SmoothDamp(transform.position, wanted, ref _velocity, smoothTime);
        }
    }
}
