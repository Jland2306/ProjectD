using Touge.Core;
using Touge.Vehicle.Data;
using Touge.Vehicle.Physics;
using UnityEngine;

namespace Touge.Vehicle
{
    /// <summary>
    /// Turns a raw steering axis into per-wheel steer angles.
    ///
    /// Everything in here shapes the DEMAND only. No force or torque is ever applied from this class,
    /// including by the counter-steer assist - the assist adds steering input exactly as a human
    /// would, and the tyre model is left to decide what that input achieves. That distinction is what
    /// keeps the assist honest: turn it to zero and the car behaves exactly as the physics dictates.
    /// </summary>
    public class SteeringController
    {
        /// <summary>Rate-limited steer angle at the axle centreline, before Ackermann. [degrees]</summary>
        public float SteerAngleDeg { get; private set; }

        /// <summary>Lock contributed by the counter-steer assist this step. [degrees]</summary>
        public float CounterSteerAssistDeg { get; private set; }

        /// <summary>
        /// Advance the steering by one step.
        /// </summary>
        /// <param name="spec">Car configuration.</param>
        /// <param name="steerInput">Raw axis, -1 (full left) to +1 (full right).</param>
        /// <param name="speedKph">Speed over ground, for speed-sensitive lock reduction. [km/h]</param>
        /// <param name="driftAngleDeg">Body slip angle, positive when the nose is right of the
        /// velocity vector. Used only by the counter-steer assist.</param>
        /// <param name="dt">Physics timestep. [s]</param>
        public void Step(CarSpec spec, float steerInput, float speedKph, float driftAngleDeg, float dt)
        {
            SteeringSpec steering = spec.steering;
            AssistSpec assists = spec.assists;

            // Speed-sensitive lock: full articulation when parking, progressively less at speed, so
            // the same stick deflection does not become unusably sharp on a fast sweeper.
            float speedFactor = Mathf.Clamp(steering.speedSensitivityCurve.Evaluate(speedKph), 0.05f, 1f);
            float availableLock = steering.maxSteerAngleDeg * speedFactor;

            float target = steerInput * availableLock;

            // ---- Counter-steer assist -----------------------------------------------------------
            CounterSteerAssistDeg = 0f;
            if (assists.counterSteerAssist > TougeMath.Epsilon)
            {
                // Ignore the few degrees of body slip that every quick corner carries. Without this
                // deadband the assist trims opposite lock into ordinary cornering, which reads as
                // understeer on turn-in - it quietly fights the driver at exactly the moment the car
                // should feel sharpest. Past the deadband the car is genuinely sideways and the
                // assist is doing what a driver's hands would.
                float deadband = Mathf.Max(0f, assists.counterSteerAssistDeadbandDeg);
                float slideAngle = Mathf.Sign(driftAngleDeg) *
                                   Mathf.Max(0f, Mathf.Abs(driftAngleDeg) - deadband);

                // Steer INTO the slide: a positive drift angle (nose right of travel, tail out left)
                // calls for left lock, hence the negation.
                float desired = -slideAngle * assists.counterSteerAssist;
                float limit = steering.maxSteerAngleDeg * assists.counterSteerAssistMaxFraction;
                CounterSteerAssistDeg = Mathf.Clamp(desired, -limit, limit);

                // The assist may exceed the speed-sensitive limit - catching a slide needs more lock
                // than normal cornering does - but never more than the rack physically has.
                target = Mathf.Clamp(target + CounterSteerAssistDeg,
                                     -steering.maxSteerAngleDeg, steering.maxSteerAngleDeg);
            }

            // ---- Rate limiting ------------------------------------------------------------------
            SteerAngleDeg = RateLimit(SteerAngleDeg, target, steering, dt);
        }

        /// <summary>
        /// Move the rack toward <paramref name="target"/>, using the faster self-centring rate for
        /// any part of the move that UNWINDS lock and the slower rate for any part that adds it.
        ///
        /// The distinction has to be made on the direction of motion, not on which end is closer to
        /// centre. Comparing magnitudes - |target| &lt; |current| - gets the single most important
        /// case wrong: a flick from full left to full right has equal magnitudes, so it was treated
        /// as winding on and ran the ENTIRE swing, including the half that is really the wheel
        /// unwinding, at the slow rate. That swing is precisely the motion used to catch a slide,
        /// and it was the slowest thing the rack could do.
        ///
        /// A move that crosses centre is therefore split in two: unwind to zero at the return rate,
        /// then spend whatever is left of the step winding on the opposite lock.
        /// </summary>
        private static float RateLimit(float current, float target, SteeringSpec steering, float dt)
        {
            float windRate = Mathf.Max(0f, steering.steerRateDegPerSec);
            float unwindRate = Mathf.Max(0f, steering.returnRateDegPerSec);

            if (current * target >= 0f)
            {
                // Same side of centre: the whole move is either winding on or unwinding.
                bool unwinding = Mathf.Abs(target) < Mathf.Abs(current);
                return TougeMath.MoveTowardsRate(current, target, unwinding ? unwindRate : windRate, dt);
            }

            // Crossing centre. Phase 1: back to straight ahead at the unwind rate.
            // A zero unwind rate yields an infinite time, which correctly parks the wheel.
            float unwindTime = TougeMath.SafeDivide(Mathf.Abs(current), unwindRate, float.PositiveInfinity);
            if (unwindTime >= dt)
                return TougeMath.MoveTowardsRate(current, 0f, unwindRate, dt);

            // Phase 2: the remainder of the step goes into opposite lock.
            return TougeMath.MoveTowardsRate(0f, target, windRate, dt - unwindTime);
        }

        /// <summary>Write the resolved steer angle, with Ackermann correction, onto the steered wheels.</summary>
        public void ApplyToWheels(Wheel[] wheels, CarSpec spec)
        {
            foreach (Wheel wheel in wheels)
            {
                AxleSpec axle = spec.GetAxle(wheel.IsFront);
                wheel.SteerAngleDeg = axle.steerable ? AckermannAngle(SteerAngleDeg, wheel.Side, spec) : 0f;
            }
        }

        /// <summary>
        /// Ackermann steering correction.
        ///
        /// In a turn the inside wheel traces a tighter radius than the outside wheel, so it must steer
        /// a larger angle or it scrubs. For a turn radius R at the axle centre and track width t:
        ///
        ///     inner = atan(L / (R - t/2))      outer = atan(L / (R + t/2))
        ///
        /// where R = L / tan(steerAngle). The result is blended against the uncorrected angle by
        /// ackermannFactor, since real racks only ever approximate true Ackermann.
        /// </summary>
        private static float AckermannAngle(float centreAngleDeg, int side, CarSpec spec)
        {
            float factor = spec.steering.ackermannFactor;
            if (factor <= TougeMath.Epsilon || Mathf.Abs(centreAngleDeg) < 0.01f) return centreAngleDeg;

            float wheelbase = spec.Wheelbase;
            float trackWidth = spec.frontAxle.trackWidth;
            if (wheelbase < TougeMath.Epsilon) return centreAngleDeg;

            float turnSign = Mathf.Sign(centreAngleDeg);
            float turnRadius = wheelbase / Mathf.Tan(Mathf.Abs(centreAngleDeg) * Mathf.Deg2Rad);

            // The inside wheel is the one on the same side as the direction of turn.
            bool isInner = Mathf.Approximately(side, turnSign);
            float offset = isInner ? -trackWidth * 0.5f : trackWidth * 0.5f;

            // Floor the effective radius: at extreme lock the inside wheel's radius can approach zero,
            // which would demand a 90 degree steer angle.
            float effectiveRadius = Mathf.Max(turnRadius + offset, 0.2f);
            float corrected = Mathf.Atan(wheelbase / effectiveRadius) * Mathf.Rad2Deg * turnSign;

            return Mathf.Lerp(centreAngleDeg, corrected, factor);
        }

        /// <summary>Clear steering state, for respawns.</summary>
        public void Reset()
        {
            SteerAngleDeg = 0f;
            CounterSteerAssistDeg = 0f;
        }
    }
}
