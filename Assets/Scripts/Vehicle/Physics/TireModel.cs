using Touge.Core;
using Touge.Vehicle.Data;
using UnityEngine;

namespace Touge.Vehicle.Physics
{
    /// <summary>
    /// Normalised Pacejka "Magic Formula" with a true friction ellipse.
    ///
    /// THE CURVE
    ///     g(u) = sin(C * atan(B*u - E*(B*u - atan(B*u))))
    /// where u is slip normalised so that u = 1 is the peak. g(1) = 1 exactly, by construction,
    /// because <see cref="SolveStiffness"/> derives B from C and E to place the peak there.
    /// Past u = 1 the curve falls away - that falloff is what lets a tyre break loose and be
    /// recovered, and C is the knob that controls how violent it is.
    ///
    /// COMBINED SLIP
    /// Longitudinal and lateral slip are normalised by their own peak values, combined into a single
    /// slip vector, and the curve is evaluated ONCE on that vector's magnitude. The resulting force
    /// is projected back along the slip direction. Because the magnitude is bounded by mu*Fz at all
    /// times, the two channels automatically share one grip budget - spend it on acceleration and
    /// there is less left for cornering. Power oversteer therefore falls out of the model rather
    /// than being scripted.
    ///
    /// All forces are in newtons, all angles in radians unless a name says otherwise.
    /// </summary>
    public static class TireModel
    {
        /// <summary>
        /// Solve the stiffness factor B such that the magic formula peaks exactly at u = 1.
        ///
        /// The peak is where the argument of sin() reaches pi/2:
        ///     C * atan(B - E*(B - atan(B))) = pi/2
        /// Rearranged, applied at u = 1:
        ///     (1 - E)*B + E*atan(B) = tan(pi / (2C))
        ///
        /// The left side is strictly increasing in B (its derivative, (1-E) + E/(1+B^2), is positive
        /// for E in [0,1]), so a bisection converges on the single root.
        /// </summary>
        public static float SolveStiffness(float shapeC, float curvatureE)
        {
            // C must exceed 1 or sin() never reaches its peak and the curve has no maximum at all.
            float c = Mathf.Max(1.01f, shapeC);
            float e = Mathf.Clamp(curvatureE, 0f, 0.98f);
            float target = Mathf.Tan(Mathf.PI / (2f * c));

            // A soft curve (C near 1) with high E needs a large B, hence the generous upper bound.
            float lo = 1e-4f;
            float hi = 2000f;
            for (int i = 0; i < 48; i++)
            {
                float mid = 0.5f * (lo + hi);
                float value = (1f - e) * mid + e * Mathf.Atan(mid);
                if (value < target) lo = mid; else hi = mid;
            }
            return 0.5f * (lo + hi);
        }

        /// <summary>
        /// Evaluate the normalised curve. <paramref name="normalisedSlip"/> is slip divided by peak
        /// slip, so 1 means "at the limit". Returns a fraction of peak force in the range [0, 1].
        /// </summary>
        public static float Evaluate(float normalisedSlip, float stiffnessB, float shapeC, float curvatureE)
        {
            float bu = stiffnessB * normalisedSlip;
            return Mathf.Sin(shapeC * Mathf.Atan(bu - curvatureE * (bu - Mathf.Atan(bu))));
        }

        /// <summary>
        /// Friction coefficient at a given vertical load.
        ///
        ///     mu(Fz) = mu0 * (1 - loadSensitivity * (Fz/Fz_ref - 1))
        ///
        /// Real tyres generate sub-linear force with load: doubling the load less than doubles the
        /// grip. THIS IS THE MECHANISM BY WHICH WEIGHT TRANSFER CHANGES THE BALANCE. An axle whose
        /// load is split evenly across its two tyres has more total grip than one carrying the same
        /// load mostly on the outside tyre, so transferring load onto an axle costs that axle grip.
        /// With loadSensitivity at 0 the car would barely respond to weight shifts at all.
        /// </summary>
        public static float EffectiveFriction(TireSpec spec, float loadN, bool isFront)
        {
            float loadRatio = TougeMath.SafeDivide(loadN, spec.referenceLoadN, 1f);
            float factor = 1f - spec.loadSensitivity * (loadRatio - 1f);
            // Clamp so a very lightly loaded tyre cannot claim unbounded grip and a heavily loaded
            // one keeps a sane floor.
            factor = Mathf.Clamp(factor, spec.minLoadSensitivityFactor, 1.35f);
            return spec.peakFrictionCoefficient * factor * spec.GripScale(isFront);
        }

        /// <summary>
        /// The combined-slip solve for one tyre.
        /// </summary>
        /// <param name="spec">Tyre parameters.</param>
        /// <param name="stiffnessB">Pre-solved B for the current C/E (see <see cref="TireCurve"/>).</param>
        /// <param name="slipRatio">Longitudinal slip, dimensionless. Positive = driving, negative = braking.</param>
        /// <param name="tanSlipAngle">tan of the lateral slip angle. Positive = contact patch sliding right.</param>
        /// <param name="loadN">Vertical load on the tyre. [N]</param>
        /// <param name="isFront">Which axle, for the per-axle grip scale.</param>
        /// <param name="longitudinalForce">Out: force along the wheel heading. [N]</param>
        /// <param name="lateralForce">Out: force across the wheel, opposing lateral slip. [N]</param>
        /// <param name="gripUsage">Out: fraction of the available friction circle in use, 0-1.</param>
        public static void Solve(
            TireSpec spec,
            float stiffnessB,
            float slipRatio,
            float tanSlipAngle,
            float loadN,
            bool isFront,
            out float longitudinalForce,
            out float lateralForce,
            out float gripUsage)
        {
            longitudinalForce = 0f;
            lateralForce = 0f;
            gripUsage = 0f;

            if (loadN <= 0f) return;   // Wheel is airborne: no contact, no force.

            // 1. Normalise each slip channel by the slip at which IT peaks. After this both channels
            //    are in the same dimensionless units and can be combined meaningfully.
            float sigmaN = slipRatio / Mathf.Max(spec.peakSlipRatio, TougeMath.Epsilon);
            float tanPeakAlpha = Mathf.Tan(spec.peakSlipAngleDeg * Mathf.Deg2Rad);
            float alphaN = tanSlipAngle / Mathf.Max(tanPeakAlpha, TougeMath.Epsilon);

            // 2. Combined slip magnitude. rho = 1 means the tyre is exactly at its peak, whatever
            //    mix of cornering and acceleration got it there.
            float rho = Mathf.Sqrt(sigmaN * sigmaN + alphaN * alphaN);
            if (rho < TougeMath.Epsilon) return;

            // 3. One curve evaluation for the pair.
            float normalisedForce = Evaluate(rho, stiffnessB, spec.shapeC, spec.curvatureE);

            // 4. Scale to newtons by the load-sensitive friction coefficient.
            float mu = EffectiveFriction(spec, loadN, isFront);
            float forceMagnitude = normalisedForce * mu * loadN;

            // 5. Project back along the slip direction. The result is bounded by the friction circle
            //    by construction, so no clamping step is required.
            float invRho = 1f / rho;
            longitudinalForce = forceMagnitude * (sigmaN * invRho);
            lateralForce = -forceMagnitude * (alphaN * invRho);   // Opposes the lateral slip.

            gripUsage = normalisedForce;
        }
    }

    /// <summary>
    /// Caches the solved stiffness factor B, which only changes when the authored C or E changes.
    /// Solving B is a 48-step bisection, so doing it per wheel per physics step would be wasteful at
    /// 200 Hz - but it must not be cached at Awake either, or live tuning of the curve shape would
    /// silently stop working.
    /// </summary>
    public class TireCurve
    {
        private float _cachedC = float.NaN;
        private float _cachedE = float.NaN;

        /// <summary>Solved stiffness factor for the current curve shape.</summary>
        public float StiffnessB { get; private set; }

        /// <summary>Re-solve B if the authored curve shape has changed since the last call.</summary>
        public void Refresh(TireSpec spec)
        {
            if (Mathf.Approximately(spec.shapeC, _cachedC) && Mathf.Approximately(spec.curvatureE, _cachedE))
                return;

            _cachedC = spec.shapeC;
            _cachedE = spec.curvatureE;
            StiffnessB = TireModel.SolveStiffness(spec.shapeC, spec.curvatureE);
        }
    }
}
