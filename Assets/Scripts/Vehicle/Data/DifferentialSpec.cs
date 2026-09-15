using System;
using Touge.Core;
using UnityEngine;

namespace Touge.Vehicle.Data
{
    /// <summary>
    /// Torque distribution. Layout picks which axles are driven; the differential type decides how
    /// each driven axle shares torque between its two wheels and how much speed difference it allows.
    ///
    /// For drifting this matters enormously: an open diff dumps torque into whichever rear wheel is
    /// unloaded (the inside one, mid-corner), so the car struggles to hold a slide. An LSD or a welded
    /// diff forces both rear wheels to spin together, which is why drift cars run them.
    /// </summary>
    [Serializable]
    public class DifferentialSpec
    {
        [Tooltip("Which axles receive torque. Default RWD; FWD and AWD are fully supported by the solver.")]
        public DrivetrainLayout layout = DrivetrainLayout.RearWheelDrive;

        [Tooltip("Differential fitted to the rear axle.")]
        public DifferentialType rearType = DifferentialType.LimitedSlip;

        [Tooltip("Differential fitted to the front axle (used for FWD and AWD).")]
        public DifferentialType frontType = DifferentialType.Open;

        [Tooltip("AWD only: fraction of engine torque sent to the front axle. 0.4 = 40/60 front/rear.")]
        [Range(0f, 1f)]
        public float centerSplitFront = 0.35f;

        [Header("Limited Slip")]
        [Tooltip("Static clutch preload - locking torque present even with no torque applied. [N*m]\n" +
                 "Raising this makes the car feel more welded at low throttle and stabilises long drifts.")]
        public float preloadNm = 40f;

        [Tooltip("Lock ramp under power: fraction of applied drive torque converted into locking torque, 0-1.\n" +
                 "Higher = the rear axle locks harder the more throttle you give = easier to hold a slide.")]
        [Range(0f, 1f)]
        public float powerLockFactor = 0.55f;

        [Tooltip("Lock ramp on overrun (closed throttle), 0-1.\n" +
                 "Higher = more stable on lift-off, but reduces lift-off oversteer availability.")]
        [Range(0f, 1f)]
        public float coastLockFactor = 0.25f;

        [Tooltip("How much wheel-speed difference the LSD tolerates before applying full locking torque. [rad/s]\n" +
                 "Small values make the diff behave closer to welded.")]
        public float lockSpeedTolerance = 1.5f;

        /// <summary>True when the given axle receives engine torque under the current layout.</summary>
        public bool IsAxleDriven(bool isFront)
        {
            return layout switch
            {
                DrivetrainLayout.AllWheelDrive => true,
                DrivetrainLayout.FrontWheelDrive => isFront,
                _ => !isFront
            };
        }

        /// <summary>Fraction of total engine torque routed to the given axle. Sums to 1 across both axles.</summary>
        public float GetAxleTorqueShare(bool isFront)
        {
            return layout switch
            {
                DrivetrainLayout.AllWheelDrive => isFront ? centerSplitFront : 1f - centerSplitFront,
                DrivetrainLayout.FrontWheelDrive => isFront ? 1f : 0f,
                _ => isFront ? 0f : 1f
            };
        }

        /// <summary>The differential type fitted to the given axle.</summary>
        public DifferentialType GetDifferentialType(bool isFront) => isFront ? frontType : rearType;
    }
}
