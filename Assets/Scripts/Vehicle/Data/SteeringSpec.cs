using System;
using UnityEngine;

namespace Touge.Vehicle.Data
{
    /// <summary>
    /// Steering geometry and rate limiting. All of this shapes the DEMAND sent to the front wheels;
    /// none of it applies force directly. What the car actually does with that steer angle is
    /// entirely up to the tyre model.
    /// </summary>
    [Serializable]
    public class SteeringSpec
    {
        [Tooltip("Steer angle at full lock, at low speed. [degrees]\n" +
                 "Drift cars run more (40-50) to allow big counter-steer angles without running out of lock. " +
                 "If you spin out because you cannot catch the slide, raise this before touching anything else.")]
        public float maxSteerAngleDeg = 38f;

        [Tooltip("Speed-sensitive lock reduction. X = speed in km/h, Y = multiplier on maxSteerAngleDeg.\n" +
                 "Prevents twitchy, unusable steering at speed without numbing it at walking pace.")]
        public AnimationCurve speedSensitivityCurve = new AnimationCurve(
            new Keyframe(0f, 1f),
            new Keyframe(40f, 0.80f),
            new Keyframe(90f, 0.55f),
            new Keyframe(160f, 0.42f));

        [Tooltip("How fast the steer angle can move toward the input. [degrees/second]\n" +
                 "Models both the driver's hands and the steering rack. Too low makes it impossible to " +
                 "catch a slide; too high makes keyboard input feel like an on/off switch.")]
        public float steerRateDegPerSec = 220f;

        [Tooltip("How fast the wheels return to centre when the input is released. [degrees/second]\n" +
                 "Usually faster than steerRate - the caster angle self-centres the wheel.")]
        public float returnRateDegPerSec = 320f;

        [Tooltip("Ackermann correction, 0-1. At 1 the inside wheel steers more than the outside, " +
                 "matching the tighter radius it traces. Mostly affects low-speed tyre scrub.")]
        [Range(0f, 1f)]
        public float ackermannFactor = 0.6f;
    }
}
