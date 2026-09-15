using System;
using System.Collections.Generic;
using UnityEngine;

namespace Touge.Track
{
    /// <summary>
    /// Tracks one vehicle's progress through an ordered list of checkpoints, and times the run.
    ///
    /// Groundwork for Time Trial. Phase 1 only uses it to prove the checkpoints are placed correctly
    /// and in the right order, but the ordering rules are here now because they are the part that is
    /// easy to get subtly wrong: checkpoints must be crossed IN SEQUENCE, so reversing back through
    /// one does not advance progress and cutting a hairpin does not skip a split.
    ///
    /// Deliberately has no reference to the vehicle assembly. It filters by Rigidbody, so it does not
    /// care whether the thing driving through is a player, an AI or a replay ghost.
    /// </summary>
    [DisallowMultipleComponent]
    public class CheckpointSequence : MonoBehaviour
    {
        [Tooltip("Checkpoints in course order. The scene builder fills this in; order is validated on enable.")]
        [SerializeField] private List<Checkpoint> checkpoints = new List<Checkpoint>();

        [Tooltip("Only this rigidbody's crossings count. Leave empty to accept any rigidbody, " +
                 "which is what you want while there is a single car in the scene.")]
        [SerializeField] private Rigidbody trackedBody;

        [Tooltip("Log every checkpoint crossing to the console. Useful while verifying placement.")]
        [SerializeField] private bool logCrossings = true;

        /// <summary>Raised when a checkpoint is legitimately crossed, in order.</summary>
        public event Action<Checkpoint> CheckpointPassed;

        /// <summary>Raised when the start line is crossed and timing begins.</summary>
        public event Action RunStarted;

        /// <summary>Raised when the finish line is crossed. Argument is the elapsed run time in seconds.</summary>
        public event Action<float> RunFinished;

        /// <summary>Index of the checkpoint the vehicle must cross next.</summary>
        public int NextIndex { get; private set; }

        /// <summary>Time since the start line was crossed. [s]</summary>
        public float ElapsedTime { get; private set; }

        /// <summary>True between the start and finish lines.</summary>
        public bool IsRunning { get; private set; }

        /// <summary>Fraction of checkpoints reached, 0-1. Useful for a progress bar or AI pacing.</summary>
        public float Progress => checkpoints.Count > 0 ? (float)NextIndex / checkpoints.Count : 0f;

        /// <summary>The ordered checkpoints.</summary>
        public IReadOnlyList<Checkpoint> Checkpoints => checkpoints;

        /// <summary>Replace the checkpoint list, used by the scene builder.</summary>
        public void SetCheckpoints(List<Checkpoint> ordered)
        {
            checkpoints = ordered;
            ResetProgress();
        }

        /// <summary>Assign the vehicle whose progress is tracked.</summary>
        public void SetTrackedBody(Rigidbody body) => trackedBody = body;

        private void OnEnable()
        {
            checkpoints.Sort((a, b) =>
            {
                if (a == null) return b == null ? 0 : 1;
                if (b == null) return -1;
                return a.index.CompareTo(b.index);
            });

            foreach (Checkpoint checkpoint in checkpoints)
                if (checkpoint != null) checkpoint.Entered += HandleCheckpointEntered;

            ResetProgress();
        }

        private void OnDisable()
        {
            foreach (Checkpoint checkpoint in checkpoints)
                if (checkpoint != null) checkpoint.Entered -= HandleCheckpointEntered;
        }

        private void Update()
        {
            if (IsRunning) ElapsedTime += Time.deltaTime;
        }

        private void HandleCheckpointEntered(Checkpoint checkpoint, Collider other)
        {
            // Colliders on a car are children of its rigidbody, so compare the attached body rather
            // than the collider's own GameObject.
            Rigidbody body = other.attachedRigidbody;
            if (body == null) return;
            if (trackedBody != null && body != trackedBody) return;

            // Out of order: either a checkpoint was skipped, or the car is reversing back through one
            // it already passed. Either way it does not advance progress.
            if (checkpoint.index != NextIndex) return;

            NextIndex++;

            if (checkpoint.kind == CheckpointKind.Start)
            {
                IsRunning = true;
                ElapsedTime = 0f;
                RunStarted?.Invoke();
                if (logCrossings) Debug.Log("[Touge] Run started.", this);
            }

            CheckpointPassed?.Invoke(checkpoint);

            if (logCrossings && checkpoint.kind == CheckpointKind.Intermediate)
                Debug.Log($"[Touge] Checkpoint {checkpoint.index} at {ElapsedTime:F2}s " +
                          $"({checkpoint.distanceAlongTrack:F0} m).", this);

            if (checkpoint.kind != CheckpointKind.Finish) return;

            IsRunning = false;
            RunFinished?.Invoke(ElapsedTime);
            if (logCrossings) Debug.Log($"[Touge] FINISH - {ElapsedTime:F3}s", this);
        }

        /// <summary>Clear progress and the timer, ready for another run.</summary>
        public void ResetProgress()
        {
            NextIndex = 0;
            ElapsedTime = 0f;
            IsRunning = false;
        }
    }
}
