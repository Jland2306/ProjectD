using Touge.Core;
using Touge.Vehicle.Data;
using UnityEngine;

namespace Touge.Vehicle.Physics
{
    /// <summary>
    /// One corner of the car: a spring-damper strut, a rotating wheel, and a tyre contact patch.
    ///
    /// Deliberately a plain C# class rather than a MonoBehaviour. The wheels are owned and stepped by
    /// <see cref="CarController"/> in a fixed order, which keeps the whole vehicle solve inside a
    /// single FixedUpdate with no dependence on Unity's component execution order - a prerequisite
    /// for the deterministic replay/ghost system later on.
    ///
    /// Sign conventions (body-local):
    ///   +X = right, +Y = up, +Z = forward.
    ///   Positive slip ratio  = wheel surface outrunning the road (driving).
    ///   Positive slip angle  = contact patch sliding toward the right of the wheel heading.
    ///   Positive drive torque spins the wheel in the forward direction.
    /// </summary>
    public class Wheel
    {
        /// <summary>Which axle this corner belongs to.</summary>
        public readonly bool IsFront;

        /// <summary>-1 for the left wheel, +1 for the right.</summary>
        public readonly int Side;

        /// <summary>Human-readable corner name for telemetry ("FL", "FR", "RL", "RR").</summary>
        public readonly string Label;

        // ---- Rotational state -------------------------------------------------------------------

        /// <summary>Wheel spin rate. [rad/s] Positive = rolling forward.</summary>
        public float AngularVelocity;

        /// <summary>Current steer angle of this wheel. [degrees]</summary>
        public float SteerAngleDeg;

        /// <summary>Accumulated spin for the visual mesh. [degrees]</summary>
        public float SpinAngleDeg;

        // ---- Suspension state -------------------------------------------------------------------

        /// <summary>How far the spring is compressed from its free length. [m]</summary>
        public float Compression;

        /// <summary>Compression as a fraction of usable travel, 0 = full droop, 1 = on the bump stop.</summary>
        public float CompressionRatio;

        /// <summary>Vertical load carried by this tyre, including the anti-roll bar contribution. [N]</summary>
        public float Load;

        /// <summary>True when the ground cast found a surface within reach.</summary>
        public bool IsGrounded;

        /// <summary>World-space tyre contact patch.</summary>
        public Vector3 ContactPoint;

        /// <summary>World-space surface normal at the contact patch.</summary>
        public Vector3 ContactNormal;

        /// <summary>World-space wheel centre, used to place the visual mesh.</summary>
        public Vector3 WheelCenter;

        /// <summary>World-space strut axis (the body's up vector).</summary>
        public Vector3 SuspensionUp;

        // ---- Tyre state -------------------------------------------------------------------------

        /// <summary>Wheel heading projected into the contact plane.</summary>
        public Vector3 ForwardDir;

        /// <summary>Lateral axis in the contact plane, pointing right of <see cref="ForwardDir"/>.</summary>
        public Vector3 RightDir;

        /// <summary>Longitudinal slip, dimensionless. 0 = rolling cleanly, +ve = wheelspin, -ve = lockup.</summary>
        public float SlipRatio;

        /// <summary>Lateral slip angle. [degrees]</summary>
        public float SlipAngleDeg;

        /// <summary>Force along <see cref="ForwardDir"/>. [N]</summary>
        public float LongitudinalForce;

        /// <summary>Force along <see cref="RightDir"/>. [N]</summary>
        public float LateralForce;

        /// <summary>Fraction of this tyre's friction circle currently in use, 0-1. 1 = at the limit.</summary>
        public float GripUsage;

        /// <summary>Engine torque delivered to this wheel by the differential this step. [N*m]</summary>
        public float DriveTorque;

        /// <summary>Total brake torque applied to this wheel this step, service + handbrake. [N*m]</summary>
        public float BrakeTorque;

        // ---- Internals --------------------------------------------------------------------------

        /// <summary>
        /// Lagged tan(slip angle), carrying the tyre relaxation length. See <see cref="UpdateTire"/>.
        /// </summary>
        private float _laggedTanSlipAngle;

        /// <summary>Suspension force before the anti-roll bar is folded in. [N]</summary>
        private float _springDamperForce;

        public Wheel(bool isFront, int side)
        {
            IsFront = isFront;
            Side = side;
            Label = (isFront ? "F" : "R") + (side < 0 ? "L" : "R");
        }

        /// <summary>Axle geometry for this corner.</summary>
        public AxleSpec Axle(CarSpec spec) => spec.GetAxle(IsFront);

        /// <summary>Spring/damper settings for this corner.</summary>
        public AxleSuspensionSpec Suspension(CarSpec spec) => spec.suspension.ForAxle(IsFront);

        /// <summary>
        /// Cast for the ground and evaluate the spring and damper.
        ///
        ///     F = k * x  +  c * v
        ///
        /// with x = compression [m] and v = compression rate [m/s]. The result is stored rather than
        /// applied, because the anti-roll bar still needs to adjust it before it reaches the body.
        /// </summary>
        public void UpdateSuspension(Rigidbody rb, CarSpec spec, float dt)
        {
            AxleSpec axle = Axle(spec);
            AxleSuspensionSpec sus = Suspension(spec);
            SuspensionSpec shared = spec.suspension;

            Transform body = rb.transform;
            SuspensionUp = body.up;
            Vector3 mount = body.TransformPoint(axle.GetMountPoint(Side));

            // Distance from the strut mount to the road when the spring is at its free length.
            float freeLength = sus.restLength + axle.wheelRadius;
            float castLength = freeLength + shared.castMargin;

            bool hit = CastGround(mount, -SuspensionUp, SuspensionUp, castLength, shared, out RaycastHit hitInfo);

            if (!hit)
            {
                // Airborne: the strut hangs at full droop and the tyre carries no load.
                IsGrounded = false;
                Compression = 0f;
                CompressionRatio = 0f;
                Load = 0f;
                _springDamperForce = 0f;
                ContactNormal = SuspensionUp;
                ContactPoint = mount - SuspensionUp * freeLength;
                WheelCenter = mount - SuspensionUp * sus.restLength;
                return;
            }

            // Measure the ground distance ALONG THE STRUT AXIS rather than using hit.distance, so the
            // result is correct for a sphere cast and on sloped ground alike.
            float groundDistance = Vector3.Dot(mount - hitInfo.point, SuspensionUp);
            float compression = freeLength - groundDistance;

            if (compression <= 0f)
            {
                // Surface found, but beyond the spring's reach - still effectively airborne.
                IsGrounded = false;
                Compression = 0f;
                CompressionRatio = 0f;
                Load = 0f;
                _springDamperForce = 0f;
                ContactNormal = hitInfo.normal;
                ContactPoint = hitInfo.point;
                WheelCenter = mount - SuspensionUp * sus.restLength;
                return;
            }

            IsGrounded = true;
            ContactPoint = hitInfo.point;
            ContactNormal = hitInfo.normal;
            Compression = compression;
            CompressionRatio = Mathf.Clamp01(compression / Mathf.Max(sus.maxTravel, TougeMath.Epsilon));

            // Spring: linear up to max travel, then the (very stiff) bump stop takes over. Without
            // the bump stop a hard landing drives the chassis straight through the road.
            float travel = Mathf.Min(compression, sus.maxTravel);
            float overTravel = Mathf.Max(0f, compression - sus.maxTravel);
            float springForce = sus.springStiffness * travel + sus.bumpStopStiffness * overTravel;

            // Damper: velocity of the mount point along the strut axis. Positive = compressing.
            float mountVelocity = Vector3.Dot(rb.GetPointVelocity(mount), SuspensionUp);
            float compressionRate = -mountVelocity;
            float damperRate = compressionRate > 0f ? sus.bumpDamper : sus.reboundDamper;
            float damperForce = damperRate * compressionRate;

            // A spring can push the body up but never pull it down toward the road.
            _springDamperForce = Mathf.Max(0f, springForce + damperForce);
            Load = _springDamperForce;

            WheelCenter = mount - SuspensionUp * (sus.restLength - compression);
        }

        /// <summary>
        /// Adjust this corner's load by the anti-roll bar contribution. Called after both wheels on
        /// the axle have run <see cref="UpdateSuspension"/>.
        /// </summary>
        public void ApplyAntiRoll(float antiRollForce)
        {
            // A tyre cannot pull the car down, so load floors at zero - which is exactly how a stiff
            // bar lifts the inside wheel off the road in a hard corner.
            Load = Mathf.Max(0f, _springDamperForce + antiRollForce);
        }

        /// <summary>
        /// Push this corner's suspension force into the rigidbody.
        ///
        /// The force acts along the strut axis, not the ground normal. Using the strut axis keeps the
        /// car stable on cambered and steep surfaces; the ground normal belongs to the tyre forces,
        /// which are computed in the contact plane.
        /// </summary>
        public void ApplySuspensionForce(Rigidbody rb)
        {
            if (!IsGrounded || Load <= 0f) return;

            // Applied at the contact patch rather than the strut mount: the lever arm from the road
            // up to the centre of mass is what produces roll and pitch moments, and therefore all
            // lateral and longitudinal weight transfer.
            rb.AddForceAtPosition(SuspensionUp * Load, ContactPoint, ForceMode.Force);
        }

        /// <summary>
        /// Compute slip, evaluate the tyre model, apply the resulting force to the body, and
        /// integrate the wheel's own rotation.
        ///
        /// Order matters: slip is measured from the CURRENT wheel speed, the force follows from that
        /// slip, and only then is the wheel speed advanced using the reaction torque. That explicit
        /// ordering is stable at a 200 Hz fixed step for realistic wheel inertias.
        /// </summary>
        public void UpdateTire(Rigidbody rb, CarSpec spec, TireCurve curve, float dt)
        {
            AxleSpec axle = Axle(spec);
            TireSpec tire = spec.tires;
            float radius = axle.wheelRadius;

            // Build the contact-plane basis from the steered wheel heading.
            Quaternion steer = Quaternion.AngleAxis(SteerAngleDeg, SuspensionUp);
            Vector3 heading = steer * rb.transform.forward;
            Vector3 normal = IsGrounded ? ContactNormal : SuspensionUp;

            Vector3 projected = Vector3.ProjectOnPlane(heading, normal);
            ForwardDir = projected.sqrMagnitude > TougeMath.Epsilon ? projected.normalized : rb.transform.forward;
            RightDir = Vector3.Cross(normal, ForwardDir);

            if (!IsGrounded)
            {
                // No contact: no tyre force, but drive and brake torque still act on the wheel.
                // This is what lets an unloaded inside wheel spin up on an open differential.
                LongitudinalForce = 0f;
                LateralForce = 0f;
                GripUsage = 0f;
                SlipRatio = 0f;
                SlipAngleDeg = 0f;
                _laggedTanSlipAngle = 0f;
                IntegrateWheelSpin(spec, 0f, dt);
                return;
            }

            Vector3 contactVelocity = rb.GetPointVelocity(ContactPoint);
            float vLong = Vector3.Dot(contactVelocity, ForwardDir);
            float vLat = Vector3.Dot(contactVelocity, RightDir);

            // --- Longitudinal slip ratio ---------------------------------------------------------
            // sigma = (wheelSurfaceSpeed - roadSpeed) / referenceSpeed
            // The reference speed is floored so the ratio stays finite as the car approaches a stop.
            float wheelSurfaceSpeed = AngularVelocity * radius;
            float referenceSpeed = Mathf.Max(Mathf.Abs(vLong), tire.lowSpeedBlendThreshold);
            SlipRatio = Mathf.Clamp((wheelSurfaceSpeed - vLong) / referenceSpeed, -4f, 4f);

            // --- Lateral slip angle --------------------------------------------------------------
            // tan(alpha) = lateralSpeed / |longitudinalSpeed|, with the denominator floored for the
            // same reason. The angle itself is only needed for telemetry; the model wants the tangent.
            float tanAlphaTarget = vLat / Mathf.Max(Mathf.Abs(vLong), 1f);

            // Tyre relaxation: lateral force does not appear instantly, it builds as the tyre rolls.
            // Closing the gap over a rolling distance of `relaxationLength` rather than over a fixed
            // time is what makes this frame-rate independent AND correctly freezes the response at a
            // standstill (a stationary tyre generates no new lateral force).
            float rollingSpeed = Mathf.Abs(vLong);
            float relaxK = 1f - Mathf.Exp(-rollingSpeed * dt / Mathf.Max(tire.relaxationLength, TougeMath.Epsilon));
            _laggedTanSlipAngle = Mathf.Lerp(_laggedTanSlipAngle, tanAlphaTarget, relaxK);
            SlipAngleDeg = Mathf.Atan(_laggedTanSlipAngle) * Mathf.Rad2Deg;

            // --- Tyre force ----------------------------------------------------------------------
            TireModel.Solve(
                tire, curve.StiffnessB, SlipRatio, _laggedTanSlipAngle, Load, IsFront,
                out float fx, out float fy, out float grip);

            // --- Low-speed blend -----------------------------------------------------------------
            // Both slip quantities are ill-conditioned near zero speed; left alone the tyre buzzes
            // and the parked car creeps. Below the threshold we fade toward a simple damper.
            //
            // CRITICAL: that damper acts on the contact patch SLIP velocity - the relative motion
            // between rubber and road - NOT on the car's velocity. Damping the car's velocity would
            // make the low-speed force zero whenever the car is stationary, and since the blend
            // weight is also the car's speed, the blend would win completely at a standstill and the
            // car could never pull away. Slip velocity includes the wheel's own surface speed, so a
            // driven wheel still produces drive force from rest, while a stationary wheel on a
            // stationary car produces nothing and simply sits there.
            float planarSpeed = new Vector2(vLong, vLat).magnitude;
            float speedBlend = Mathf.Clamp01(planarSpeed / Mathf.Max(tire.lowSpeedBlendThreshold, TougeMath.Epsilon));
            if (speedBlend < 1f)
            {
                float maxForce = TireModel.EffectiveFriction(tire, Load, IsFront) * Load;

                // Longitudinal: positive when the tyre surface outruns the road, pushing the car
                // forward. Lateral: opposes sideways scrub.
                Vector2 slipVelocity = new Vector2(wheelSurfaceSpeed - vLong, -vLat);
                Vector2 lowSpeedForce = slipVelocity * tire.lowSpeedDamping;

                // Still bounded by the friction circle, so this behaves like static friction rather
                // than an arbitrary spring.
                if (lowSpeedForce.magnitude > maxForce)
                    lowSpeedForce = lowSpeedForce.normalized * maxForce;

                fx = Mathf.Lerp(lowSpeedForce.x, fx, speedBlend);
                fy = Mathf.Lerp(lowSpeedForce.y, fy, speedBlend);
                grip = Mathf.Lerp(TougeMath.SafeDivide(lowSpeedForce.magnitude, maxForce), grip, speedBlend);
            }

            LongitudinalForce = fx;
            LateralForce = fy;
            GripUsage = grip;

            rb.AddForceAtPosition(ForwardDir * fx + RightDir * fy, ContactPoint, ForceMode.Force);

            // Rolling resistance acts as a torque on the wheel rather than a force on the body, which
            // is both more correct and keeps it out of the friction-circle budget.
            // The deadband matters: Mathf.Sign(0) returns +1, so without it a stationary wheel would
            // be handed a constant negative torque and slowly drive the parked car backwards.
            float rollingResistTorque = 0f;
            if (Mathf.Abs(AngularVelocity) > 0.05f)
                rollingResistTorque = -Mathf.Sign(AngularVelocity) * tire.rollingResistance * Load * radius;

            IntegrateWheelSpin(spec, LongitudinalForce, dt, rollingResistTorque);
        }

        /// <summary>
        /// Advance the wheel's rotation for one step.
        ///
        ///     I * dOmega/dt = driveTorque - Fx*r + rollingResistance - brakeTorque
        ///
        /// Brake torque is applied last and clamped so it can bring the wheel to a stop but never
        /// drive it backwards. Handling it this way rather than with a naive sign(omega) term is what
        /// stops a braked wheel oscillating around zero - and a wheel that reaches exactly zero while
        /// the car still moves is a locked wheel, which the slip ratio then reports as -1 and the
        /// tyre model turns into a loss of lateral grip. That is the entire basis of the e-brake drift.
        /// </summary>
        private void IntegrateWheelSpin(CarSpec spec, float longitudinalForce, float dt, float extraTorque = 0f)
        {
            AxleSpec axle = Axle(spec);
            float inertia = Mathf.Max(axle.WheelInertia, TougeMath.Epsilon);

            float reactionTorque = -longitudinalForce * axle.wheelRadius;
            float netTorque = DriveTorque + reactionTorque + extraTorque;

            float omega = AngularVelocity + netTorque / inertia * dt;

            float brakeDeltaOmega = Mathf.Abs(BrakeTorque) / inertia * dt;
            if (Mathf.Abs(omega) <= brakeDeltaOmega)
                omega = 0f;                                         // Fully locked this step.
            else
                omega -= Mathf.Sign(omega) * brakeDeltaOmega;

            AngularVelocity = omega;
            SpinAngleDeg = Mathf.Repeat(SpinAngleDeg + omega * Mathf.Rad2Deg * dt, 360f);
        }

        /// <summary>
        /// Scratch buffer for the ground cast.
        ///
        /// Static and shared by all four corners. That is safe here because the wheels are stepped
        /// one after another inside a single FixedUpdate on the main thread (see
        /// <see cref="CarController"/>), so only one cast is ever in flight and its results are fully
        /// consumed before the next call. Determinism is unaffected - every cast overwrites the
        /// entries it reads.
        /// </summary>
        private static readonly RaycastHit[] GroundHits = new RaycastHit[8];

        /// <summary>
        /// Find the nearest DRIVABLE surface beneath the wheel.
        ///
        /// "Drivable" is the important word, and taking the closest hit is not good enough. The
        /// guardrails sit on the same layer as the road, and the sphere cast readily catches their
        /// vertical inner face whenever the car runs alongside one. When that happened the wheel
        /// treated a wall as ground, and three things went wrong at once:
        ///
        ///   - the contact normal came back horizontal, so the contact-plane basis tipped on its
        ///     side and the tyre fired its LATERAL force vertically;
        ///   - the ground distance measured along the strut collapsed, so the spring read a huge
        ///     compression, ran past maxTravel and fired the 250 kN/m bump stop;
        ///   - all of that was applied at a point on the wall face.
        ///
        /// The result was tens of kilonewtons throwing the car into the barrier and holding it
        /// there, which is the "stuck inside the border" bug.
        ///
        /// Rejecting by surface ANGLE rather than by layer keeps this correct however the scene is
        /// authored, and considering every hit along the sweep rather than just the first means a
        /// wheel that is touching a rail and the road at the same time still finds the road.
        /// </summary>
        private static bool CastGround(Vector3 origin, Vector3 direction, Vector3 up, float length,
                                       SuspensionSpec shared, out RaycastHit hitInfo)
        {
            hitInfo = default;

            int count;
            if (shared.castRadius > TougeMath.Epsilon)
            {
                // Cast the sphere from one radius back up the strut so it cannot start already
                // intersecting the road surface, which would report a zero-distance hit.
                count = UnityEngine.Physics.SphereCastNonAlloc(
                    origin + direction * -shared.castRadius,
                    shared.castRadius,
                    direction,
                    GroundHits,
                    length + shared.castRadius,
                    shared.groundMask,
                    QueryTriggerInteraction.Ignore);
            }
            else
            {
                count = UnityEngine.Physics.RaycastNonAlloc(
                    origin, direction, GroundHits, length, shared.groundMask,
                    QueryTriggerInteraction.Ignore);
            }

            float minNormalDot = Mathf.Cos(Mathf.Clamp(shared.maxDrivableSlopeDeg, 0f, 89f) * Mathf.Deg2Rad);
            float nearest = float.MaxValue;
            bool found = false;

            for (int i = 0; i < count; i++)
            {
                RaycastHit candidate = GroundHits[i];

                // Zero distance means the cast began already inside that collider, and PhysX then
                // reports no usable point or normal. There is nothing to do with such a hit.
                if (candidate.distance <= 0f) continue;
                if (candidate.distance >= nearest) continue;
                if (Vector3.Dot(candidate.normal, up) < minNormalDot) continue;

                nearest = candidate.distance;
                hitInfo = candidate;
                found = true;
            }

            return found;
        }

        /// <summary>Reset all transient state. Used when respawning or teleporting the car.</summary>
        public void Reset()
        {
            AngularVelocity = 0f;
            SteerAngleDeg = 0f;
            SpinAngleDeg = 0f;
            Compression = 0f;
            CompressionRatio = 0f;
            Load = 0f;
            SlipRatio = 0f;
            SlipAngleDeg = 0f;
            LongitudinalForce = 0f;
            LateralForce = 0f;
            GripUsage = 0f;
            DriveTorque = 0f;
            BrakeTorque = 0f;
            IsGrounded = false;
            _laggedTanSlipAngle = 0f;
            _springDamperForce = 0f;
        }
    }
}
