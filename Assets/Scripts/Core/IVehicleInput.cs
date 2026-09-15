namespace Touge.Core
{
    /// <summary>
    /// The single seam between "something that decides what to do" and the car that does it.
    /// <see cref="Touge.Vehicle.CarController"/> knows nothing else about its driver, so a human,
    /// an AI racing line follower, or a ghost-replay playback head can all drive the same vehicle.
    ///
    /// Contract:
    ///  - Continuous axes are already normalised and clamped by the implementation.
    ///  - <see cref="Sample"/> is called once per rendered frame (Update).
    ///  - <see cref="ConsumeEdges"/> is called once per physics step (FixedUpdate) and latches
    ///    the momentary button flags to false, so a press between two physics steps is never
    ///    dropped and never applied twice. This is what keeps shifting frame-rate independent.
    /// </summary>
    public interface IVehicleInput
    {
        /// <summary>Accelerator, 0 = closed throttle, 1 = wide open.</summary>
        float Throttle { get; }

        /// <summary>Service brake, 0 = off, 1 = maximum line pressure.</summary>
        float Brake { get; }

        /// <summary>Steering demand, -1 = full left, +1 = full right. Raw: no rate limiting applied yet.</summary>
        float Steer { get; }

        /// <summary>Handbrake / e-brake, 0 = released, 1 = fully applied (locks the rear axle).</summary>
        float Handbrake { get; }

        /// <summary>
        /// Clutch pedal travel, 0 = fully engaged (engine coupled to gearbox), 1 = fully disengaged.
        /// Analog so that a fast 1 -> 0 transition produces a real torque spike (clutch kick).
        /// </summary>
        float Clutch { get; }

        /// <summary>True for exactly one physics step after an upshift request.</summary>
        bool ShiftUpPressed { get; }

        /// <summary>True for exactly one physics step after a downshift request.</summary>
        bool ShiftDownPressed { get; }

        /// <summary>True for exactly one physics step after a manual/automatic toggle request.</summary>
        bool ToggleTransmissionModePressed { get; }

        /// <summary>Poll the underlying device. Called from Update.</summary>
        void Sample(float deltaTime);

        /// <summary>Clear latched momentary flags. Called from FixedUpdate after they have been read.</summary>
        void ConsumeEdges();
    }
}
