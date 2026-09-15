using UnityEngine;

namespace Touge.Core
{
    /// <summary>
    /// Small numeric helpers shared by the vehicle, camera and track code.
    /// Everything here is frame-rate independent and allocation free.
    /// </summary>
    public static class TougeMath
    {
        /// <summary>Guard value for divisions by quantities that legitimately reach zero (speed, load, mass).</summary>
        public const float Epsilon = 1e-5f;

        /// <summary>Divide that degrades to <paramref name="fallback"/> instead of producing NaN/Inf.</summary>
        public static float SafeDivide(float numerator, float denominator, float fallback = 0f)
        {
            return Mathf.Abs(denominator) < Epsilon ? fallback : numerator / denominator;
        }

        /// <summary>
        /// Move <paramref name="current"/> toward <paramref name="target"/> at no more than
        /// <paramref name="ratePerSecond"/> units per second. Used for steering rate limiting,
        /// so the same input produces the same motion regardless of frame rate.
        /// </summary>
        public static float MoveTowardsRate(float current, float target, float ratePerSecond, float deltaTime)
        {
            return Mathf.MoveTowards(current, target, ratePerSecond * deltaTime);
        }

        /// <summary>
        /// Frame-rate independent exponential smoothing.
        /// <paramref name="smoothTime"/> is roughly the time to close ~63% of the gap.
        /// Prefer this over a raw Lerp(a, b, k) which silently changes behaviour with frame rate.
        /// </summary>
        public static float Damp(float current, float target, float smoothTime, float deltaTime)
        {
            if (smoothTime <= Epsilon) return target;
            float t = 1f - Mathf.Exp(-deltaTime / smoothTime);
            return Mathf.Lerp(current, target, t);
        }

        /// <inheritdoc cref="Damp(float,float,float,float)"/>
        public static Vector3 Damp(Vector3 current, Vector3 target, float smoothTime, float deltaTime)
        {
            if (smoothTime <= Epsilon) return target;
            float t = 1f - Mathf.Exp(-deltaTime / smoothTime);
            return Vector3.Lerp(current, target, t);
        }

        /// <summary>Exponential smoothing for an angle in degrees, taking the short way around.</summary>
        public static float DampAngle(float current, float target, float smoothTime, float deltaTime)
        {
            return current + Mathf.DeltaAngle(current, target) * (smoothTime <= Epsilon ? 1f : 1f - Mathf.Exp(-deltaTime / smoothTime));
        }

        /// <summary>Remap a value from one range to another without clamping.</summary>
        public static float Remap(float value, float inMin, float inMax, float outMin, float outMax)
        {
            return outMin + SafeDivide(value - inMin, inMax - inMin) * (outMax - outMin);
        }

        /// <summary>Remap and clamp to the output range.</summary>
        public static float RemapClamped(float value, float inMin, float inMax, float outMin, float outMax)
        {
            float t = Mathf.Clamp01(SafeDivide(value - inMin, inMax - inMin));
            return Mathf.Lerp(outMin, outMax, t);
        }

        /// <summary>Radians per second -> revolutions per minute.</summary>
        public static float RadPerSecToRpm(float radPerSec) => radPerSec * (60f / (2f * Mathf.PI));

        /// <summary>Revolutions per minute -> radians per second.</summary>
        public static float RpmToRadPerSec(float rpm) => rpm * ((2f * Mathf.PI) / 60f);

        /// <summary>Metres per second -> kilometres per hour.</summary>
        public static float MpsToKph(float metresPerSecond) => metresPerSecond * 3.6f;

        /// <summary>
        /// Signed drift angle in degrees between the body's forward axis and its velocity,
        /// measured in the horizontal plane. Positive = the car is pointing right of where it is going
        /// (i.e. the tail has stepped out to the left). Returns 0 below <paramref name="minSpeed"/>
        /// because heading is meaningless when nearly stationary.
        /// </summary>
        public static float HorizontalSlipAngleDeg(Vector3 forward, Vector3 velocity, float minSpeed = 0.5f)
        {
            Vector3 flatVel = new Vector3(velocity.x, 0f, velocity.z);
            if (flatVel.sqrMagnitude < minSpeed * minSpeed) return 0f;

            Vector3 flatFwd = new Vector3(forward.x, 0f, forward.z);
            if (flatFwd.sqrMagnitude < Epsilon) return 0f;

            return Vector3.SignedAngle(flatVel.normalized, flatFwd.normalized, Vector3.up);
        }

        /// <summary>
        /// Symmetric smooth deadzone. Removes stick drift without the discontinuity of a hard cut:
        /// output starts at 0 exactly at the deadzone edge and reaches 1 at full deflection.
        /// </summary>
        public static float ApplyDeadzone(float value, float deadzone)
        {
            float magnitude = Mathf.Abs(value);
            if (magnitude <= deadzone) return 0f;
            float scaled = (magnitude - deadzone) / (1f - deadzone);
            return Mathf.Sign(value) * Mathf.Clamp01(scaled);
        }
    }
}
