using System.Collections.Generic;
using Touge.Track;
using Touge.Vehicle;
using Touge.Vehicle.Data;
using Unity.Mathematics;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Splines;

namespace Touge.Editor
{
    /// <summary>
    /// Generates the downhill mountain pass: the spline, the road geometry, the checkpoints and the
    /// scene that holds them.
    ///
    /// The course is authored as a SEQUENCE OF DRIVING MOVES - "straight for 70 m at 10% grade",
    /// "right-hand arc, 15 m radius, 180 degrees" - rather than as a list of control points. Placing
    /// spline knots by hand to get a clean 15 m hairpin is fiddly and the result is hard to adjust;
    /// describing the corner by its radius makes the layout readable and lets a corner be retuned by
    /// changing one number.
    /// </summary>
    public static class TrackSplineFactory
    {
        private const string ScenePath = "Assets/Scenes/AkinaDownhill.unity";
        private const string MeshFolder = "Assets/Generated/Track";
        private const string TrackAssetPath = CarSpecFactory.SettingsFolder + "/AkinaDownhill.asset";

        /// <summary>Starting elevation of the pass. [m]</summary>
        private const float StartElevation = 95f;

        /// <summary>Roughly how far apart checkpoints are placed along the course. [m]</summary>
        private const float CheckpointSpacing = 120f;

        /// <summary>One authored move along the course.</summary>
        private struct CourseMove
        {
            /// <summary>Straight length, or 0 for an arc. [m]</summary>
            public float Length;

            /// <summary>Corner radius. [m] Zero for a straight.</summary>
            public float Radius;

            /// <summary>Corner sweep. [degrees] Positive turns right, negative turns left.</summary>
            public float AngleDeg;

            /// <summary>Downhill gradient over this move, as a percentage. 10 = a 10% descent.</summary>
            public float GradePercent;

            public string Label;

            public bool IsArc => Radius > 0.01f;

            public static CourseMove Straight(float length, float grade, string label) =>
                new CourseMove { Length = length, GradePercent = grade, Label = label };

            public static CourseMove Arc(float radius, float angleDeg, float grade, string label) =>
                new CourseMove { Radius = radius, AngleDeg = angleDeg, GradePercent = grade, Label = label };
        }

        /// <summary>
        /// The pass layout.
        ///
        /// Two hairpins, three linked esses, two fast sweepers and a long finishing straight, over
        /// roughly 730 m with about 85 m of descent (an average grade near 12%, which is steep but
        /// believable for a Japanese mountain pass).
        ///
        /// Headings run: 0 -> 60 -> -20 -> 70 -> 0 -> 180 (hairpin) -> 135 -> 205 -> 30. The hairpins
        /// double the road back on itself with 30 m and 32 m of lateral separation respectively,
        /// comfortably clear of the ~14 m footprint of road plus verge.
        /// </summary>
        private static readonly CourseMove[] Course =
        {
            CourseMove.Straight(70f, 10f, "Start straight"),
            CourseMove.Arc(90f, 60f, 11f, "Fast sweeper right"),
            CourseMove.Straight(40f, 10f, "Short chute"),
            CourseMove.Arc(28f, -80f, 13f, "Esse 1 - left"),
            CourseMove.Arc(26f, 90f, 13f, "Esse 2 - right"),
            CourseMove.Arc(30f, -70f, 13f, "Esse 3 - left"),
            CourseMove.Straight(50f, 10f, "Approach to hairpin 1"),
            CourseMove.Arc(15f, 180f, 15f, "HAIRPIN 1 - right"),
            CourseMove.Straight(60f, 10f, "Back straight"),
            CourseMove.Arc(110f, -45f, 11f, "Fast sweeper left"),
            CourseMove.Arc(24f, 70f, 13f, "Approach to hairpin 2"),
            CourseMove.Arc(16f, -175f, 15f, "HAIRPIN 2 - left"),
            CourseMove.Straight(90f, 10f, "Run to the line")
        };

        [MenuItem("Touge/Build Mountain Pass Scene", false, 21)]
        public static void BuildMountainPassScene()
        {
            if (!EditorUtility.DisplayDialog(
                    "Build Mountain Pass",
                    $"This creates (or replaces) {ScenePath} and regenerates the road meshes.\n\n" +
                    "Your CarSpec asset is NOT overwritten.\n\n" +
                    "Any unsaved changes in the current scene will be lost.",
                    "Build", "Cancel"))
            {
                return;
            }

            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

            int vehicleLayer = TougeSceneBuilder.EnsureLayer(TougeSceneBuilder.VehicleLayerName);
            int groundLayer = TougeSceneBuilder.EnsureLayer(TougeSceneBuilder.GroundLayerName);

            // Scene first, then assets. Changing scenes unloads anything not yet referenced, which
            // would leave these holding destroyed objects that serialize as null. See the same note
            // in TougeSceneBuilder.BuildTestScene.
            UnityEngine.SceneManagement.Scene scene =
                EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);

            CarSpec spec = CarSpecFactory.CreateOrLoadDefault();
            TougeSceneBuilder.ConfigureGroundMask(spec, vehicleLayer);

            InputActionAsset inputAsset =
                AssetDatabase.LoadAssetAtPath<InputActionAsset>(TougeSceneBuilder.InputAssetPath);
            if (inputAsset == null)
                Debug.LogWarning($"[Touge] Could not find {TougeSceneBuilder.InputAssetPath}. " +
                                 "Input will need assigning by hand.");

            // ---- Track --------------------------------------------------------------------------
            List<Vector3> knots = GenerateCoursePoints(out float totalLength, out float totalDrop);

            GameObject trackRoot = new GameObject("Track");
            SplineContainer container = trackRoot.AddComponent<SplineContainer>();
            PopulateSpline(container, knots);

            SplineRoadBuilder road = trackRoot.AddComponent<SplineRoadBuilder>();
            road.roadMaterial = TougeSceneBuilder.CreateMaterial("Greybox_Road", new Color(0.26f, 0.26f, 0.28f));
            road.vergeMaterial = TougeSceneBuilder.CreateMaterial("Greybox_Verge", new Color(0.22f, 0.26f, 0.18f));
            road.railMaterial = TougeSceneBuilder.CreateMaterial("Greybox_Rail", new Color(0.72f, 0.72f, 0.74f));
            road.Rebuild();

            TougeSceneBuilder.SetLayerRecursive(trackRoot, groundLayer);
            SaveGeneratedMeshes(road);

            // ---- Checkpoints --------------------------------------------------------------------
            CheckpointSequence sequence = trackRoot.AddComponent<CheckpointSequence>();
            List<Checkpoint> checkpoints = PlaceCheckpoints(road, trackRoot.transform);
            sequence.SetCheckpoints(checkpoints);

            // ---- Car, camera, HUD ---------------------------------------------------------------
            GameObject car = TougeSceneBuilder.BuildCar(spec, inputAsset, vehicleLayer);
            PlaceCarAtStart(car, road);

            TougeSceneBuilder.BuildCamera(car, inputAsset);
            TougeSceneBuilder.BuildHud(car.GetComponent<CarController>(), inputAsset);

            sequence.SetTrackedBody(car.GetComponent<Rigidbody>());

            // ---- Mode and track asset -----------------------------------------------------------
            GameObject modeRoot = new GameObject("GameMode");
            modeRoot.AddComponent<Touge.Modes.FreeDriveMode>();

            CreateTrackDefinition(totalLength, totalDrop, car.transform);

            CarSpecFactory.EnsureFolder("Assets/Scenes");
            EditorSceneManager.SaveScene(scene, ScenePath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log($"[Touge] Mountain pass built: {totalLength:F0} m, {totalDrop:F0} m of descent " +
                      $"({totalDrop / totalLength * 100f:F1}% average grade), {checkpoints.Count} checkpoints. " +
                      $"Scene saved to {ScenePath}.");
        }

        // -----------------------------------------------------------------------------------------
        // Course generation
        // -----------------------------------------------------------------------------------------

        /// <summary>
        /// Walk the authored moves and emit spline control points.
        ///
        /// Arc points are placed on the TRUE arc by rotating around the corner's centre, rather than
        /// by stepping along chords. Chord stepping would systematically cut every corner, making a
        /// 15 m hairpin come out noticeably tighter than 15 m.
        /// </summary>
        private static List<Vector3> GenerateCoursePoints(out float totalLength, out float totalDrop)
        {
            List<Vector3> points = new List<Vector3>();

            Vector3 position = new Vector3(0f, StartElevation, 0f);
            float headingDeg = 0f;
            totalLength = 0f;
            totalDrop = 0f;

            points.Add(position);

            foreach (CourseMove move in Course)
            {
                if (move.IsArc)
                {
                    float arcLength = move.Radius * Mathf.Abs(move.AngleDeg) * Mathf.Deg2Rad;
                    int steps = Mathf.Max(2, Mathf.CeilToInt(Mathf.Abs(move.AngleDeg) / 12f));

                    // The centre of the corner lies one radius to the side the car is turning toward.
                    Vector3 right = HeadingToRight(headingDeg);
                    float turnSign = Mathf.Sign(move.AngleDeg);
                    Vector3 center = position + right * (move.Radius * turnSign);
                    Vector3 relative = position - center;

                    for (int i = 1; i <= steps; i++)
                    {
                        float sweep = move.AngleDeg * i / steps;
                        Vector3 rotated = Quaternion.Euler(0f, sweep, 0f) * relative;

                        float travelled = arcLength * i / steps;
                        Vector3 point = center + rotated;
                        point.y = position.y - travelled * move.GradePercent * 0.01f;

                        points.Add(point);
                    }

                    position = points[points.Count - 1];
                    headingDeg += move.AngleDeg;
                    totalLength += arcLength;
                    totalDrop += arcLength * move.GradePercent * 0.01f;
                }
                else
                {
                    int steps = Mathf.Max(1, Mathf.CeilToInt(move.Length / 10f));
                    Vector3 direction = HeadingToDirection(headingDeg);

                    for (int i = 1; i <= steps; i++)
                    {
                        float travelled = move.Length * i / steps;
                        Vector3 point = position + direction * travelled;
                        point.y = position.y - travelled * move.GradePercent * 0.01f;
                        points.Add(point);
                    }

                    position = points[points.Count - 1];
                    totalLength += move.Length;
                    totalDrop += move.Length * move.GradePercent * 0.01f;
                }
            }

            return points;
        }

        /// <summary>Unit direction of travel for a compass heading, where 0 faces +Z.</summary>
        private static Vector3 HeadingToDirection(float headingDeg)
        {
            float radians = headingDeg * Mathf.Deg2Rad;
            return new Vector3(Mathf.Sin(radians), 0f, Mathf.Cos(radians));
        }

        /// <summary>Unit vector 90 degrees right of the heading.</summary>
        private static Vector3 HeadingToRight(float headingDeg)
        {
            float radians = headingDeg * Mathf.Deg2Rad;
            return new Vector3(Mathf.Cos(radians), 0f, -Mathf.Sin(radians));
        }

        /// <summary>Fill the container's spline with auto-smoothed knots.</summary>
        private static void PopulateSpline(SplineContainer container, List<Vector3> points)
        {
            Spline spline = container.Spline;
            spline.Clear();
            spline.Closed = false;   // Point to point: a touge run has a start and a finish, not laps.

            foreach (Vector3 point in points)
            {
                // AutoSmooth derives tangents from the neighbouring knots, which is what turns the
                // dense arc samples into a genuinely smooth curve.
                spline.Add(new BezierKnot(new float3(point.x, point.y, point.z)), TangentMode.AutoSmooth);
            }
        }

        // -----------------------------------------------------------------------------------------
        // Scene population
        // -----------------------------------------------------------------------------------------

        /// <summary>Place start, intermediate and finish triggers across the road.</summary>
        private static List<Checkpoint> PlaceCheckpoints(SplineRoadBuilder road, Transform parent)
        {
            List<Checkpoint> checkpoints = new List<Checkpoint>();
            IReadOnlyList<SplineRoadBuilder.RoadFrame> frames = road.Frames;
            if (frames.Count < 2) return checkpoints;

            GameObject group = new GameObject("Checkpoints");
            group.transform.SetParent(parent, false);

            // The start line sits a short way in so the car spawns behind it and crosses it properly.
            float startDistance = 12f;
            float finishDistance = road.TrackLength - 6f;

            List<float> distances = new List<float> { startDistance };
            for (float d = startDistance + CheckpointSpacing; d < finishDistance - CheckpointSpacing * 0.5f; d += CheckpointSpacing)
                distances.Add(d);
            distances.Add(finishDistance);

            for (int i = 0; i < distances.Count; i++)
            {
                SplineRoadBuilder.RoadFrame frame = FrameAtDistance(frames, distances[i]);

                CheckpointKind kind = i == 0 ? CheckpointKind.Start
                    : i == distances.Count - 1 ? CheckpointKind.Finish
                    : CheckpointKind.Intermediate;

                GameObject checkpointObject = new GameObject($"Checkpoint_{i}_{kind}");
                checkpointObject.transform.SetParent(group.transform, false);
                checkpointObject.transform.SetPositionAndRotation(
                    frame.Center + frame.Up * 2.5f,
                    Quaternion.LookRotation(frame.Forward, frame.Up));

                BoxCollider box = checkpointObject.AddComponent<BoxCollider>();
                box.isTrigger = true;
                // Wider than the road so a car running the verge still trips it; thin along the
                // direction of travel so the crossing point is unambiguous.
                box.size = new Vector3(road.roadWidth + 6f, 5f, 1.5f);

                Checkpoint checkpoint = checkpointObject.AddComponent<Checkpoint>();
                checkpoint.index = i;
                checkpoint.kind = kind;
                checkpoint.distanceAlongTrack = distances[i];

                checkpoints.Add(checkpoint);
            }

            return checkpoints;
        }

        private static SplineRoadBuilder.RoadFrame FrameAtDistance(
            IReadOnlyList<SplineRoadBuilder.RoadFrame> frames, float distance)
        {
            for (int i = 0; i < frames.Count; i++)
                if (frames[i].Distance >= distance) return frames[i];

            return frames[frames.Count - 1];
        }

        /// <summary>Put the car on the road at the start, facing down the hill.</summary>
        private static void PlaceCarAtStart(GameObject car, SplineRoadBuilder road)
        {
            IReadOnlyList<SplineRoadBuilder.RoadFrame> frames = road.Frames;
            if (frames.Count == 0) return;

            SplineRoadBuilder.RoadFrame start = FrameAtDistance(frames, 4f);

            // Lifted clear of the surface so the suspension settles rather than starting compressed.
            car.transform.SetPositionAndRotation(
                start.Center + start.Up * 0.6f,
                Quaternion.LookRotation(start.Forward, start.Up));
        }

        /// <summary>
        /// Persist the generated meshes as assets.
        ///
        /// Required, not optional: a mesh created at edit time and only referenced from a scene is not
        /// serialised with it, so without this the road would be missing the next time the scene is
        /// opened.
        /// </summary>
        internal static void SaveGeneratedMeshes(SplineRoadBuilder road)
        {
            CarSpecFactory.EnsureFolder(MeshFolder);
            SaveMesh(road.RoadMesh, "RoadSurface");
            SaveMesh(road.VergeMesh, "Verge");
            SaveMesh(road.RailMesh, "Guardrails");
        }

        private static void SaveMesh(Mesh mesh, string name)
        {
            if (mesh == null) return;

            string path = $"{MeshFolder}/{name}.asset";
            Mesh existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);

            if (existing != null)
            {
                // Overwrite in place so any existing scene references stay valid.
                existing.Clear();
                existing.indexFormat = mesh.indexFormat;
                existing.vertices = mesh.vertices;
                existing.triangles = mesh.triangles;
                existing.uv = mesh.uv;
                existing.RecalculateNormals();
                existing.RecalculateBounds();
                EditorUtility.SetDirty(existing);
                return;
            }

            AssetDatabase.CreateAsset(mesh, path);
        }

        private static void CreateTrackDefinition(float length, float drop, Transform carTransform)
        {
            TrackDefinition definition = AssetDatabase.LoadAssetAtPath<TrackDefinition>(TrackAssetPath);
            bool isNew = definition == null;

            if (isNew)
            {
                definition = ScriptableObject.CreateInstance<TrackDefinition>();
                CarSpecFactory.EnsureFolder(CarSpecFactory.SettingsFolder);
            }

            definition.displayName = "Akina Downhill";
            definition.description = "Point-to-point mountain descent. Two hairpins, three linked esses, " +
                                     "two fast sweepers.";
            definition.sceneName = "AkinaDownhill";
            definition.isPointToPoint = true;
            definition.lengthMeters = length;
            definition.elevationChangeMeters = -drop;
            definition.startGrid = new List<GridSlot>
            {
                new GridSlot
                {
                    position = carTransform.position,
                    eulerAngles = carTransform.eulerAngles
                }
            };

            if (isNew) AssetDatabase.CreateAsset(definition, TrackAssetPath);
            else EditorUtility.SetDirty(definition);
        }
    }
}
