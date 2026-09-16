using System;
using Touge.Core;
using UnityEngine;

namespace Touge.Vehicle.Data
{
    /// <summary>
    /// Gearbox and clutch. Total reduction from crank to wheel is
    /// <c>gearRatio * finalDrive</c>, so wheel torque = engineTorque * ratio * finalDrive * efficiency,
    /// and engine speed = wheelSpeed * ratio * finalDrive.
    /// </summary>
    [Serializable]
    public class GearboxSpec
    {
        [Tooltip("Forward gear ratios, first gear first. Close-ratio 5-speed from a late-80s FR hatch.")]
        public float[] forwardRatios = { 3.587f, 2.022f, 1.384f, 1.000f, 0.861f };

        [Tooltip("Reverse gear ratio (stored positive, applied as a direction flip).")]
        public float reverseRatio = 3.484f;

        [Tooltip("Final drive ratio at the differential.")]
        public float finalDrive = 4.30f;

        [Tooltip("Driveline mechanical efficiency, 0-1. Accounts for gear mesh and bearing losses.")]
        [Range(0.7f, 1f)]
        public float efficiency = 0.90f;

        [Tooltip("Torque interruption time during an automatic-mode shift. [s]\n" +
                 "In manual mode the player's own clutch use governs this instead.")]
        public float shiftTimeSeconds = 0.25f;

        [Tooltip("Which mode the car starts in.")]
        public TransmissionMode defaultMode = TransmissionMode.Manual;

        [Tooltip("Automatic mode upshifts above this engine speed. [rpm]")]
        public float autoUpshiftRpm = 6800f;

        [Tooltip("Automatic mode downshifts below this engine speed. [rpm]\n" +
                 "Must be low enough that an upshift does not immediately trigger a downshift.")]
        public float autoDownshiftRpm = 2800f;

        [Header("Clutch")]
        [Tooltip("Maximum torque the clutch can transmit before it slips. [N*m]\n" +
                 "Should be comfortably above peak engine torque or the clutch slips under normal load. " +
                 "This cap is what converts a fast pedal release into a finite torque spike rather than " +
                 "an infinite impulse - the physical basis of the clutch kick.")]
        public float clutchMaxTorqueNm = 320f;

        [Header("Auto-clutch")]
        [Tooltip("Let the car slip its own clutch at low engine speed, the way a driver's left foot " +
                 "does. Without it an idling engine in gear transmits full clutch capacity, which " +
                 "produces a hard creep and a violent launch. Turn it off to drive the clutch " +
                 "entirely by hand.")]
        public bool autoClutchEnabled = true;

        [Tooltip("Engine speed above which the auto-clutch stops intervening and the clutch locks " +
                 "fully. [rpm] Must sit above idle and below the revs a clutch kick is thrown at, " +
                 "or it would blunt the kick.")]
        public float autoClutchEngageRpm = 2500f;

        [Tooltip("At idle, the fraction of engine torque the auto-clutch will pass to the wheels. " +
                 "The remainder is left over to spin the engine up, which is what makes a launch " +
                 "build instead of bogging. Lower = lazier, more slip; 1 would bog the engine.")]
        [Range(0.2f, 0.95f)]
        public float autoClutchSlipShare = 0.6f;

        [Tooltip("Pedal travel to clutch engagement mapping. X = pedal (0 released .. 1 floored), " +
                 "Y = fraction of clutchMaxTorqueNm transmitted. The steep section is the bite point.")]
        public AnimationCurve clutchEngagementCurve = new AnimationCurve(
            new Keyframe(0f, 1f),
            new Keyframe(0.35f, 0.9f),
            new Keyframe(0.6f, 0.25f),
            new Keyframe(1f, 0f));

        /// <summary>Number of forward gears. Gear indexing elsewhere: -1 = reverse, 0 = neutral, 1..N = forward.</summary>
        public int ForwardGearCount => forwardRatios != null ? forwardRatios.Length : 0;

        /// <summary>
        /// Ratio for a gear index, signed for direction. Returns 0 for neutral, which callers must
        /// treat as "driveline disconnected" rather than dividing by it.
        /// </summary>
        public float GetRatio(int gear)
        {
            if (gear == 0 || forwardRatios == null) return 0f;
            if (gear < 0) return -reverseRatio;
            return gear <= forwardRatios.Length ? forwardRatios[gear - 1] : 0f;
        }
    }
}
