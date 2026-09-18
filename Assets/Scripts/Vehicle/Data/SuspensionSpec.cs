using System;
using UnityEngine;

namespace Touge.Vehicle.Data
{
    /// <summary>
    /// Spring and damper settings for one axle.
    ///
    /// The suspension force along the cast direction is:
    ///     F = springStiffness * compression  +  damper * compressionVelocity
    /// where compression is how far the spring is squashed from its rest length [m] and
    /// compressionVelocity is the rate of change of that [m/s].
    ///
    /// Sizing the spring: to carry a static corner load of Fz newtons at a target compression of
    /// x metres, springStiffness ~= Fz / x. For a 950 kg car with 55% front bias, a front corner
    /// carries ~2565 N; at 0.09 m of static sag that is ~28500 N/m.
    /// </summary>
    [Serializable]
    public class AxleSuspensionSpec
    {
        [Tooltip("Length of the spring at full droop, measured from the wheel's mount point. [m]\n" +
                 "The cast reaches restLength + wheelRadius; anything beyond that is airborne.")]
        public float restLength = 0.32f;

        [Tooltip("Maximum compression from rest before the bump stop is hit. [m]")]
        public float maxTravel = 0.16f;

        [Tooltip("Spring rate. [N/m] Higher = less body roll and pitch = less weight transfer per unit of g.")]
        public float springStiffness = 28500f;

        [Tooltip("Damping while the spring is compressing. [N per m/s]\n" +
                 "Controls how quickly load arrives on a wheel during a weight shift. " +
                 "This is the parameter that governs how sharply a Scandinavian flick loads the outside tyre.")]
        public float bumpDamper = 2600f;

        [Tooltip("Damping while the spring is extending. [N per m/s]\n" +
                 "Usually 1.5-2x the bump value. Too low and the car floats after a kerb or a flick.")]
        public float reboundDamper = 4200f;

        [Tooltip("Anti-roll bar rate for this axle, in newtons per metre of left/right " +
                 "compression difference. Directly comparable to springStiffness above, but " +
                 "note a bar contributes TWICE the roll stiffness of a spring at the same rate, " +
                 "because it acts differentially across the axle. Useful values are 10-20% of " +
                 "the spring rate. " +
                 "KEY TUNING LEVER: stiffening one axle's bar moves lateral load transfer onto " +
                 "that axle, which reduces ITS grip, so more rear bar = looser rear = easier " +
                 "drift initiation. Too stiff on BOTH flattens the car and kills the roll " +
                 "transients that a Scandinavian flick depends on.")]
        public float antiRollStiffness = 3500f;

        [Tooltip("Bump stop rate once maxTravel is exceeded. [N/m] Very stiff by design - " +
                 "this is what stops the chassis punching through the ground on a big landing.")]
        public float bumpStopStiffness = 250000f;
    }

    /// <summary>Suspension section of the car spec: per-axle springs plus shared ground-cast settings.</summary>
    [Serializable]
    public class SuspensionSpec
    {
        public AxleSuspensionSpec front = new AxleSuspensionSpec();

        public AxleSuspensionSpec rear = new AxleSuspensionSpec
        {
            restLength = 0.32f,
            maxTravel = 0.16f,
            springStiffness = 24000f,   // Softer rear: less rear grip under load transfer, mildly loose balance.
            bumpDamper = 2300f,
            reboundDamper = 3700f,
            antiRollStiffness = 5000f   // Stiffer rear bar than front - biases the car toward oversteer.
        };

        [Tooltip("Layers treated as drivable ground by the suspension casts. " +
                 "Must exclude the car's own colliders or every wheel will hit the body.")]
        public LayerMask groundMask = ~0;

        [Tooltip("Radius of the sphere cast used to find the ground. 0 = use a cheaper single raycast.\n" +
                 "A radius of roughly a third of the wheel radius stops wheels dropping into small seams " +
                 "between road mesh triangles. [m]")]
        public float castRadius = 0.12f;

        [Tooltip("Extra distance cast beyond full droop, used to detect ground just out of reach. [m]")]
        public float castMargin = 0.05f;

        [Tooltip("Steepest surface the suspension will accept as drivable ground. [degrees]\n" +
                 "Anything steeper - a guardrail face, a wall, the side of a rock - is ignored by the " +
                 "cast and the wheel looks past it for real road. Without this a wheel that brushes a " +
                 "barrier treats the wall as ground: the contact normal comes back horizontal, the tyre " +
                 "fires its lateral force vertically, and the spring reads a huge compression and blows " +
                 "through the bump stop. That is what pins the car inside a barrier.\n" +
                 "Must sit above the steepest road bank and below vertical. 60 is a good default.")]
        [Range(0f, 89f)]
        public float maxDrivableSlopeDeg = 60f;

        /// <summary>Suspension settings for the requested axle.</summary>
        public AxleSuspensionSpec ForAxle(bool isFront) => isFront ? front : rear;
    }
}
