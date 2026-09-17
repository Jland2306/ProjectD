using Touge.Core;
using Touge.Vehicle.Data;
using Touge.Vehicle.Drivetrain;
using Touge.Vehicle.Physics;
using UnityEngine;

namespace Touge.Vehicle
{
    /// <summary>
    /// The vehicle solver. Owns one Rigidbody, four <see cref="Wheel"/>s, a <see cref="Powertrain"/>
    /// and a <see cref="SteeringController"/>, and steps them in a fixed order once per FixedUpdate.
    ///
    /// DESIGN NOTES
    ///
    /// No WheelCollider. Each corner is a spring-damper raycast plus an explicit tyre model, so every
    /// force acting on the car is visible and tunable in this file and the ones it calls.
    ///
    /// Nothing about the car's behaviour is faked. Weight transfer is not applied as a correction -
    /// it emerges because suspension forces act at the contact patches, below the centre of mass, and
    /// tyre grip is sub-linear in load. Oversteer is not scripted - it happens when the rear tyres are
    /// asked for more than their share of the friction circle.
    ///
    /// The solver reads <see cref="CarSpec"/> every step rather than caching it, so the whole car can
    /// be retuned live in Play Mode.
    ///
    /// DETERMINISM: all state lives in this component and its owned objects, all integration happens
    /// in FixedUpdate at a fixed step, and input arrives through <see cref="IVehicleInput"/> with
    /// button presses latched rather than sampled per frame. Given the same spec and the same input
    /// sequence the car follows the same path, which is what the ghost/replay system will need.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    [DisallowMultipleComponent]
    public class CarController : MonoBehaviour
    {
        [Header("Configuration")]
        [Tooltip("All handling parameters. Editable live in Play Mode.")]
        [SerializeField] private CarSpec spec;

        [Header("Visuals")]
        [Tooltip("Wheel meshes in the order FL, FR, RL, RR. May be left empty - physics does not need them.")]
        [SerializeField] private Transform[] wheelVisuals = new Transform[4];

        [Header("Drift scoring")]
        [SerializeField] private DriftScorer driftScorer = new DriftScorer();

        [Header("Debug")]
        [Tooltip("Draw suspension rays, tyre force vectors, centre of mass and velocity in the Scene view.")]
        [SerializeField] private bool drawGizmos = true;

        [Tooltip("Scene-view length of a tyre force arrow at 10 kN. [m]")]
        [SerializeField] private float gizmoForceScale = 0.0006f;

        // Wheel array indices. Fixed order so telemetry and visuals can rely on it.
        private const int FrontLeft = 0;
        private const int FrontRight = 1;
        private const int RearLeft = 2;
        private const int RearRight = 3;

        private Rigidbody _rb;
        private Wheel[] _wheels;
        private Powertrain _powertrain;
        private SteeringController _steering;
        private TireCurve _tireCurve;
        private VehicleTelemetry _telemetry;
        private IVehicleInput _input;
        private Vector3 _previousVelocity;

        /// <summary>The active tuning asset.</summary>
        public CarSpec Spec => spec;

        /// <summary>Live telemetry snapshot, refreshed each physics step.</summary>
        public VehicleTelemetry Telemetry => _telemetry;

        /// <summary>The four wheels, ordered FL, FR, RL, RR.</summary>
        public Wheel[] Wheels => _wheels;

        /// <summary>Powertrain state, for UI and future AI shift logic.</summary>
        public Powertrain Powertrain => _powertrain;

        /// <summary>The rigidbody being driven.</summary>
        public Rigidbody Body => _rb;

        private void Awake()
        {
            _rb = GetComponent<Rigidbody>();
            _rb.interpolation = RigidbodyInterpolation.Interpolate;
            _rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;

            _wheels = new[]
            {
                new Wheel(true, -1),    // FL
                new Wheel(true, 1),     // FR
                new Wheel(false, -1),   // RL
                new Wheel(false, 1)     // RR
            };

            _powertrain = new Powertrain();
            _steering = new SteeringController();
            _tireCurve = new TireCurve();
            _telemetry = new VehicleTelemetry { Wheels = _wheels };

            if (spec != null)
            {
                ApplyChassis();
                _powertrain.Initialize(spec);
            }

            // Any MonoBehaviour on this object implementing IVehicleInput becomes the driver. An AI
            // or replay driver can replace it at any time via SetInput.
            //
            // Scanned explicitly rather than via GetComponent<IVehicleInput>(): asking for an
            // interface can hand back Unity's fake null, which would then NOT compare equal to null
            // through an interface reference and would fault later instead of failing here.
            if (_input == null)
            {
                foreach (MonoBehaviour behaviour in GetComponents<MonoBehaviour>())
                {
                    if (behaviour is not IVehicleInput candidate) continue;
                    _input = candidate;
                    break;
                }
            }
        }

        private void OnEnable()
        {
            if (spec == null)
                Debug.LogError($"{nameof(CarController)} on '{name}' has no CarSpec assigned.", this);
        }

        /// <summary>Swap the driver. Works with the player, an AI, or a replay playback head.</summary>
        public void SetInput(IVehicleInput input) => _input = input;

        private void FixedUpdate()
        {
            if (spec == null || _input == null) return;

            float dt = Time.fixedDeltaTime;

            // Re-solve the tyre curve only if its shape was edited, and push chassis properties onto
            // the rigidbody - both exist to make Inspector edits take effect without re-entering Play.
            _tireCurve.Refresh(spec.tires);
            ApplyChassis();

            Vector3 velocity = _rb.linearVelocity;
            float speed = velocity.magnitude;
            float speedKph = TougeMath.MpsToKph(speed);
            float driftAngle = TougeMath.HorizontalSlipAngleDeg(transform.forward, velocity);

            // 1. Suspension. Casts for ground and evaluates each spring and damper, but does not yet
            //    apply anything - the anti-roll bars still need to adjust the loads.
            foreach (Wheel wheel in _wheels) wheel.UpdateSuspension(_rb, spec, dt);

            // 2. Anti-roll bars redistribute load across each axle.
            AntiRollBar.Apply(_wheels[FrontLeft], _wheels[FrontRight], spec.suspension.front.antiRollStiffness);
            AntiRollBar.Apply(_wheels[RearLeft], _wheels[RearRight], spec.suspension.rear.antiRollStiffness);

            // 3. Now the loads are final, push them into the body. This is the step that creates all
            //    roll and pitch, and therefore all weight transfer.
            foreach (Wheel wheel in _wheels) wheel.ApplySuspensionForce(_rb);

            // 4. Steering demand -> per-wheel steer angles.
            _steering.Step(spec, _input.Steer, speedKph, driftAngle, dt);
            _steering.ApplyToWheels(_wheels, spec);

            // 5. Brake torques, which the wheel spin integration consumes in step 7.
            ApplyBrakeTorques(speed);

            // 6. Powertrain, then split its output across the driven axles.
            _powertrain.Step(spec, _input, AverageDrivenWheelOmega(), DrivenWheelInertia(), dt);
            DistributeDriveTorque(dt);

            // 7. Tire forces and wheel rotation. Uses the loads from step 2 and the torques from 5-6.
            foreach (Wheel wheel in _wheels) wheel.UpdateTire(_rb, spec, _tireCurve, dt);

            // 8. Aerodynamics and any enabled stability cheat.
            ApplyAerodynamics(velocity, speed);
            ApplyStabilityAssist();

            // 9. Bookkeeping.
            int groundedCount = 0;
            foreach (Wheel wheel in _wheels) if (wheel.IsGrounded) groundedCount++;
            driftScorer.Step(driftAngle, speed, groundedCount > 0, dt);
            UpdateTelemetry(velocity, speed, speedKph, driftAngle, groundedCount, dt);

            // 10. Release latched button presses now that the physics step has seen them.
            _input.ConsumeEdges();

            _previousVelocity = velocity;
        }

        /// <summary>
        /// Push mass, centre of mass and inertia onto the rigidbody.
        ///
        /// The inertia tensor is set explicitly rather than left to Unity. Unity derives it from the
        /// collider shape, which for a box-shaped car body gives a yaw inertia far higher than a real
        /// car's - the result feels reluctant to rotate and, worse, reluctant to STOP rotating, which
        /// makes a slide impossible to catch.
        /// </summary>
        private void ApplyChassis()
        {
            ChassisSpec chassis = spec.chassis;

            if (!Mathf.Approximately(_rb.mass, chassis.massKg))
                _rb.mass = chassis.massKg;

            // Assigning centreOfMass/inertiaTensor also disables Unity's automatic computation.
            if (_rb.centerOfMass != chassis.centerOfMassOffset)
                _rb.centerOfMass = chassis.centerOfMassOffset;

            if (chassis.overrideInertiaTensor && _rb.inertiaTensor != chassis.inertiaTensor)
                _rb.inertiaTensor = chassis.inertiaTensor;

            // Linear damping stays at zero: drag is modelled explicitly as a v^2 aerodynamic force.
            // Unity's linear damping is a v^1 term and would quietly distort the top-speed curve.
            _rb.linearDamping = 0f;
            _rb.angularDamping = chassis.angularDamping;
        }

        /// <summary>
        /// Work out each wheel's brake torque from the pedal, the bias and the handbrake.
        ///
        /// Bias is expressed so that 0.5 leaves both axles at their configured maximum; moving it
        /// forward scales the front up and the rear down proportionally.
        /// </summary>
        private void ApplyBrakeTorques(float speed)
        {
            AssistSpec assists = spec.assists;
            float brakeInput = Mathf.Clamp01(_input.Brake);
            float handbrakeInput = Mathf.Clamp01(_input.Handbrake);

            foreach (Wheel wheel in _wheels)
            {
                AxleSpec axle = spec.GetAxle(wheel.IsFront);

                float biasFactor = wheel.IsFront
                    ? 2f * assists.brakeBiasFront
                    : 2f * (1f - assists.brakeBiasFront);

                float serviceBrake = brakeInput * axle.maxBrakeTorqueNm * biasFactor;

                // ABS bleeds pressure in proportion to how far past the slip threshold the wheel has
                // gone, rather than dumping it. A flat cut makes the wheel snap between locked and
                // free and the car judders; releasing proportionally holds the wheel near the
                // threshold, which is where the tyre makes its peak force AND keeps enough lateral
                // grip to still steer.
                //
                // Off by default: locking the rear axle is a drift technique, not a fault. Turn it on
                // for a car that should just stop and turn in.
                if (assists.absEnabled && speed > assists.absMinSpeed)
                {
                    float threshold = Mathf.Max(assists.absSlipThreshold, TougeMath.Epsilon);
                    float excess = Mathf.Abs(wheel.SlipRatio) - threshold;
                    if (excess > 0f)
                        serviceBrake *= 1f - Mathf.Clamp01(excess / threshold);
                }

                // The handbrake bypasses the bias entirely and acts only on the axle configured for
                // it. Enough torque to fully lock the wheel is the whole point: a locked tyre has a
                // slip ratio of -1, and at that slip the combined-slip solve leaves it almost no
                // lateral force, so the rear simply steps out.
                float handbrake = handbrakeInput * axle.handbrakeTorqueNm;

                wheel.BrakeTorque = serviceBrake + handbrake;
            }
        }

        /// <summary>Mean angular velocity of the driven wheels, which the clutch couples the engine to.</summary>
        private float AverageDrivenWheelOmega()
        {
            DifferentialSpec diff = spec.differential;
            float total = 0f;
            int count = 0;

            foreach (Wheel wheel in _wheels)
            {
                if (!diff.IsAxleDriven(wheel.IsFront)) continue;
                total += wheel.AngularVelocity;
                count++;
            }

            return count > 0 ? total / count : 0f;
        }

        /// <summary>
        /// Combined rotational inertia of every driven wheel. The clutch needs this to work out how
        /// much torque actually synchronises the engine to the driveline - see the reduced-inertia
        /// note in <see cref="Powertrain.Step"/>.
        /// </summary>
        private float DrivenWheelInertia()
        {
            DifferentialSpec diff = spec.differential;
            float total = 0f;

            foreach (Wheel wheel in _wheels)
            {
                if (!diff.IsAxleDriven(wheel.IsFront)) continue;
                total += spec.GetAxle(wheel.IsFront).WheelInertia;
            }

            return total;
        }

        /// <summary>Route driveshaft torque to the driven axles and through their differentials.</summary>
        private void DistributeDriveTorque(float dt)
        {
            DifferentialSpec diff = spec.differential;
            float output = _powertrain.OutputTorqueNm;

            // Traction control cuts drive torque when a driven wheel spins up. Simplified compared to
            // a real system (which cuts spark or fuel), and off by default - it directly suppresses
            // power oversteer, which is the opposite of what this car is for.
            if (spec.assists.tractionControlEnabled)
            {
                foreach (Wheel wheel in _wheels)
                {
                    if (!diff.IsAxleDriven(wheel.IsFront)) continue;
                    if (wheel.SlipRatio > spec.assists.tractionControlSlipThreshold)
                    {
                        output *= 0.25f;
                        break;
                    }
                }
            }

            ApplyAxleTorque(true, _wheels[FrontLeft], _wheels[FrontRight], output, diff, dt);
            ApplyAxleTorque(false, _wheels[RearLeft], _wheels[RearRight], output, diff, dt);
        }

        private void ApplyAxleTorque(bool isFront, Wheel left, Wheel right, float driveshaftTorque,
                                     DifferentialSpec diff, float dt)
        {
            if (!diff.IsAxleDriven(isFront))
            {
                left.DriveTorque = 0f;
                right.DriveTorque = 0f;
                return;
            }

            float axleTorque = driveshaftTorque * diff.GetAxleTorqueShare(isFront);
            float wheelInertia = spec.GetAxle(isFront).WheelInertia;

            Differential.Distribute(left, right, axleTorque, diff, isFront, wheelInertia, dt);
        }

        /// <summary>
        /// Aerodynamic drag and downforce, both proportional to v^2.
        ///     F = k * v^2
        /// Downforce is applied at the axle positions so a front/rear split produces a real pitching
        /// moment rather than just pressing the car straight down.
        /// </summary>
        private void ApplyAerodynamics(Vector3 velocity, float speed)
        {
            if (speed < TougeMath.Epsilon) return;

            ChassisSpec chassis = spec.chassis;
            float dynamicPressure = speed * speed;

            _rb.AddForce(-velocity / speed * (chassis.aeroDragCoefficient * dynamicPressure), ForceMode.Force);

            if (chassis.downforceFront > 0f)
            {
                Vector3 frontPoint = transform.TransformPoint(new Vector3(0f, 0f, spec.frontAxle.positionZ));
                _rb.AddForceAtPosition(-transform.up * (chassis.downforceFront * dynamicPressure),
                                       frontPoint, ForceMode.Force);
            }

            if (chassis.downforceRear > 0f)
            {
                Vector3 rearPoint = transform.TransformPoint(new Vector3(0f, 0f, spec.rearAxle.positionZ));
                _rb.AddForceAtPosition(-transform.up * (chassis.downforceRear * dynamicPressure),
                                       rearPoint, ForceMode.Force);
            }
        }

        /// <summary>
        /// Artificial yaw damping. A deliberate cheat, disabled by default - it fights rotation the
        /// tyres legitimately produced, so any non-zero value is trading drift feel for stability.
        /// </summary>
        private void ApplyStabilityAssist()
        {
            float damping = spec.assists.stabilityYawDamping;
            if (damping <= TougeMath.Epsilon) return;

            float yawRate = Vector3.Dot(_rb.angularVelocity, transform.up);
            float inertiaY = spec.chassis.inertiaTensor.y;
            _rb.AddTorque(-transform.up * (yawRate * damping * inertiaY), ForceMode.Force);
        }

        private void UpdateTelemetry(Vector3 velocity, float speed, float speedKph,
                                     float driftAngle, int groundedCount, float dt)
        {
            VehicleTelemetry t = _telemetry;

            t.SpeedMps = speed;
            t.SpeedKph = speedKph;
            t.ForwardSpeed = Vector3.Dot(velocity, transform.forward);

            // Acceleration from the change in velocity across this step, resolved into body axes.
            Vector3 acceleration = (velocity - _previousVelocity) / Mathf.Max(dt, TougeMath.Epsilon);
            t.LongitudinalG = Vector3.Dot(acceleration, transform.forward) / 9.81f;
            t.LateralG = Vector3.Dot(acceleration, transform.right) / 9.81f;
            t.YawRateDegPerSec = Vector3.Dot(_rb.angularVelocity, transform.up) * Mathf.Rad2Deg;

            t.EngineRpm = _powertrain.EngineRpm;
            t.EngineTorqueNm = _powertrain.EngineTorqueNm;
            t.ClutchTorqueNm = _powertrain.ClutchTorqueNm;
            t.ClutchSlipping = _powertrain.ClutchSlipping;
            t.Gear = _powertrain.Gear;
            t.TransmissionMode = _powertrain.Mode;
            t.RevLimiterActive = _powertrain.RevLimiterActive;

            t.Throttle = _input.Throttle;
            t.Brake = _input.Brake;
            t.Steer = _input.Steer;
            t.Handbrake = _input.Handbrake;
            t.Clutch = _input.Clutch;
            t.SteerAngleDeg = _steering.SteerAngleDeg;
            t.CounterSteerAssistDeg = _steering.CounterSteerAssistDeg;

            t.DriftAngleDeg = driftAngle;
            t.DriftDuration = driftScorer.Duration;
            t.CurrentDriftScore = driftScorer.CurrentScore;
            t.TotalDriftScore = driftScorer.TotalScore;
            t.IsDrifting = driftScorer.IsDrifting;

            float totalLoad = 0f;
            foreach (Wheel wheel in _wheels) totalLoad += wheel.Load;
            t.TotalLoadN = totalLoad;
            t.GroundedWheelCount = groundedCount;
        }

        /// <summary>
        /// Place the visual wheel meshes.
        ///
        /// Runs in LateUpdate, and recomputes the strut mount from the CURRENT (interpolated) body
        /// transform rather than reusing the position cached during FixedUpdate. Reusing the cached
        /// one would make the wheels lag the body by up to a frame and visibly swim.
        /// </summary>
        private void LateUpdate()
        {
            if (spec == null || wheelVisuals == null) return;

            for (int i = 0; i < _wheels.Length && i < wheelVisuals.Length; i++)
            {
                Transform visual = wheelVisuals[i];
                if (visual == null) continue;

                Wheel wheel = _wheels[i];
                AxleSpec axle = spec.GetAxle(wheel.IsFront);
                AxleSuspensionSpec sus = spec.suspension.ForAxle(wheel.IsFront);

                Vector3 mount = transform.TransformPoint(axle.GetMountPoint(wheel.Side));
                Vector3 position = mount - transform.up * (sus.restLength - wheel.Compression);

                // Steer about the strut axis, then spin about the wheel's own lateral axis.
                Quaternion rotation = transform.rotation
                                      * Quaternion.Euler(0f, wheel.SteerAngleDeg, 0f)
                                      * Quaternion.Euler(wheel.SpinAngleDeg, 0f, 0f);

                visual.SetPositionAndRotation(position, rotation);
            }
        }

        /// <summary>
        /// Teleport the car to a pose and zero all motion. Used by respawns and, later, by race
        /// starts and free-roam fast travel.
        /// </summary>
        public void ResetTo(Vector3 position, Quaternion rotation)
        {
            _rb.linearVelocity = Vector3.zero;
            _rb.angularVelocity = Vector3.zero;
            _rb.position = position;
            _rb.rotation = rotation;
            transform.SetPositionAndRotation(position, rotation);

            foreach (Wheel wheel in _wheels) wheel.Reset();
            _steering.Reset();
            driftScorer.Bank();
            if (spec != null) _powertrain.Initialize(spec);
            _previousVelocity = Vector3.zero;
        }

        private void OnDrawGizmos()
        {
            if (!drawGizmos || _wheels == null || spec == null || _rb == null) return;

            // Centre of mass.
            Gizmos.color = Color.yellow;
            Vector3 com = transform.TransformPoint(spec.chassis.centerOfMassOffset);
            Gizmos.DrawSphere(com, 0.07f);

            // Velocity vector, scaled down so it stays readable at speed.
            Gizmos.color = Color.cyan;
            Gizmos.DrawLine(com, com + _rb.linearVelocity * 0.25f);

            foreach (Wheel wheel in _wheels)
            {
                AxleSpec axle = spec.GetAxle(wheel.IsFront);
                AxleSuspensionSpec sus = spec.suspension.ForAxle(wheel.IsFront);
                Vector3 mount = transform.TransformPoint(axle.GetMountPoint(wheel.Side));

                // Strut travel: green while in contact, red when the wheel is in the air.
                Gizmos.color = wheel.IsGrounded ? Color.green : Color.red;
                Gizmos.DrawLine(mount, mount - transform.up * (sus.restLength + axle.wheelRadius));

                if (!wheel.IsGrounded) continue;

                // Wheel centre.
                Gizmos.color = Color.white;
                Gizmos.DrawWireSphere(wheel.WheelCenter, axle.wheelRadius);

                // Tyre forces at the contact patch: blue = longitudinal, magenta = lateral.
                Gizmos.color = Color.blue;
                Gizmos.DrawLine(wheel.ContactPoint,
                                wheel.ContactPoint + wheel.ForwardDir * (wheel.LongitudinalForce * gizmoForceScale));

                Gizmos.color = Color.magenta;
                Gizmos.DrawLine(wheel.ContactPoint,
                                wheel.ContactPoint + wheel.RightDir * (wheel.LateralForce * gizmoForceScale));

                // Vertical load, drawn upward.
                Gizmos.color = Color.grey;
                Gizmos.DrawLine(wheel.ContactPoint,
                                wheel.ContactPoint + transform.up * (wheel.Load * gizmoForceScale));
            }
        }
    }
}
