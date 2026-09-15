using System;
using System.Collections.Generic;
using UnityEngine;

namespace Touge.Track
{
    /// <summary>
    /// One starting position and heading on the grid.
    ///
    /// Stored as position plus Euler angles rather than a <see cref="Pose"/> so the values are
    /// legible and hand-editable in the Inspector, which matters for a grid you will nudge by eye.
    /// </summary>
    [Serializable]
    public struct GridSlot
    {
        [Tooltip("World position of this grid slot.")]
        public Vector3 position;

        [Tooltip("World rotation in Euler angles. Only yaw normally matters.")]
        public Vector3 eulerAngles;

        /// <summary>The slot as a rotation quaternion.</summary>
        public Quaternion Rotation => Quaternion.Euler(eulerAngles);
    }

    /// <summary>
    /// Describes one mountain pass: what it is called, which scene holds it, and where cars start.
    ///
    /// Phase 1 has exactly one track, so nothing yet consumes this. It exists now because track
    /// SELECTION is coming, and retrofitting an asset like this after the fact usually means
    /// unpicking hard-coded scene names from half a dozen places.
    ///
    /// Deliberately NOT stored here: the spline itself, the road mesh, and the checkpoint objects.
    /// Those live in the scene, because they are scene objects - a ScriptableObject cannot hold a
    /// reference to one. The checkpoint sequence component finds them at load time.
    /// </summary>
    [CreateAssetMenu(fileName = "TrackDefinition", menuName = "Touge/Track Definition", order = 1)]
    public class TrackDefinition : ScriptableObject
    {
        [Header("Identity")]
        [Tooltip("Name shown in track selection and results.")]
        public string displayName = "Unnamed Pass";

        [Tooltip("Short description, for a selection screen.")]
        [TextArea(2, 4)]
        public string description;

        [Header("Scene")]
        [Tooltip("Name of the scene containing this track. Must be in Build Settings to load at runtime.\n" +
                 "Stored as a string rather than a SceneAsset because SceneAsset is editor-only and " +
                 "cannot be referenced from a runtime assembly.")]
        public string sceneName;

        [Header("Layout")]
        [Tooltip("Whether the pass runs point to point (a true touge downhill) or returns to its start.")]
        public bool isPointToPoint = true;

        [Tooltip("Approximate course length, for UI. [m]")]
        public float lengthMeters;

        [Tooltip("Total elevation change from start to finish. Negative for a downhill run. [m]")]
        public float elevationChangeMeters;

        [Tooltip("Starting positions, in grid order. Slot 0 is pole.")]
        public List<GridSlot> startGrid = new List<GridSlot>();

        [Header("Records")]
        [Tooltip("Target time used for medals or rival comparison. [s] Zero means unset.")]
        public float targetTimeSeconds;

        /// <summary>Grid slot for a given position, falling back to pole if the grid is short.</summary>
        public GridSlot GetGridSlot(int index)
        {
            if (startGrid == null || startGrid.Count == 0) return default;
            return startGrid[Mathf.Clamp(index, 0, startGrid.Count - 1)];
        }
    }
}
