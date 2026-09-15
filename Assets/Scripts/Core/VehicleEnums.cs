namespace Touge.Core
{
    /// <summary>Which axles receive engine torque.</summary>
    public enum DrivetrainLayout
    {
        RearWheelDrive,
        FrontWheelDrive,
        AllWheelDrive
    }

    /// <summary>
    /// How torque is split between the two wheels of a driven axle, and how much
    /// speed difference between them is resisted.
    /// </summary>
    public enum DifferentialType
    {
        /// <summary>Equal torque both sides. The unloaded wheel sets the limit, so it spins up first.</summary>
        Open,

        /// <summary>Clutch-pack LSD: resists wheel-speed difference with configurable power/coast lock.</summary>
        LimitedSlip,

        /// <summary>Welded. Both wheels forced to the same speed. Very drift-friendly, scrubs on tight turns.</summary>
        Locked
    }

    /// <summary>Shift strategy. Manual is the default; automatic exists so the car is approachable.</summary>
    public enum TransmissionMode
    {
        Manual,
        Automatic
    }

    /// <summary>How the isometric camera decides its yaw.</summary>
    public enum CameraFollowMode
    {
        /// <summary>Yaw is pinned to a constant world angle. Classic isometric, maximum readability.</summary>
        FixedYaw,

        /// <summary>Yaw drifts slowly toward the car's velocity heading so winding descents stay legible.</summary>
        SoftFollow
    }
}
