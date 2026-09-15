using UnityEngine;

namespace Touge.Modes
{
    /// <summary>
    /// Lifecycle contract every game mode follows.
    ///
    /// Scaffolding for later phases (Time Trial, VS, Free Roam). Only <see cref="FreeDriveMode"/>
    /// implements it in Phase 1, but the hooks are fixed now so that adding a mode later is a new
    /// subclass rather than a refactor of whatever the vehicle and track code has grown into.
    ///
    /// The split exists because these four things happen at genuinely different times:
    ///   Setup  - references resolved, car spawned, track prepared. Nothing is moving yet.
    ///   Begin  - the moment control is handed to the player. Timers start here, not in Setup.
    ///   Tick   - per-frame rule evaluation while running.
    ///   End    - results are final. Runs exactly once, whether the mode completed or was abandoned.
    /// </summary>
    public abstract class GameModeBase : MonoBehaviour
    {
        /// <summary>Where the mode is in its lifecycle.</summary>
        public enum ModeState
        {
            /// <summary>Constructed but not yet prepared.</summary>
            Idle,

            /// <summary>Prepared and waiting to start.</summary>
            Ready,

            /// <summary>Running and ticking.</summary>
            Running,

            /// <summary>Complete. Results are final.</summary>
            Finished
        }

        /// <summary>Current lifecycle state.</summary>
        public ModeState State { get; private set; } = ModeState.Idle;

        [Tooltip("Run Setup and Begin automatically on Start. Turn off when a future front end " +
                 "needs to drive the lifecycle itself (countdowns, loading screens, grid sequences).")]
        [SerializeField] private bool autoStart = true;

        protected virtual void Start()
        {
            if (!autoStart) return;
            RunSetup();
            RunBegin();
        }

        protected virtual void Update()
        {
            if (State == ModeState.Running) Tick(Time.deltaTime);
        }

        /// <summary>Prepare the mode. Safe to call once; ignored afterwards.</summary>
        public void RunSetup()
        {
            if (State != ModeState.Idle) return;
            Setup();
            State = ModeState.Ready;
        }

        /// <summary>Hand control to the player and start any timers.</summary>
        public void RunBegin()
        {
            if (State != ModeState.Ready) return;
            State = ModeState.Running;
            Begin();
        }

        /// <summary>Finish the mode. Idempotent - calling it twice does not re-run End.</summary>
        public void RunEnd()
        {
            if (State == ModeState.Finished) return;
            State = ModeState.Finished;
            End();
        }

        /// <summary>Resolve references and place the world in its starting configuration.</summary>
        protected abstract void Setup();

        /// <summary>Called once at the instant the player gains control.</summary>
        protected abstract void Begin();

        /// <summary>Per-frame rule evaluation. Only called while running.</summary>
        /// <param name="deltaTime">Frame time. [s]</param>
        protected abstract void Tick(float deltaTime);

        /// <summary>Called once when the mode completes or is abandoned.</summary>
        protected abstract void End();
    }
}
