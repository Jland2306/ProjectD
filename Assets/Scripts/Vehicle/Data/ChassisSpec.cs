using System;
using UnityEngine;

namespace Touge.Vehicle.Data
{
    /// <summary>
    /// Rigid body properties. These are pushed onto the Rigidbody every physics step so that
    /// edits in the Inspector take effect live in Play Mode.
    ///
    /// Longitudinal and lateral weight transfer are NOT simulated here - they emerge from the
    /// suspension forces acting at the contact patches against this mass and inertia. Getting the
    /// centre of mass height right is therefore the single biggest lever on how the car transfers load.
    /// </summary>
    [Serializable]
    public class ChassisSpec
    {
        [Tooltip("Total vehicle mass including driver and fuel. [kg]")]
        public float massKg = 950f;

        [Tooltip("Centre of mass in body-local space, relative to the rigidbody origin. " +
                 "Y is the big one: lower = less weight transfer = flatter, less rotation on entry. [m]")]
        public Vector3 centerOfMassOffset = new Vector3(0f, 0.05f, 0.125f);

        [Tooltip("Override Unity's auto-computed inertia tensor. Strongly recommended: the auto value " +
                 "is derived from the collider shape and is usually far too high in yaw for a car, " +
                 "which makes rotation feel sluggish and un-catchable.")]
        public bool overrideInertiaTensor = true;

        [Tooltip("Moment of inertia about each body-local axis. [kg*m^2]\n" +
                 "X = pitch, Y = yaw, Z = roll.\n" +
                 "Yaw (Y) is the drift-critical one: lower = the car rotates and stops rotating faster.")]
        public Vector3 inertiaTensor = new Vector3(1250f, 1200f, 350f);

        [Tooltip("Aerodynamic drag, expressed as the force coefficient k in F = k * v^2. [N/(m/s)^2]\n" +
                 "k = 0.5 * airDensity * Cd * frontalArea. For a boxy 80s hatch: ~0.5*1.225*0.36*1.8 = 0.40")]
        public float aeroDragCoefficient = 0.40f;

        [Tooltip("Downforce coefficient in F = k * v^2, split front/rear. [N/(m/s)^2]\n" +
                 "A road car of this era makes essentially none. Left near zero deliberately.")]
        public float downforceFront = 0.02f;

        [Tooltip("See downforceFront. [N/(m/s)^2]")]
        public float downforceRear = 0.03f;

        [Tooltip("Angular drag applied to the rigidbody. Keep very low - real yaw damping should come " +
                 "from the tyres, not from a fudge factor. Raising this makes drifts feel artificially stable.")]
        public float angularDamping = 0.05f;
    }
}
