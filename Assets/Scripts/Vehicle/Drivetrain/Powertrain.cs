using Touge.Core;
using Touge.Vehicle.Data;
using UnityEngine;

namespace Touge.Vehicle.Drivetrain
{
    /// <summary>
    /// Engine, clutch and gearbox.
    ///
    /// The engine is a rotating inertia, not a torque lookup. It has its own angular velocity that is
    /// integrated every step and is coupled to the wheels only through a clutch of finite capacity.
    /// That single design decision is what makes several drift techniques possible:
    ///
    ///   - CLUTCH KICK: disengaging lets the engine free-rev (only flywheel inertia resists it);
    ///     re-engaging dumps the stored angular momentum into the driveline as a torque spike
    ///     bounded by clutchMaxTorqueNm. Nothing about it is scripted - it falls out of the coupling.
    ///   - LIFT-OFF OVERSTEER: a closed throttle makes engine torque go NEGATIVE, so the clutch pulls
    ///     backwards on the driven wheels, eating into their share of the friction circle.
    ///   - POWER OVERSTEER: torque multiplied by a low gear easily exceeds what the rear tyres can
    ///     hold, pushing them past peak slip.
    ///
    /// Gear indexing throughout: -1 = reverse, 0 = neutral, 1..N = forward gears.
    /// </summary>
    public class Powertrain
    {
        /// <summary>Crankshaft angular velocity. [rad/s]</summary>
        public float EngineOmega { get; private set; }

        /// <summary>Currently selected gear. -1 = reverse, 0 = neutral, 1..N = forward.</summary>
        public int Gear { get; private set; }

        /// <summary>Manual or automatic shifting.</summary>
        public TransmissionMode Mode { get; private set; }

        /// <summary>Net torque produced at the crank this step, after engine braking. [N*m]</summary>
        public float EngineTorqueNm { get; private set; }

        /// <summary>Torque transmitted across the clutch this step. [N*m]</summary>
        public float ClutchTorqueNm { get; private set; }

        /// <summary>Torque delivered to the driveshaft this step, after gearing. [N*m]</summary>
        public float OutputTorqueNm { get; private set; }

        /// <summary>True when the clutch cannot transmit everything the engine is asking of it.</summary>
        public bool ClutchSlipping { get; private set; }

        /// <summary>True while the rev limiter is cutting fuel.</summary>
        public bool RevLimiterActive { get; private set; }

        /// <summary>Engine speed. [rpm]</summary>
        public float EngineRpm => TougeMath.RadPerSecToRpm(EngineOmega);

        private float _limiterCutTimer;
        private float _autoShiftTimer;

        /// <summary>Put the powertrain in its starting state: idling, first gear selected.</summary>
        public void Initialize(CarSpec spec)
        {
            EngineOmega = TougeMath.RpmToRadPerSec(spec.engine.idleRpm);
            Gear = 1;
            Mode = spec.gearbox.defaultMode;
            _limiterCutTimer = 0f;
            _autoShiftTimer = 0f;
            EngineTorqueNm = 0f;
            ClutchTorqueNm = 0f;
            OutputTorqueNm = 0f;
        }

        /// <summary>
        /// Advance the powertrain by one physics step.
        /// </summary>
        /// <param name="spec">Car configuration.</param>
        /// <param name="input">Driver demand for this step.</param>
        /// <param name="drivenWheelOmega">Mean angular velocity of the driven wheels. [rad/s]</param>
        /// <param name="drivenWheelInertia">Combined rotational inertia of every driven wheel.
        /// [kg*m^2] Needed for the clutch's reduced-inertia synchronisation.</param>
        /// <param name="dt">Physics timestep. [s]</param>
        public void Step(CarSpec spec, IVehicleInput input, float drivenWheelOmega,
                         float drivenWheelInertia, float dt)
        {
            EngineSpec engine = spec.engine;
            GearboxSpec gearbox = spec.gearbox;

            UpdateGearSelection(spec, input, dt);

            // Total reduction from crank to wheel. Zero in neutral, negative in reverse.
            float totalRatio = gearbox.GetRatio(Gear) * gearbox.finalDrive;

            // ---- Engine torque ------------------------------------------------------------------
            float rpm = EngineRpm;
            float throttle = Mathf.Clamp01(input.Throttle);

            // Idle governor: a proportional controller that feeds in just enough throttle to hold
            // idle speed. Without it the engine drops to the stall floor and sits there.
            if (rpm < engine.idleRpm)
            {
                float idleDemand = (engine.idleRpm - rpm) * engine.idleGovernorGain;
                throttle = Mathf.Max(throttle, Mathf.Clamp01(idleDemand));
            }

            // Rev limiter: cut fuel for a fixed duration once the redline is crossed, and require the
            // revs to fall back through a hysteresis band before allowing another cut. Both parts are
            // needed or the limiter chatters on and off every step.
            if (_limiterCutTimer > 0f)
            {
                _limiterCutTimer -= dt;
                throttle = 0f;
                RevLimiterActive = true;
            }
            else if (rpm >= engine.redlineRpm)
            {
                _limiterCutTimer = engine.limiterCutDuration;
                throttle = 0f;
                RevLimiterActive = true;
            }
            else if (rpm < engine.redlineRpm - engine.limiterHysteresisRpm)
            {
                RevLimiterActive = false;
            }

            // Wide-open-throttle torque from the curve, scaled by how far the throttle is open.
            float wotTorque = engine.torqueCurveNm.Evaluate(rpm);

            // Engine braking: pumping and friction losses, always opposing rotation. This is the
            // term that produces lift-off oversteer on a rear-wheel-drive car.
            //
            // It is faded out as the throttle opens. A published torque curve is measured at the
            // crank with the throttle wide open, so it is ALREADY net of internal friction and
            // pumping - subtracting the full drag again on top of it double-counts. On this engine
            // that cost 40 N*m at 6000 rpm and 48 N*m at the limiter, i.e. 30-40% of peak torque,
            // taken out exactly where the car should be pulling hardest. It made the engine feel
            // like it died above 6000 rpm and blunted every gear.
            //
            // At a closed throttle the relief term is 1 and nothing changes, so engine braking and
            // the lift-off oversteer that depends on it are untouched.
            float dragRelief = 1f - Mathf.Clamp01(engine.engineBrakingThrottleRelief) * throttle;
            float brakingTorque = (engine.engineBrakingCoefficient * EngineOmega + engine.frictionTorqueNm)
                                  * dragRelief;

            EngineTorqueNm = wotTorque * throttle - brakingTorque;

            // ---- Clutch -------------------------------------------------------------------------
            float pedal = Mathf.Clamp01(input.Clutch);

            // An automatic shift opens the clutch for the duration of the torque cut. In manual mode
            // the player's own pedal is the only thing that opens it - miss the clutch and you get
            // the resulting shock load through the driveline, which is the point.
            if (_autoShiftTimer > 0f) pedal = 1f;

            float engagement = Mathf.Clamp01(gearbox.clutchEngagementCurve.Evaluate(pedal));
            float clutchCapacity = gearbox.clutchMaxTorqueNm * engagement;

            // ---- Auto-clutch --------------------------------------------------------------------
            // Engine speed is floored at stallRpm rather than modelled as a true stall, so without
            // this an idling engine in gear hands the driveline its FULL clutch capacity - 320 N*m
            // becomes 4441 N*m at the axle in first, which is a hard creep and a violent launch.
            //
            // The ceiling is a fraction of what the engine is actually producing, rising to full
            // capacity by autoClutchEngageRpm. Leaving a share of engine torque unclaimed is the
            // important part: that surplus is what accelerates the flywheel, so a launch builds revs
            // instead of bogging against a clutch that demands everything the engine makes.
            //
            // It yields entirely to the player: touching the clutch pedal disables it, and above the
            // engagement RPM it does nothing, so a clutch kick at 6000 rpm is untouched.
            if (gearbox.autoClutchEnabled && pedal < 0.01f)
            {
                float ramp = Mathf.InverseLerp(engine.idleRpm, gearbox.autoClutchEngageRpm, rpm);
                float atIdle = Mathf.Max(0f, EngineTorqueNm) * gearbox.autoClutchSlipShare;
                clutchCapacity = Mathf.Min(clutchCapacity, Mathf.Lerp(atIdle, clutchCapacity, ramp));
            }

            if (Mathf.Abs(totalRatio) < TougeMath.Epsilon)
            {
                // Neutral: the driveline is disconnected, so the engine sees only its own inertia.
                ClutchTorqueNm = 0f;
                ClutchSlipping = false;
            }
            else
            {
                // Engine speed the driveline is demanding, given the current wheel speed and gearing.
                float targetEngineOmega = drivenWheelOmega * totalRatio;
                float omegaError = EngineOmega - targetEngineOmega;

                // Torque that would synchronise the engine to the driveline within one step.
                //
                // The inertia here must be the REDUCED (harmonic) inertia of the two sides, not the
                // flywheel's alone:
                //
                //     I_reduced = (I_engine * I_wheels) / (I_wheels + ratio^2 * I_engine)
                //
                // derived by solving w_engine' = ratio * w_wheel' for the torque that applies to both
                // sides at once. Using the flywheel inertia by itself asks for the torque to drag the
                // ENGINE to the target while ignoring that the same torque drags the WHEELS toward it
                // as well - an overestimate of roughly 24x in first gear.
                //
                // That overestimate is not a small error. It made the coupling numerically unstable:
                // the speed error grew by ~21x per step, the clutch bang-banged between plus and minus
                // full capacity every 5 ms, and the driven tyres never settled anywhere near peak slip.
                // The car accelerated poorly and felt like it was on ice, in every gear.
                //
                // Written in this form the loop is stable at any ratio, because the same ratio^2 that
                // amplifies the wheel-side response also shrinks the inertia driving it.
                float wheelSideInertia = Mathf.Max(drivenWheelInertia, TougeMath.Epsilon);
                float ratioSquared = totalRatio * totalRatio;
                float reducedInertia = engine.flywheelInertia * wheelSideInertia /
                                       (wheelSideInertia + ratioSquared * engine.flywheelInertia);

                // Clamping to clutch capacity is still what turns an instantaneous pedal release into
                // a finite, tunable spike rather than an impulse.
                float syncTorque = omegaError * reducedInertia / Mathf.Max(dt, TougeMath.Epsilon);

                ClutchTorqueNm = Mathf.Clamp(syncTorque, -clutchCapacity, clutchCapacity);
                ClutchSlipping = Mathf.Abs(syncTorque) > clutchCapacity;
            }

            // ---- Integrate the engine -----------------------------------------------------------
            // Whatever the clutch takes away is what reaches the wheels; the remainder accelerates
            // the flywheel.
            float netEngineTorque = EngineTorqueNm - ClutchTorqueNm;
            EngineOmega += netEngineTorque / Mathf.Max(engine.flywheelInertia, TougeMath.Epsilon) * dt;

            // Floor the engine speed rather than modelling a true stall. Stalling is easy to add here
            // later (kill fuel below stallRpm until a restart) but it punishes experimentation while
            // the handling is still being tuned.
            EngineOmega = Mathf.Max(EngineOmega, TougeMath.RpmToRadPerSec(engine.stallRpm));

            // ---- Output to the driveshaft -------------------------------------------------------
            OutputTorqueNm = ClutchTorqueNm * totalRatio * gearbox.efficiency;
        }

        /// <summary>Handle mode toggling, manual shift requests and the automatic shift schedule.</summary>
        private void UpdateGearSelection(CarSpec spec, IVehicleInput input, float dt)
        {
            if (_autoShiftTimer > 0f) _autoShiftTimer -= dt;

            if (input.ToggleTransmissionModePressed)
                Mode = Mode == TransmissionMode.Manual ? TransmissionMode.Automatic : TransmissionMode.Manual;

            GearboxSpec gearbox = spec.gearbox;

            if (Mode == TransmissionMode.Manual)
            {
                // No torque cut in manual - the clutch pedal is the player's responsibility.
                if (input.ShiftUpPressed) SelectGear(Gear + 1, gearbox, false);
                if (input.ShiftDownPressed) SelectGear(Gear - 1, gearbox, false);
                return;
            }

            if (_autoShiftTimer > 0f) return;   // Mid-shift, leave the gear alone.

            float rpm = EngineRpm;
            if (Gear == 0)
            {
                SelectGear(1, gearbox, true);
            }
            else if (Gear >= 1 && rpm > gearbox.autoUpshiftRpm && Gear < gearbox.ForwardGearCount)
            {
                SelectGear(Gear + 1, gearbox, true);
            }
            else if (Gear > 1 && rpm < gearbox.autoDownshiftRpm)
            {
                SelectGear(Gear - 1, gearbox, true);
            }
        }

        /// <summary>Change gear, clamped to the available range.</summary>
        private void SelectGear(int gear, GearboxSpec gearbox, bool withTorqueCut)
        {
            int clamped = Mathf.Clamp(gear, -1, gearbox.ForwardGearCount);
            if (clamped == Gear) return;

            Gear = clamped;
            if (withTorqueCut) _autoShiftTimer = gearbox.shiftTimeSeconds;
        }

        /// <summary>Force a gear from outside the normal shift logic (respawns, mode setup).</summary>
        public void ForceGear(int gear, CarSpec spec) => SelectGear(gear, spec.gearbox, false);
    }
}
