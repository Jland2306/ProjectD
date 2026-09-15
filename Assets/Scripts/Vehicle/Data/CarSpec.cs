using Touge.Core;
using UnityEngine;

namespace Touge.Vehicle.Data
{
    /// <summary>
    /// The complete tuning description of one car. Everything the physics needs lives here and
    /// nowhere else - no handling-relevant numbers are stored on the prefab or in code.
    ///
    /// LIVE TUNING: CarController re-reads this asset every physics step rather than caching it at
    /// Awake, so every value below can be edited in the Inspector while in Play Mode and takes
    /// effect immediately. Note the usual Unity caveat - changes made to the asset during Play Mode
    /// PERSIST after exiting, because a ScriptableObject asset is not part of the scene.
    ///
    /// Units are SI throughout: metres, kilograms, seconds, newtons, newton-metres, radians/second.
    /// RPM and km/h appear only in curves and UI, and are converted at the boundary.
    /// </summary>
    [CreateAssetMenu(fileName = "CarSpec", menuName = "Touge/Car Spec", order = 0)]
    public class CarSpec : ScriptableObject
    {
        [Tooltip("Name shown in UI and telemetry.")]
        public string displayName = "Touge Hatch";

        [Header("Chassis")]
        public ChassisSpec chassis = new ChassisSpec();

        [Header("Axles")]
        [Tooltip("Front axle geometry. positionZ should be positive (ahead of the rigidbody origin).")]
        public AxleSpec frontAxle = new AxleSpec
        {
            positionZ = 1.25f,
            steerable = true,
            maxBrakeTorqueNm = 1600f,
            handbrakeTorqueNm = 0f
        };

        [Tooltip("Rear axle geometry. positionZ should be negative (behind the rigidbody origin).")]
        public AxleSpec rearAxle = new AxleSpec
        {
            positionZ = -1.25f,
            steerable = false,
            maxBrakeTorqueNm = 1100f,
            handbrakeTorqueNm = 2800f   // High enough to fully lock the rear wheels - required for e-brake drifts.
        };

        [Header("Powertrain")]
        public EngineSpec engine = new EngineSpec();
        public GearboxSpec gearbox = new GearboxSpec();
        public DifferentialSpec differential = new DifferentialSpec();

        [Header("Suspension and tyres")]
        public SuspensionSpec suspension = new SuspensionSpec();
        public TireSpec tires = new TireSpec();

        [Header("Driver")]
        public SteeringSpec steering = new SteeringSpec();
        public AssistSpec assists = new AssistSpec();

        /// <summary>Wheelbase, derived from axle positions. [m]</summary>
        public float Wheelbase => Mathf.Abs(frontAxle.positionZ - rearAxle.positionZ);

        /// <summary>
        /// Static front weight distribution, 0-1, derived from where the centre of mass sits
        /// between the axles. A front-engine RWD hatch sits around 0.55.
        /// </summary>
        public float StaticFrontWeightBias
        {
            get
            {
                float wheelbase = Wheelbase;
                if (wheelbase < TougeMath.Epsilon) return 0.5f;
                // Distance from the REAR axle to the CoM, over the wheelbase, gives the front share.
                return Mathf.Clamp01((chassis.centerOfMassOffset.z - rearAxle.positionZ) / wheelbase);
            }
        }

        /// <summary>Axle spec for the requested end of the car.</summary>
        public AxleSpec GetAxle(bool isFront) => isFront ? frontAxle : rearAxle;

        /// <summary>
        /// Clamp values that would produce a divide-by-zero or a non-physical result.
        /// Called by Unity on Inspector edits, so live tuning cannot put the solver in a bad state.
        /// </summary>
        private void OnValidate()
        {
            chassis.massKg = Mathf.Max(1f, chassis.massKg);
            chassis.inertiaTensor = Vector3.Max(chassis.inertiaTensor, Vector3.one * 0.01f);

            frontAxle.wheelRadius = Mathf.Max(0.05f, frontAxle.wheelRadius);
            rearAxle.wheelRadius = Mathf.Max(0.05f, rearAxle.wheelRadius);
            frontAxle.wheelMass = Mathf.Max(0.5f, frontAxle.wheelMass);
            rearAxle.wheelMass = Mathf.Max(0.5f, rearAxle.wheelMass);
            frontAxle.trackWidth = Mathf.Max(0.1f, frontAxle.trackWidth);
            rearAxle.trackWidth = Mathf.Max(0.1f, rearAxle.trackWidth);

            engine.idleRpm = Mathf.Max(100f, engine.idleRpm);
            engine.redlineRpm = Mathf.Max(engine.idleRpm + 500f, engine.redlineRpm);
            engine.flywheelInertia = Mathf.Max(0.01f, engine.flywheelInertia);

            gearbox.finalDrive = Mathf.Max(0.1f, gearbox.finalDrive);
            gearbox.clutchMaxTorqueNm = Mathf.Max(1f, gearbox.clutchMaxTorqueNm);
            // An automatic that downshifts above its upshift point would oscillate between gears.
            gearbox.autoDownshiftRpm = Mathf.Min(gearbox.autoDownshiftRpm, gearbox.autoUpshiftRpm - 300f);

            tires.peakSlipRatio = Mathf.Max(0.01f, tires.peakSlipRatio);
            tires.peakSlipAngleDeg = Mathf.Clamp(tires.peakSlipAngleDeg, 0.5f, 45f);
            tires.referenceLoadN = Mathf.Max(1f, tires.referenceLoadN);
            tires.relaxationLength = Mathf.Max(0.01f, tires.relaxationLength);
            tires.lowSpeedBlendThreshold = Mathf.Max(0.1f, tires.lowSpeedBlendThreshold);

            ValidateAxleSuspension(suspension.front);
            ValidateAxleSuspension(suspension.rear);
        }

        private static void ValidateAxleSuspension(AxleSuspensionSpec axle)
        {
            axle.restLength = Mathf.Max(0.02f, axle.restLength);
            axle.maxTravel = Mathf.Clamp(axle.maxTravel, 0.01f, axle.restLength);
            axle.springStiffness = Mathf.Max(100f, axle.springStiffness);
            axle.bumpDamper = Mathf.Max(0f, axle.bumpDamper);
            axle.reboundDamper = Mathf.Max(0f, axle.reboundDamper);
        }
    }
}
