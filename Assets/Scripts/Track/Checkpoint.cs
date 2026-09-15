using System;
using UnityEngine;

namespace Touge.Track
{
    /// <summary>What role a checkpoint plays in the course.</summary>
    public enum CheckpointKind
    {
        /// <summary>The start line. Crossing it begins the timer.</summary>
        Start,

        /// <summary>A split point along the course, used to validate that the route was actually driven.</summary>
        Intermediate,

        /// <summary>The finish line. Crossing it stops the timer.</summary>
        Finish
    }

    /// <summary>
    /// A trigger volume spanning the road, reporting anything that drives through it.
    ///
    /// Knows nothing about racing, timing or cars - it only announces that a collider entered.
    /// <see cref="CheckpointSequence"/> decides whether that crossing counts. Keeping the split here
    /// means a Time Trial, a VS race and a free-roam waypoint can all reuse the same triggers with
    /// completely different rules on top.
    /// </summary>
    [RequireComponent(typeof(BoxCollider))]
    [DisallowMultipleComponent]
    public class Checkpoint : MonoBehaviour
    {
        [Tooltip("Position in the course order. Start is 0 and the finish is the highest index.")]
        public int index;

        [Tooltip("Role this checkpoint plays.")]
        public CheckpointKind kind = CheckpointKind.Intermediate;

        [Tooltip("Distance along the spline where this checkpoint sits. [m] Informational, for UI and AI.")]
        public float distanceAlongTrack;

        /// <summary>Raised when any collider enters the volume. Arguments: this checkpoint, the collider.</summary>
        public event Action<Checkpoint, Collider> Entered;

        private void Reset()
        {
            // A checkpoint that is not a trigger would physically block the road, which is a
            // memorable but unhelpful way to find out it was configured wrongly.
            GetComponent<BoxCollider>().isTrigger = true;
        }

        private void OnTriggerEnter(Collider other) => Entered?.Invoke(this, other);

        private void OnDrawGizmos()
        {
            BoxCollider box = GetComponent<BoxCollider>();
            if (box == null) return;

            Gizmos.color = kind switch
            {
                CheckpointKind.Start => new Color(0.2f, 1f, 0.3f, 0.35f),
                CheckpointKind.Finish => new Color(1f, 0.85f, 0.1f, 0.35f),
                _ => new Color(0.3f, 0.6f, 1f, 0.2f)
            };

            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.DrawCube(box.center, box.size);
        }
    }
}
