using UnityEngine;
using UnityEngine.InputSystem;

namespace Touge.ArcadeDrift
{
    /// <summary>
    /// A whole drift car in one file: two virtual axles, each with a lateral grip budget.
    ///
    /// THE ENTIRE MODEL. Twice per physics step, at a point ahead of the centre of mass and a point
    /// behind it:
    ///   1. measure how fast that point is sliding sideways,
    ///   2. work out the force that would stop it sliding outright,
    ///   3. refuse to produce more than that axle's grip limit,
    ///   4. apply what is left at the axle's position, which yaws the car as a side effect.
    ///
    /// Step 3 is the game. While both axles meet their demand the car grips and turns; when the rear
    /// cannot, the back keeps sliding while the front does not and the car rotates. A drift is the
    /// same four lines of code as an ordinary corner.
    ///
    /// DELIBERATELY ABSENT: no yaw correction, no drift-angle term, no counter-steer assist, no
    /// stability control. If it spins, you asked the rear for more than rearGrip.
    ///
    /// TWO RULES WORTH KNOWING BEFORE YOU TUNE.
    ///
    /// 1. IS A SLIDE CATCHABLE? While both axles slide each makes exactly its limit, so the yaw
    ///    torque each contributes is just grip * offset:
    ///        frontGrip * frontAxleOffset   vs   rearGrip * rearAxleOffset
    ///    More than ~10% front-heavy and the car gains yaw rate on its own for as long as it slides -
    ///    divergent, not loose, and no counter-steer brings it back. Within a few percent and a slide
    ///    holds where you put it, because counter-steering shrinks the front side and the rear wins.
    ///    Defaults: 21*1.13 = 23.7 against 15*1.6 = 24.0, neutral on purpose.
    ///
    /// 2. MORE LOCK IS NOT MORE TURN. At speed the front saturates within a few degrees, so extra
    ///    steering adds no force - it only rotates the force already there. The part that yaws the
    ///    car goes as cos(steer), the part that fights the engine as sin(steer), so past saturation
    ///    every extra degree rotates LESS and scrubs off MORE speed. Cornering is limited by
    ///    frontGrip, not by lock: raise frontGrip and drop frontAxleOffset to keep rule 1.
    ///
    /// TUNING ORDER when it feels wrong:
    ///   rearGrip           the drift knob, and what decides rule 1.
    ///   frontGrip          how tight it corners on the grip (keep the product, see rule 2).
    ///   handbrakeGripScale how violently the handbrake breaks the rear away.
    ///   rearAxleOffset     leverage. Further back = the rear resists rotation harder.
    ///   steerSpeed         below ~200 deg/s slides stop being saveable, whatever the grips say.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    [RequireComponent(typeof(BoxCollider))]
    [DisallowMultipleComponent]
    public class ArcadeDriftCar : MonoBehaviour
    {
        // The twelve. Grip and power are in m/s^2 so they read as g-forces (10 is about 1 g) and do
        // not change meaning when the mass does. Nothing below is scaled by anything hidden.

        [Header("Power")]
        [Tooltip("Forward acceleration at full throttle. [m/s^2] ~10 is brisk, ~18 is quick.")]
        public float enginePower = 14f;

        [Tooltip("Braking deceleration. [m/s^2] Doubles as reverse when held at a standstill.")]
        public float brakePower = 18f;

        [Tooltip("Top speed. [m/s] Drag is derived from this, so it is the only speed number.")]
        public float maxSpeed = 42f;

        [Header("Grip - the drift controls")]
        [Tooltip("Front axle lateral grip limit. [m/s^2] Sets how tight the car corners ON THE GRIP. " +
                 "Raise it and lower frontAxleOffset to match, or the balance below shifts.")]
        public float frontGrip = 21f;

        [Tooltip("Rear axle lateral grip limit. [m/s^2] THE drift knob - start here. Below frontGrip " +
                 "the car rotates into corners; well below it, it spins.")]
        public float rearGrip = 15f;

        [Tooltip("Rear grip multiplier while the handbrake is held. 0.25 is a violent break-away.")]
        [Range(0f, 1f)]
        public float handbrakeGripScale = 0.25f;

        [Header("Steering")]
        [Tooltip("Steer angle at full lock. [deg] More is NOT better - see the note on scrub above. " +
                 "30-34 corners tightest and is still plenty of counter-steer.")]
        public float maxSteerAngle = 32f;

        [Tooltip("How fast the steer angle chases the stick. [deg/s] These are your hands: too slow " +
                 "and a slide is uncatchable, too fast and the keyboard feels like a switch.")]
        public float steerSpeed = 340f;

        [Header("Geometry - where the axles sit")]
        [Tooltip("Front axle distance ahead of the centre of mass. [m] Further forward = more " +
                 "leverage = sharper rotation on turn-in.")]
        public float frontAxleOffset = 1.13f;

        [Tooltip("Rear axle distance behind the centre of mass. [m] Further back = the rear resists " +
                 "rotation harder = lazier, more controllable slides.")]
        public float rearAxleOffset = 1.6f;

        [Header("Feel")]
        [Tooltip("Gravity on top of Physics.gravity. [m/s^2] Raise for planted and hard landings.")]
        public float extraGravity = 12f;

        [Tooltip("How briskly the body lies onto the surface, and rights itself in the air. Tilt " +
                 "only - this can never yaw the car.")]
        public float alignSpeed = 14f;

        [Header("Debug")]
        [Tooltip("On-screen readout. NOT a handling parameter - delete it freely.")]
        public bool showReadout = true;

        // How quickly an axle is asked to bleed off sideways motion. [s] This sets the width of the
        // progressive band before grip saturates: at 0.06 s an axle takes about 1 m/s of sideways
        // slide to reach its limit. Deliberately a constant and not a parameter, to hold the count
        // at twelve - say the word and it becomes the thirteenth.
        private const float GripRelaxation = 0.06f;

        private const float ProbeMargin = 0.45f;      // How far below the body to look for ground. [m]
        private const float SteepestGround = 0.35f;   // cos of the steepest surface worth standing on.

        private Rigidbody _rb;
        private Vector3 _probeCentre;
        private float _probeDistance;
        private float _steerAngle, _steerInput, _throttle, _brake;
        private bool _handbrake, _grounded;
        private Vector3 _groundNormal = Vector3.up;
        private Vector3 _spawnPosition;
        private Quaternion _spawnRotation;

        /// <summary>Speed over ground. [km/h]</summary>
        public float SpeedKph => _rb.linearVelocity.magnitude * 3.6f;

        /// <summary>True while the probe has a drivable surface under the car.</summary>
        public bool IsGrounded => _grounded;

        /// <summary>Angle between heading and travel. [deg] Reported only - never read back.</summary>
        public float SlipAngle
        {
            get
            {
                Vector3 flat = Vector3.ProjectOnPlane(_rb.linearVelocity, Vector3.up);
                return flat.sqrMagnitude < 1f ? 0f : Vector3.SignedAngle(
                    flat, Vector3.ProjectOnPlane(transform.forward, Vector3.up), Vector3.up);
            }
        }

        private void Awake()
        {
            _rb = GetComponent<Rigidbody>();
            _rb.interpolation = RigidbodyInterpolation.Interpolate;
            _rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;

            // Unity leaves this unbounded, so a deep overlap clears in ONE step - on a track with
            // barriers that is a catapult, not a collision.
            _rb.maxDepenetrationVelocity = 3f;

            // All resistance is modelled explicitly below. Unity's damping would quietly bleed the
            // sideways velocity a slide is made of, shortening every drift for no visible reason
            // and making rearGrip a liar.
            _rb.linearDamping = 0f;
            _rb.angularDamping = 0f;

            // Probe from the middle of the body: a ray starting inside a primitive collider does not
            // report that collider, so this needs no layer mask and cannot find the car itself.
            BoxCollider box = GetComponent<BoxCollider>();
            _probeCentre = box.center;
            _probeDistance = box.size.y * 0.5f * transform.lossyScale.y + ProbeMargin;

            _spawnPosition = transform.position;
            _spawnRotation = transform.rotation;
        }

        private void Update() => ReadInput();

        private void FixedUpdate()
        {
            float dt = Time.fixedDeltaTime;
            ProbeGround();

            // Steering only points the front axle. Nothing here rotates the car - the car rotates
            // because a pointed axle then makes force somewhere off the centre of mass.
            _steerAngle = Mathf.MoveTowards(_steerAngle, _steerInput * maxSteerAngle, steerSpeed * dt);

            if (_grounded)
            {
                Vector3 steered = Quaternion.AngleAxis(_steerAngle, _groundNormal) * transform.forward;
                ApplyAxle(frontAxleOffset, steered, frontGrip, dt);
                ApplyAxle(-rearAxleOffset, transform.forward, RearGripNow, dt);
                ApplyDrive();
            }

            Align();
            _rb.AddForce(Vector3.down * extraGravity, ForceMode.Acceleration);
        }

        /// <summary>Rear grip right now. Reducing this is all the handbrake does.</summary>
        private float RearGripNow => _handbrake ? rearGrip * handbrakeGripScale : rearGrip;

        /// <summary>
        /// One axle. This is the whole handling model; the rest of the file is plumbing.
        /// </summary>
        /// <param name="localZ">Axle position along body Z, relative to the centre of mass. [m]</param>
        /// <param name="heading">Where this axle points - steered at the front, body forward at the rear.</param>
        /// <param name="gripLimit">Most lateral acceleration this axle may produce. [m/s^2]</param>
        private void ApplyAxle(float localZ, Vector3 heading, float gripLimit, float dt)
        {
            Vector3 point = transform.TransformPoint(new Vector3(0f, 0f, localZ));

            // Work in the ground plane, so a slope or a bank cannot tip part of the grip force into
            // the vertical and quietly change how much of it is left.
            Vector3 forward = Vector3.ProjectOnPlane(heading, _groundNormal).normalized;
            Vector3 right = Vector3.Cross(_groundNormal, forward);

            float lateralSpeed = Vector3.Dot(_rb.GetPointVelocity(point), right);
            float axleMass = _rb.mass * 0.5f;             // Each axle carries half the car.

            // Bleed the slide off over GripRelaxation rather than killing it in a single step. With
            // dt here, an axle reached its limit at 0.09 m/s of sideways motion, so grip behaved as
            // a switch - full bite, then nothing, with no band in between for the car to visibly
            // load up in. Flooring it at dt keeps this stable at any timestep.
            float relaxation = Mathf.Max(GripRelaxation, dt);
            float required = -lateralSpeed * axleMass / relaxation;
            float limit = gripLimit * axleMass;

            // THE CLAMP IS THE WHOLE GAME: below it the axle holds and the car corners; at it the
            // axle is sliding and the car drifts.
            _rb.AddForceAtPosition(right * Mathf.Clamp(required, -limit, limit), point, ForceMode.Force);
        }

        /// <summary>
        /// Throttle, brake, and the drag that sets top speed. Drag is solved from maxSpeed rather
        /// than authored separately, so top speed stays one number. It acts along the heading ONLY -
        /// drag against the full velocity would eat the sideways motion a slide is made of.
        /// </summary>
        private void ApplyDrive()
        {
            Vector3 forward = Vector3.ProjectOnPlane(transform.forward, _groundNormal).normalized;
            float forwardSpeed = Vector3.Dot(_rb.linearVelocity, forward);

            float fraction = forwardSpeed / Mathf.Max(maxSpeed, 0.01f);
            float accel = _throttle * enginePower - enginePower * fraction * Mathf.Abs(fraction);

            // The brake opposes travel and keeps pushing once stopped, which is reverse. The floor
            // stops it accelerating backwards forever.
            if (forwardSpeed > -maxSpeed * 0.25f) accel -= _brake * brakePower;

            _rb.AddForce(forward * accel, ForceMode.Acceleration);
        }

        /// <summary>
        /// Lie the body onto whatever it stands on, or right it in the air.
        ///
        /// The torque axis is Cross(up, target), which by construction has no component along the
        /// car's own up axis - so this can roll and pitch the car but can NEVER yaw it. Deliberate:
        /// yaw belongs to the axles alone, and if this could nudge it, rearGrip would stop being the
        /// only thing deciding whether the car spins.
        /// </summary>
        private void Align()
        {
            Vector3 target = _grounded ? _groundNormal : Vector3.up;
            Vector3 axis = Vector3.Cross(transform.up, target);

            // Damp only the tilting part of the spin - again leaving yaw alone - near critically, so
            // the body settles instead of wallowing.
            Vector3 spin = _rb.angularVelocity - transform.up * Vector3.Dot(_rb.angularVelocity, transform.up);
            _rb.AddTorque(axis * alignSpeed - spin * (2f * Mathf.Sqrt(alignSpeed)), ForceMode.Acceleration);
        }

        private void ProbeGround()
        {
            _grounded = false;
            _groundNormal = Vector3.up;

            if (!Physics.Raycast(transform.TransformPoint(_probeCentre), -transform.up,
                                 out RaycastHit hit, _probeDistance, ~0, QueryTriggerInteraction.Ignore))
                return;

            // Refuse anything too steep to stand on. A wall's normal is horizontal, and accepting
            // one would have Align() calmly lay the car over onto its side.
            if (Vector3.Dot(hit.normal, Vector3.up) < SteepestGround) return;

            _grounded = true;
            _groundNormal = hit.normal;
        }

        /// <summary>Devices polled directly, not via an InputActionAsset - one less thing to wire
        /// for a lab. Swap in an action asset once bindings start to matter.</summary>
        private void ReadInput()
        {
            float steer = 0f, throttle = 0f, brake = 0f;
            bool hand = false;

            Keyboard k = Keyboard.current;
            if (k != null)
            {
                if (k.aKey.isPressed || k.leftArrowKey.isPressed) steer -= 1f;
                if (k.dKey.isPressed || k.rightArrowKey.isPressed) steer += 1f;
                if (k.wKey.isPressed || k.upArrowKey.isPressed) throttle = 1f;
                if (k.sKey.isPressed || k.downArrowKey.isPressed) brake = 1f;
                hand = k.spaceKey.isPressed;
                if (k.rKey.wasPressedThisFrame) Respawn();
            }

            Gamepad g = Gamepad.current;
            if (g != null)
            {
                float stick = g.leftStick.x.ReadValue();
                if (Mathf.Abs(stick) > Mathf.Abs(steer)) steer = stick;
                throttle = Mathf.Max(throttle, g.rightTrigger.ReadValue());
                brake = Mathf.Max(brake, g.leftTrigger.ReadValue());
                hand |= g.buttonEast.isPressed;
                if (g.buttonNorth.wasPressedThisFrame) Respawn();
            }

            _steerInput = Mathf.Clamp(steer, -1f, 1f);
            _throttle = throttle;
            _brake = brake;
            _handbrake = hand;
        }

        /// <summary>Back to the spawn pose, stationary. You will want this constantly.</summary>
        public void Respawn()
        {
            _rb.linearVelocity = Vector3.zero;
            _rb.angularVelocity = Vector3.zero;
            _rb.position = _spawnPosition;
            _rb.rotation = _spawnRotation;
            transform.SetPositionAndRotation(_spawnPosition, _spawnRotation);
            _steerAngle = 0f;
        }

        private void OnGUI()
        {
            if (!showReadout) return;
            GUI.Label(new Rect(12f, 12f, 360f, 90f),
                      $"{SpeedKph:F0} km/h\nslip {SlipAngle:F0} deg\n"
                      + (_grounded ? "grounded" : "AIRBORNE")
                      + "\nWASD  space=handbrake  R=respawn");
        }

        /// <summary>The two points the entire model runs on.</summary>
        private void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.cyan;
            Gizmos.DrawWireSphere(transform.TransformPoint(new Vector3(0f, 0f, frontAxleOffset)), 0.25f);
            Gizmos.color = Color.magenta;
            Gizmos.DrawWireSphere(transform.TransformPoint(new Vector3(0f, 0f, -rearAxleOffset)), 0.25f);
        }
    }
}
