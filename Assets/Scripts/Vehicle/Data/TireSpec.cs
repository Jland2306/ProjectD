using System;
using UnityEngine;

namespace Touge.Vehicle.Data
{
    /// <summary>
    /// Tyre force model parameters - a normalised Pacejka "Magic Formula" evaluated once on the
    /// COMBINED slip vector, which gives a true friction ellipse rather than a post-hoc clamp.
    ///
    /// How the model is put together (see TireModel.cs for the implementation):
    ///
    ///  1. Normalise each slip channel by the slip at which it peaks:
    ///         sigmaN = slipRatio      / peakSlipRatio
    ///         alphaN = tan(slipAngle) / tan(peakSlipAngle)
    ///  2. Combined slip magnitude:  rho = sqrt(sigmaN^2 + alphaN^2)
    ///  3. Evaluate the normalised magic formula once:  g(rho), where g(1) = 1 by construction.
    ///  4. Total force magnitude:    F = g(rho) * mu(Fz) * Fz
    ///  5. Project back along the slip direction:
    ///         Fx = F * sigmaN / rho      Fy = -F * alphaN / rho
    ///
    /// Because the magnitude is bounded by mu*Fz at every step, longitudinal and lateral demand
    /// automatically compete for one grip budget: heavy throttle eats lateral grip, which is exactly
    /// what makes power oversteer emerge instead of having to be faked.
    ///
    /// The raw magic formula is:
    ///     g(u) = sin(C * atan(B*u - E*(B*u - atan(B*u))))
    /// B (stiffness) is NOT authored here. It is solved automatically from C and E so that the curve
    /// peaks exactly at rho = 1, which is what makes the peak-slip values above physically meaningful.
    /// </summary>
    [Serializable]
    public class TireSpec
    {
        [Header("Grip")]
        [Tooltip("Peak friction coefficient at the reference load. Force at peak = mu * Fz.\n" +
                 "~1.0 is a decent road tyre, 1.3+ is a semi-slick, 0.8 is a hard budget tyre.")]
        public float peakFrictionCoefficient = 1.05f;

        [Tooltip("Grip multiplier applied to the front axle only. Lower = more understeer.")]
        public float frontGripScale = 1.0f;

        [Tooltip("Grip multiplier applied to the rear axle only.\n" +
                 "PRIMARY BALANCE LEVER: below 1.0 the car is loose and rotates easily; " +
                 "above 1.0 it pushes and resists drifting.")]
        public float rearGripScale = 0.96f;

        [Header("Curve shape")]
        [Tooltip("Shape factor C. The main falloff control: how much grip is LOST once you exceed " +
                 "the peak slip.\n" +
                 "1.3 = very forgiving plateau, slides are easy to hold.\n" +
                 "1.7 = moderate falloff (default).\n" +
                 "2.2 = sharp drop past the peak, snappy and punishing.\n" +
                 "Must stay above 1.0 or the curve never peaks at all.")]
        [Range(1.05f, 2.5f)]
        public float shapeC = 1.7f;

        [Tooltip("Curvature factor E, 0-1. Adjusts how rounded the shoulder of the curve is near the peak. " +
                 "Higher values flatten the approach to the peak and soften the falloff slightly.")]
        [Range(0f, 0.98f)]
        public float curvatureE = 0.6f;

        [Tooltip("Longitudinal slip ratio at which braking/driving force peaks. Dimensionless.\n" +
                 "Real tyres peak around 0.10-0.15. Larger values make wheelspin and lockup more gradual.")]
        public float peakSlipRatio = 0.12f;

        [Tooltip("Lateral slip angle at which cornering force peaks. [degrees]\n" +
                 "Road tyres peak around 7-10 deg. LARGER values widen the window in which you can hold " +
                 "a drift, because the tyre stays near peak force over a broader angle range.")]
        public float peakSlipAngleDeg = 8.5f;

        [Header("Load sensitivity")]
        [Tooltip("Load at which peakFrictionCoefficient is exactly achieved. [N]\n" +
                 "Set near a static corner load: mass * 9.81 / 4. For 950 kg that is ~2330 N.")]
        public float referenceLoadN = 2330f;

        [Tooltip("How much the friction coefficient falls as load rises above the reference, 0-1.\n" +
                 "mu(Fz) = mu * (1 - loadSensitivity * (Fz/refLoad - 1))\n" +
                 "THIS IS WHY WEIGHT TRANSFER MATTERS. At zero, an axle's total grip would not care how " +
                 "load is split across its two tyres and the car would barely respond to weight shifts. " +
                 "0.15-0.30 is realistic; higher exaggerates the effect of transfer.")]
        [Range(0f, 0.6f)]
        public float loadSensitivity = 0.22f;

        [Tooltip("Lower clamp on the load-sensitivity multiplier, so a heavily loaded tyre never loses " +
                 "an implausible amount of grip.")]
        [Range(0.2f, 1f)]
        public float minLoadSensitivityFactor = 0.55f;

        [Header("Transient and low speed")]
        [Tooltip("Relaxation length: the distance the tyre must roll for its lateral force to build up. [m]\n" +
                 "Models sidewall flex. This is the main thing that keeps the tyre stable at high physics " +
                 "rates and stops the lateral force oscillating. 0.3-0.6 m is typical. " +
                 "Too high feels vague and delayed; too low reintroduces jitter.")]
        public float relaxationLength = 0.45f;

        [Tooltip("Below this speed the model blends toward a simple velocity-damping contact that holds " +
                 "the car still. [m/s]\n" +
                 "Slip ratio and slip angle are both ill-conditioned near zero speed, so without this " +
                 "the car vibrates at rest. 2-3 m/s works well.")]
        public float lowSpeedBlendThreshold = 2.5f;

        [Tooltip("Stiffness of the low-speed damping contact that replaces the slip model near standstill. " +
                 "[N per m/s of contact patch velocity]")]
        public float lowSpeedDamping = 900f;

        [Tooltip("Rolling resistance coefficient. Force = Crr * Fz, always opposing travel.")]
        public float rollingResistance = 0.014f;

        /// <summary>Per-axle grip multiplier.</summary>
        public float GripScale(bool isFront) => isFront ? frontGripScale : rearGripScale;
    }
}
