using Touge.Core;
using Touge.Vehicle.Data;
using Touge.Vehicle.Physics;
using UnityEngine;

namespace Touge.Vehicle.Drivetrain
{
    /// <summary>
    /// Splits one axle's torque between its two wheels and resists the speed difference between them.
    ///
    /// Both halves matter. The torque split decides how much each wheel can pull; the locking
    /// behaviour decides whether one wheel is allowed to run away from the other. For a drift car the
    /// second is the important one - an open differential sends equal torque to both sides, which
    /// means the wheel with the LEAST grip sets the limit for the pair. Mid-corner that is the
    /// unloaded inside wheel, so an open-diff car lights up one rear tyre and struggles to hold a
    /// slide. A limited-slip or welded diff forces both rear wheels to turn together, which is why
    /// nearly every drift car runs one.
    /// </summary>
    public static class Differential
    {
        /// <summary>
        /// Locking capacity used for a welded differential. Deliberately far above anything the
        /// engine can produce - the clamp that actually bounds the applied torque is the
        /// one-step synchronisation limit, not this number.
        /// </summary>
        private const float WeldedCapacityNm = 100000f;

        /// <summary>
        /// Distribute <paramref name="axleTorque"/> across the two wheels of an axle, writing the
        /// result into each wheel's <see cref="Wheel.DriveTorque"/>.
        ///
        /// The locking term is expressed as the torque that would bring the two wheels to the same
        /// speed within a single physics step:
        ///
        ///     T_sync = deltaOmega * I_wheel / (2 * dt)
        ///
        /// then clamped to whatever the fitted differential can actually transmit. Deriving it this
        /// way rather than from an arbitrary stiffness constant means a welded diff synchronises
        /// exactly, with no overshoot or chatter, at any physics rate.
        /// </summary>
        /// <param name="left">Left wheel of the axle.</param>
        /// <param name="right">Right wheel of the axle.</param>
        /// <param name="axleTorque">Torque arriving at this axle from the driveshaft. [N*m]</param>
        /// <param name="spec">Differential configuration.</param>
        /// <param name="isFront">Which axle, selecting the fitted differential type.</param>
        /// <param name="wheelInertia">Rotational inertia of one wheel on this axle. [kg*m^2]</param>
        /// <param name="dt">Physics timestep. [s]</param>
        public static void Distribute(
            Wheel left,
            Wheel right,
            float axleTorque,
            DifferentialSpec spec,
            bool isFront,
            float wheelInertia,
            float dt)
        {
            // Torque itself always splits evenly. What differs between diff types is how much
            // additional torque is shuffled across the axle to resist a speed difference.
            float half = axleTorque * 0.5f;

            DifferentialType type = spec.GetDifferentialType(isFront);
            float capacity;
            float deadband;

            switch (type)
            {
                case DifferentialType.Open:
                    // No locking at all: each wheel is free to spin at its own speed.
                    capacity = 0f;
                    deadband = 0f;
                    break;

                case DifferentialType.Locked:
                    // Welded: no tolerated difference, effectively unlimited locking torque.
                    capacity = WeldedCapacityNm;
                    deadband = 0f;
                    break;

                default:
                    // Clutch-pack LSD. Capacity is a static preload plus a ramp proportional to the
                    // torque passing through, using a different ramp rate on power than on overrun -
                    // which is why a 2-way diff behaves differently entering and exiting a corner.
                    float ramp = axleTorque >= 0f ? spec.powerLockFactor : spec.coastLockFactor;
                    capacity = spec.preloadNm + ramp * Mathf.Abs(axleTorque);
                    deadband = Mathf.Max(0f, spec.lockSpeedTolerance);
                    break;
            }

            float omegaDifference = left.AngularVelocity - right.AngularVelocity;

            // Inside the deadband the LSD lets the wheels turn at different speeds freely, which is
            // what allows the car to corner without scrubbing.
            float excess = Mathf.Sign(omegaDifference) *
                           Mathf.Max(0f, Mathf.Abs(omegaDifference) - deadband);

            float syncTorque = excess * wheelInertia / (2f * Mathf.Max(dt, TougeMath.Epsilon));
            float lockTorque = Mathf.Clamp(syncTorque, -capacity, capacity);

            // The faster wheel is slowed and the slower wheel is driven, by equal and opposite
            // amounts - the differential cannot create or destroy torque, only move it across.
            left.DriveTorque = half - lockTorque;
            right.DriveTorque = half + lockTorque;
        }
    }
}
