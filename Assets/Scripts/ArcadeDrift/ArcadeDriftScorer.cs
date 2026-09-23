using UnityEngine;

namespace Touge.ArcadeDrift
{
    /// <summary>
    /// Scores drifts and draws the running chain across the top of the screen.
    ///
    /// Separate from ArcadeDriftCar deliberately. That file is a closed budget - twelve numbers and
    /// one job - and scoring is not handling. Nothing here feeds back into the physics: it reads
    /// SlipAngle, speed and grounded state and writes pixels, so deleting this file changes how the
    /// car drives by exactly nothing.
    ///
    /// THE CHAIN. Points accrue while you hold an angle and a multiplier climbs the longer you hold
    /// it, but both stay PENDING. Straighten up and they bank at the multiplier you earned. Spin,
    /// slow down, or hit something and the pending score is gone. That is the entire game of it: the
    /// longer you hold on, the more you stand to lose by holding on a moment longer.
    ///
    /// Linking matters as much as angle. The chain survives linkGrace seconds of being straight, so
    /// a clean transition from one corner into the next keeps the multiplier climbing across a whole
    /// descent instead of resetting at every exit.
    ///
    /// Lives on the car so it can see collisions. IMGUI on purpose - it needs no canvas, no font
    /// asset and no scene wiring, which keeps it as deletable as it claims to be.
    /// </summary>
    [RequireComponent(typeof(ArcadeDriftCar))]
    [DisallowMultipleComponent]
    public class ArcadeDriftScorer : MonoBehaviour
    {
        [Header("What counts as a drift")]
        [Tooltip("Slip angle the chain starts at. [deg] Scoring fades in from here, so the threshold " +
                 "is not a cliff.")]
        public float minAngle = 12f;

        [Tooltip("Slip angle worth full points. [deg] Past this there is no further angle bonus.")]
        public float bestAngle = 45f;

        [Tooltip("Slip angle treated as a spin. [deg] Past this the pending chain is lost.")]
        public float spinAngle = 110f;

        [Tooltip("The chain will not run or continue below this. [km/h]")]
        public float minSpeedKph = 25f;

        [Header("Scoring")]
        [Tooltip("Points per second at the best angle and the reference speed.")]
        public float pointsPerSecond = 120f;

        [Tooltip("Speed that scores at the full rate. [km/h] Faster scores proportionally more, so " +
                 "a fast sweeper is worth more than the same angle in a hairpin.")]
        public float referenceSpeedKph = 80f;

        [Tooltip("How fast the multiplier climbs while the chain runs. [per second]")]
        public float multiplierPerSecond = 0.4f;

        [Tooltip("Ceiling on the multiplier.")]
        public float maxMultiplier = 8f;

        [Tooltip("How long you may be straight before the chain banks. [s] This is the link window - " +
                 "long enough to cross between two corners without losing the run.")]
        public float linkGrace = 1.2f;

        [Header("Ending a run")]
        [Tooltip("Hitting something hard ends the chain and loses the pending score.")]
        public bool crashEndsChain = true;

        [Tooltip("Impact speed that counts as a crash. [m/s] Above the jostle of settling onto the " +
                 "road or brushing a verge, below any real barrier hit.")]
        public float crashSpeed = 5f;

        /// <summary>Banked score for the session.</summary>
        public float Total { get; private set; }

        /// <summary>Score earned by the current chain, not yet banked or multiplied.</summary>
        public float Pending { get; private set; }

        /// <summary>Multiplier the current chain has earned.</summary>
        public float Multiplier { get; private set; } = 1f;

        /// <summary>True while a chain is alive, including during the link grace window.</summary>
        public bool ChainRunning { get; private set; }

        private const float FlashTime = 1.3f;

        private ArcadeDriftCar _car;
        private float _graceLeft;
        private float _flashTimer;
        private string _flashText = string.Empty;
        private Color _flashColour = Color.white;
        private GUIStyle _big, _mid;

        private void Awake() => _car = GetComponent<ArcadeDriftCar>();

        private void Update()
        {
            float dt = Time.deltaTime;
            if (_flashTimer > 0f) _flashTimer -= dt;

            float slip = Mathf.Abs(_car.SlipAngle);

            // A spin is not a drift that got big - it is the run ending. Checked first so no frame of
            // a spin can still be scoring.
            if (slip > spinAngle)
            {
                EndChain("SPUN OUT", new Color(1f, 0.45f, 0.4f));
                return;
            }

            float speed = _car.SpeedKph;
            bool drifting = _car.IsGrounded && speed >= minSpeedKph && slip >= minAngle;

            if (drifting)
            {
                ChainRunning = true;
                _graceLeft = linkGrace;

                // Angle fades in from minAngle so the threshold is progressive, and pace rewards
                // carrying speed rather than parking the car sideways at walking pace.
                float angle = Mathf.InverseLerp(minAngle, bestAngle, slip);
                float pace = speed / Mathf.Max(referenceSpeedKph, 1f);

                Pending += pointsPerSecond * angle * pace * dt;
                Multiplier = Mathf.Min(Multiplier + multiplierPerSecond * dt, maxMultiplier);
                return;
            }

            if (!ChainRunning) return;

            // Straight, but the chain is still alive: this is the gap between two corners.
            _graceLeft -= dt;
            if (_graceLeft <= 0f) Bank();
        }

        /// <summary>
        /// A hard enough impact ends the run. The speed test is what stops the car's own box settling
        /// onto the road, or clipping a verge, from reading as a crash.
        /// </summary>
        private void OnCollisionEnter(Collision collision)
        {
            if (!crashEndsChain) return;
            if (collision.relativeVelocity.sqrMagnitude < crashSpeed * crashSpeed) return;

            EndChain("CRASHED", new Color(1f, 0.45f, 0.4f));
        }

        /// <summary>End the chain successfully: the pending score is multiplied and kept.</summary>
        public void Bank()
        {
            if (ChainRunning && Pending > 1f)
            {
                float earned = Pending * Multiplier;
                Total += earned;
                Flash($"+{earned:N0}", new Color(0.55f, 0.95f, 0.55f));
            }

            ClearChain();
        }

        /// <summary>Throw the chain away. Only flashes if there was something to lose.</summary>
        private void EndChain(string reason, Color colour)
        {
            if (ChainRunning && Pending > 1f) Flash(reason, colour);
            ClearChain();
        }

        private void ClearChain()
        {
            Pending = 0f;
            Multiplier = 1f;
            ChainRunning = false;
            _graceLeft = 0f;
        }

        /// <summary>Wipe the session, for starting a fresh run down the pass.</summary>
        public void ResetAll()
        {
            ClearChain();
            Total = 0f;
            _flashTimer = 0f;
        }

        private void Flash(string text, Color colour)
        {
            _flashText = text;
            _flashColour = colour;
            _flashTimer = FlashTime;
        }

        private void OnGUI()
        {
            if (_big == null)
            {
                _big = new GUIStyle(GUI.skin.label)
                {
                    fontSize = 46, fontStyle = FontStyle.Bold, alignment = TextAnchor.UpperCenter
                };
                _mid = new GUIStyle(GUI.skin.label)
                {
                    fontSize = 22, fontStyle = FontStyle.Bold, alignment = TextAnchor.UpperCenter
                };
            }

            float width = Screen.width;
            Color previous = GUI.color;

            GUI.Label(new Rect(0f, 10f, width, 28f), $"{Total:N0}", _mid);

            if (ChainRunning && Pending > 1f)
            {
                // Dim through the link window, so a chain about to expire is visibly about to expire.
                float alive = linkGrace > 0f ? Mathf.Clamp01(_graceLeft / linkGrace) : 1f;
                GUI.color = new Color(1f, 0.85f, 0.3f, Mathf.Lerp(0.35f, 1f, alive));
                GUI.Label(new Rect(0f, 38f, width, 58f), $"{Pending:N0}", _big);
                GUI.Label(new Rect(0f, 94f, width, 28f), $"x{Multiplier:0.0}", _mid);
            }
            else if (_flashTimer > 0f)
            {
                _flashColour.a = Mathf.Clamp01(_flashTimer / FlashTime);
                GUI.color = _flashColour;
                GUI.Label(new Rect(0f, 38f, width, 58f), _flashText, _big);
            }

            GUI.color = previous;
        }
    }
}
