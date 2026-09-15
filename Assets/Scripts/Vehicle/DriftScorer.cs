using System;
using UnityEngine;

namespace Touge.Vehicle
{
    /// <summary>
    /// Detects sustained slides and accumulates a score for them.
    ///
    /// Phase 1 only surfaces this on the debug HUD - it is here now because drift angle and duration
    /// are the two numbers you need most while tuning, and because scoring wants to be driven by the
    /// physics step rather than bolted on later. The scoring rules themselves are deliberately plain;
    /// a game mode can layer combos, zones and multipliers on top without touching the detector.
    /// </summary>
    [Serializable]
    public class DriftScorer
    {
        [Tooltip("Body slip angle above which the car is considered to be drifting. [degrees]")]
        public float minDriftAngleDeg = 12f;

        [Tooltip("Slip angle beyond which the car counts as spun rather than drifting, ending the run. [degrees]")]
        public float maxDriftAngleDeg = 90f;

        [Tooltip("Minimum speed for a slide to count, so spinning on the spot scores nothing. [m/s]")]
        public float minSpeedMps = 8f;

        [Tooltip("How long the drift conditions may lapse before the run is banked and reset. [s]\n" +
                 "A short grace period lets a transition between two corners stay one continuous drift.")]
        public float breakGracePeriod = 0.35f;

        [Tooltip("Score per second at full slip angle and 100 km/h. Scales linearly with both.")]
        public float scoreRate = 100f;

        /// <summary>Current body slip angle. [degrees] Signed: positive = nose right of travel.</summary>
        public float DriftAngleDeg { get; private set; }

        /// <summary>How long the current drift has been running. [s]</summary>
        public float Duration { get; private set; }

        /// <summary>Score accumulated in the current drift.</summary>
        public float CurrentScore { get; private set; }

        /// <summary>Score banked from all completed drifts.</summary>
        public float TotalScore { get; private set; }

        /// <summary>True while a drift is in progress (including inside the grace period).</summary>
        public bool IsDrifting { get; private set; }

        private float _graceTimer;

        /// <summary>
        /// Advance the detector by one physics step.
        /// </summary>
        /// <param name="driftAngleDeg">Signed body slip angle. [degrees]</param>
        /// <param name="speedMps">Speed over ground. [m/s]</param>
        /// <param name="isGrounded">Whether any wheel is touching the road. Airborne does not count.</param>
        /// <param name="dt">Physics timestep. [s]</param>
        public void Step(float driftAngleDeg, float speedMps, bool isGrounded, float dt)
        {
            DriftAngleDeg = driftAngleDeg;

            float magnitude = Mathf.Abs(driftAngleDeg);
            bool qualifies = isGrounded
                             && speedMps >= minSpeedMps
                             && magnitude >= minDriftAngleDeg
                             && magnitude <= maxDriftAngleDeg;

            if (qualifies)
            {
                _graceTimer = breakGracePeriod;
                IsDrifting = true;
                Duration += dt;

                // Reward angle and speed together: a big angle held at speed is worth far more than
                // either alone. Normalised so scoreRate is "points per second at full angle, 100 km/h".
                float angleFactor = Mathf.InverseLerp(minDriftAngleDeg, maxDriftAngleDeg, magnitude);
                float speedFactor = speedMps / 27.78f;   // 100 km/h in m/s
                CurrentScore += scoreRate * angleFactor * speedFactor * dt;
                return;
            }

            if (!IsDrifting) return;

            // Conditions lapsed. Hold the run open briefly so a flick between two corners, or a
            // moment of air over a crest, does not break the chain.
            _graceTimer -= dt;
            if (_graceTimer > 0f)
            {
                Duration += dt;
                return;
            }

            Bank();
        }

        /// <summary>Close the current drift and add it to the running total.</summary>
        public void Bank()
        {
            TotalScore += CurrentScore;
            CurrentScore = 0f;
            Duration = 0f;
            IsDrifting = false;
            _graceTimer = 0f;
        }

        /// <summary>Clear everything, including the banked total. Used on respawn or restart.</summary>
        public void ResetAll()
        {
            CurrentScore = 0f;
            TotalScore = 0f;
            Duration = 0f;
            DriftAngleDeg = 0f;
            IsDrifting = false;
            _graceTimer = 0f;
        }
    }
}
