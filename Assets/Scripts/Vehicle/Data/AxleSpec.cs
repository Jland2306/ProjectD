using System;
using UnityEngine;

namespace Touge.Vehicle.Data
{
    /// <summary>
    /// Geometry and braking for one axle. Wheel positions are derived from these values rather than
    /// read from transforms, so the physical car is fully described by the spec and a prefab with
    /// mismatched wheel transforms cannot silently change the handling.
    /// </summary>
    [Serializable]
    public class AxleSpec
    {
        [Tooltip("Longitudinal position of the axle in body-local space. " +
                 "Positive is forward of the rigidbody origin. [m]")]
        public float positionZ = 1.25f;

        [Tooltip("Vertical position of the wheel's suspension mount point in body-local space. [m]")]
        public float mountHeightY = 0.10f;

        [Tooltip("Distance between the two wheel centres on this axle. [m]\n" +
                 "Wider track = more resistance to roll = less lateral load transfer.")]
        public float trackWidth = 1.42f;

        [Tooltip("Loaded wheel radius. Directly sets the wheel's rotational speed for a given road " +
                 "speed, so it feeds both the slip ratio and the engine RPM. [m]")]
        public float wheelRadius = 0.30f;

        [Tooltip("Mass of one wheel and tyre assembly, used for its rotational inertia. [kg]")]
        public float wheelMass = 18f;

        [Tooltip("Can this axle steer?")]
        public bool steerable = true;

        [Tooltip("Maximum brake torque this axle can produce at full pedal, per wheel. [N*m]\n" +
                 "Scaled by the brake bias in AssistSpec.")]
        public float maxBrakeTorqueNm = 1600f;

        [Tooltip("Handbrake torque per wheel on this axle. [N*m]\n" +
                 "Zero on the front. On the rear it must be high enough to fully lock the wheels, " +
                 "which is what makes an e-brake drift possible - a locked tyre has near-zero " +
                 "lateral grip, so the rear simply lets go.")]
        public float handbrakeTorqueNm;

        /// <summary>
        /// Rotational inertia of one wheel, modelled as a solid disc: I = 0.5 * m * r^2. [kg*m^2]
        /// Lower inertia lets the wheel change speed faster, so it locks and spins up more readily.
        /// </summary>
        public float WheelInertia => 0.5f * wheelMass * wheelRadius * wheelRadius;

        /// <summary>
        /// Body-local position of one wheel's suspension mount point.
        /// <paramref name="side"/> is -1 for the left wheel, +1 for the right.
        /// </summary>
        public Vector3 GetMountPoint(int side) => new Vector3(side * trackWidth * 0.5f, mountHeightY, positionZ);
    }
}
