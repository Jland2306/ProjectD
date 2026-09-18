using System;
using UnityEngine;

namespace Touge.Vehicle.Data
{
    /// <summary>
    /// Engine characteristics. The engine is simulated as a rotating inertia driven by the torque
    /// curve and loaded through the clutch, so revs have real momentum - which is what makes
    /// clutch kicking work.
    /// </summary>
    [Serializable]
    public class EngineSpec
    {
        [Tooltip("Crankshaft torque at wide open throttle. X axis = engine RPM, Y axis = torque in N*m.\n" +
                 "Peak power [kW] = peakTorque[N*m] * rpm * 2*pi / 60 / 1000.\n" +
                 "~130 hp (97 kW) at 7000 rpm needs roughly 132 N*m there.")]
        public AnimationCurve torqueCurveNm = new AnimationCurve(
            new Keyframe(1000f, 85f),
            new Keyframe(2500f, 110f),
            new Keyframe(4500f, 128f),
            new Keyframe(6000f, 132f),
            new Keyframe(7000f, 126f),
            new Keyframe(7800f, 108f));

        [Tooltip("Idle speed the governor holds when the throttle is closed and the clutch is disengaged. [rpm]")]
        public float idleRpm = 850f;

        [Tooltip("Rev limiter cut point. [rpm]")]
        public float redlineRpm = 7600f;

        [Tooltip("Engine stalls below this if the clutch is engaged. Set to 0 to disable stalling. [rpm]")]
        public float stallRpm = 450f;

        [Tooltip("How far the revs must fall below the redline before fuel is restored. " +
                 "Non-zero prevents the limiter from chattering every step. [rpm]")]
        public float limiterHysteresisRpm = 250f;

        [Tooltip("How long fuel stays cut once the limiter trips. [s]")]
        public float limiterCutDuration = 0.06f;

        [Tooltip("Rotational inertia of the crank, flywheel and clutch basket. [kg*m^2]\n" +
                 "Lower = revs rise and fall faster = snappier clutch kicks and blips. " +
                 "A light flywheel on a small 4-cylinder is around 0.12-0.20.")]
        public float flywheelInertia = 0.16f;

        [Tooltip("Closed-throttle engine braking torque per unit of engine speed. [N*m per rad/s]\n" +
                 "This is the force behind lift-off oversteer on a RWD car: lifting mid-corner applies " +
                 "a rearward force at the rear contact patches, which eats into their lateral grip budget.")]
        public float engineBrakingCoefficient = 0.045f;

        [Tooltip("Constant friction torque, always opposing rotation. [N*m]")]
        public float frictionTorqueNm = 12f;

        [Tooltip("How much of the engine braking drag is cancelled as the throttle opens, 0-1.\n" +
                 "A published torque curve is already NET of internal friction and pumping at wide open " +
                 "throttle, so subtracting the full drag on top of it double-counts and quietly removes a " +
                 "large slice of peak torque. 1 = drag acts only off-throttle (most correct for a measured " +
                 "curve), 0 = the old behaviour. Lift-off oversteer is unaffected either way, because at a " +
                 "closed throttle this term does nothing.")]
        [Range(0f, 1f)]
        public float engineBrakingThrottleRelief = 0.85f;

        [Tooltip("Proportional gain for the idle governor - how hard it adds throttle to hold idleRpm. " +
                 "Too high oscillates, too low stalls at rest.")]
        public float idleGovernorGain = 0.0025f;
    }
}
