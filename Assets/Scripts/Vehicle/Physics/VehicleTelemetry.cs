using Touge.Core;

namespace Touge.Vehicle.Physics
{
    /// <summary>
    /// A read-only snapshot of the vehicle state, refreshed once per physics step.
    ///
    /// Exists so that the debug HUD, the scene gizmos and (later) the replay recorder all read the
    /// same numbers from one place, instead of each reaching into the solver and risking a different
    /// answer depending on when in the frame they happened to look.
    /// </summary>
    public class VehicleTelemetry
    {
        // ---- Motion -----------------------------------------------------------------------------

        /// <summary>Speed over ground. [m/s]</summary>
        public float SpeedMps;

        /// <summary>Speed over ground. [km/h]</summary>
        public float SpeedKph;

        /// <summary>Forward component of velocity in body space. Negative when reversing. [m/s]</summary>
        public float ForwardSpeed;

        /// <summary>Lateral acceleration. [g] Positive = accelerating toward the car's right.</summary>
        public float LateralG;

        /// <summary>Longitudinal acceleration. [g] Positive = accelerating forward.</summary>
        public float LongitudinalG;

        /// <summary>Yaw rate. [degrees/second]</summary>
        public float YawRateDegPerSec;

        // ---- Powertrain -------------------------------------------------------------------------

        /// <summary>Engine speed. [rpm]</summary>
        public float EngineRpm;

        /// <summary>Engine output at the crank this step. [N*m]</summary>
        public float EngineTorqueNm;

        /// <summary>Torque crossing the clutch this step. [N*m]</summary>
        public float ClutchTorqueNm;

        /// <summary>True while the clutch is transmitting less torque than the engine is producing.</summary>
        public bool ClutchSlipping;

        /// <summary>-1 = reverse, 0 = neutral, 1..N = forward gears.</summary>
        public int Gear;

        /// <summary>Manual or automatic.</summary>
        public TransmissionMode TransmissionMode;

        /// <summary>True while the rev limiter is cutting fuel.</summary>
        public bool RevLimiterActive;

        // ---- Driver input (post-processing, as the solver saw it) --------------------------------

        public float Throttle;
        public float Brake;
        public float Steer;
        public float Handbrake;
        public float Clutch;

        /// <summary>Steer angle actually applied to the front wheels after rate limiting. [degrees]</summary>
        public float SteerAngleDeg;

        /// <summary>Lock added by the counter-steer assist this step. [degrees] 0 when the assist is off.</summary>
        public float CounterSteerAssistDeg;

        // ---- Drift ------------------------------------------------------------------------------

        /// <summary>
        /// Angle between where the car points and where it is travelling. [degrees]
        /// Positive = the nose is right of the velocity vector (tail out to the left).
        /// </summary>
        public float DriftAngleDeg;

        /// <summary>How long the current drift has been sustained. [s] Zero when not drifting.</summary>
        public float DriftDuration;

        /// <summary>Score accumulated during the current drift, reset when it ends.</summary>
        public float CurrentDriftScore;

        /// <summary>Score banked from all completed drifts this session.</summary>
        public float TotalDriftScore;

        /// <summary>True while the drift detector considers the car to be sliding.</summary>
        public bool IsDrifting;

        // ---- Per-wheel --------------------------------------------------------------------------

        /// <summary>
        /// Live reference to the solver's wheel array, ordered FL, FR, RL, RR.
        /// Consumers must treat these as read-only - they are the solver's own objects, not copies.
        /// </summary>
        public Wheel[] Wheels;

        /// <summary>Sum of all four tyre loads. [N] Should sit near mass*9.81 when settled on level ground.</summary>
        public float TotalLoadN;

        /// <summary>Number of wheels currently touching the ground, 0-4.</summary>
        public int GroundedWheelCount;
    }
}
