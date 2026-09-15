using Touge.Vehicle;
using UnityEngine;

namespace Touge.Modes
{
    /// <summary>
    /// Drive with no rules, no timer and no end condition.
    ///
    /// The Phase 1 mode, and deliberately almost empty - its job is to prove the
    /// <see cref="GameModeBase"/> lifecycle is usable, not to add behaviour. It also gives the debug
    /// HUD and any future systems a single, well-defined place to ask "which car is the player
    /// driving?" rather than each one running its own scene search.
    /// </summary>
    public class FreeDriveMode : GameModeBase
    {
        [Tooltip("The player's car. Found automatically if left empty.")]
        [SerializeField] private CarController playerCar;

        /// <summary>The car the player is currently driving.</summary>
        public CarController PlayerCar => playerCar;

        protected override void Setup()
        {
            if (playerCar == null) playerCar = FindFirstObjectByType<CarController>();

            if (playerCar == null)
                Debug.LogWarning($"[Touge] {nameof(FreeDriveMode)} found no CarController in the scene.", this);
        }

        protected override void Begin()
        {
            // Nothing to gate: in free drive the player already has control from the first frame.
        }

        protected override void Tick(float deltaTime)
        {
            // No rules to evaluate. Time Trial will check checkpoints here; VS will check positions.
        }

        protected override void End()
        {
            // No results to report.
        }
    }
}
