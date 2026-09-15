using Touge.Core;

namespace Touge.Vehicle.Drivers
{
    /// <summary>
    /// A non-MonoBehaviour <see cref="IVehicleInput"/> whose values are set directly in code.
    ///
    /// Present now because it makes the vehicle testable without a device - drive the car from an
    /// edit-mode test or hold a fixed throttle while tuning the tyre model. It is also the shape that
    /// the future AI driver and ghost-replay playback head will take, which is the point of routing
    /// everything through the interface in the first place.
    /// </summary>
    public class ConstantVehicleInput : IVehicleInput
    {
        public float Throttle { get; set; }
        public float Brake { get; set; }
        public float Steer { get; set; }
        public float Handbrake { get; set; }
        public float Clutch { get; set; }
        public bool ShiftUpPressed { get; set; }
        public bool ShiftDownPressed { get; set; }
        public bool ToggleTransmissionModePressed { get; set; }

        /// <summary>No device to poll - values are whatever was assigned.</summary>
        public void Sample(float deltaTime) { }

        /// <inheritdoc />
        public void ConsumeEdges()
        {
            ShiftUpPressed = false;
            ShiftDownPressed = false;
            ToggleTransmissionModePressed = false;
        }

        /// <summary>Release everything. Useful when handing control back or resetting a test.</summary>
        public void Clear()
        {
            Throttle = Brake = Steer = Handbrake = Clutch = 0f;
            ConsumeEdges();
        }
    }
}
