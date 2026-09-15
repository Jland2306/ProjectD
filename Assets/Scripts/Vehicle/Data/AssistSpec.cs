using System;
using UnityEngine;

namespace Touge.Vehicle.Data
{
    /// <summary>
    /// Driver aids and brake distribution. Every assist here defaults to OFF or neutral so the raw
    /// vehicle model can be judged on its own before anything is layered on top.
    /// </summary>
    [Serializable]
    public class AssistSpec
    {
        [Header("Brakes")]
        [Tooltip("Fraction of brake torque sent to the front axle, 0-1. 0.62 = 62% front / 38% rear.\n" +
                 "Moving bias REARWARD makes brake-drift entries easier because the rear locks sooner, " +
                 "but too far rearward and the car spins under any straight-line braking.")]
        [Range(0f, 1f)]
        public float brakeBiasFront = 0.62f;

        [Header("Steering assists")]
        [Tooltip("Counter-steer assist strength, 0-1. 0 = fully off, raw.\n" +
                 "Adds steering INPUT proportional to the rear slip angle - it does not apply any force " +
                 "or torque to the car, so the physics stays honest. Useful on keyboard, where you cannot " +
                 "hold a partial counter-steer angle.")]
        [Range(0f, 1f)]
        public float counterSteerAssist;

        [Tooltip("Upper limit on how much lock the counter-steer assist may add, as a fraction of full lock.")]
        [Range(0f, 1f)]
        public float counterSteerAssistMaxFraction = 0.7f;

        [Header("Electronic aids")]
        [Tooltip("Traction control: cuts throttle when a driven wheel exceeds the slip ratio threshold. " +
                 "Leave off - it directly prevents power oversteer.")]
        public bool tractionControlEnabled;

        [Tooltip("Slip ratio above which traction control starts cutting throttle.")]
        public float tractionControlSlipThreshold = 0.16f;

        [Tooltip("ABS: releases brake torque on a wheel whose slip ratio passes the threshold.")]
        public bool absEnabled;

        [Tooltip("Slip ratio magnitude above which ABS releases that wheel's brake.")]
        public float absSlipThreshold = 0.18f;

        [Tooltip("ABS is disabled below this speed so it cannot prevent the car coming to a stop. [m/s]")]
        public float absMinSpeed = 3f;

        [Header("Stability")]
        [Tooltip("Artificial yaw damping, 0-1. A deliberate cheat, left at 0.\n" +
                 "Only raise this if you want an 'assisted' difficulty preset later - it will flatten " +
                 "the drift feel because it fights rotation the tyres legitimately created.")]
        [Range(0f, 1f)]
        public float stabilityYawDamping;
    }
}
