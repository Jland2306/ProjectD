using Touge.Track;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Touge.Editor
{
    /// <summary>
    /// Adds a rebuild button to <see cref="SplineRoadBuilder"/>.
    ///
    /// The road does not regenerate on its own when the spline changes, which is deliberate: on a
    /// long course the rebuild takes long enough that doing it on every knot drag would make editing
    /// unpleasant. Drag knots freely, then press the button once.
    /// </summary>
    [CustomEditor(typeof(SplineRoadBuilder))]
    public class SplineRoadBuilderEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            SplineRoadBuilder builder = (SplineRoadBuilder)target;

            EditorGUILayout.Space();

            if (builder.TrackLength > 0f)
            {
                EditorGUILayout.HelpBox(
                    $"Length {builder.TrackLength:F0} m   |   {builder.Frames.Count} cross-sections   " +
                    $"|   {builder.sampleSpacing:F1} m spacing",
                    MessageType.None);
            }

            EditorGUILayout.HelpBox(
                "Geometry is not regenerated automatically when the spline changes. " +
                "Move knots, then rebuild.",
                MessageType.Info);

            if (builder.railPhysicsMaterial == null)
            {
                EditorGUILayout.HelpBox(
                    "The guardrails have no physics material, so they are running on the project " +
                    "default: full friction and no bounce. That is what makes a wall grab the car " +
                    "and hold it. Use Touge > Fix Track Barrier Physics, or rebuild.",
                    MessageType.Warning);
            }

            if (GUILayout.Button("Rebuild Road", GUILayout.Height(28f)))
            {
                // Before the geometry, not after: the rebuild reads railPhysicsMaterial when it
                // assigns the guardrail collider, so a track built without one would otherwise come
                // back with bare barriers again.
                TrackSplineFactory.ApplyBarrierPhysics(builder);

                builder.Rebuild();

                // The meshes must be written back to their assets, or the scene will reference
                // objects that are not serialised anywhere and the road disappears on reload.
                TrackSplineFactory.SaveGeneratedMeshes(builder);

                EditorSceneManager.MarkSceneDirty(builder.gameObject.scene);
                AssetDatabase.SaveAssets();

                Debug.Log($"[Touge] Road rebuilt: {builder.TrackLength:F0} m, " +
                          $"{builder.Frames.Count} cross-sections.", builder);
            }
        }
    }
}
