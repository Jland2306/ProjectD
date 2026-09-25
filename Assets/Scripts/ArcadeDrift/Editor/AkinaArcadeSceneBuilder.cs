using Touge.Track;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Touge.ArcadeDrift.Editor
{
    /// <summary>
    /// Puts the arcade car on the Akina downhill course.
    ///
    /// WRITES A SEPARATE SCENE ON PURPOSE. AkinaDownhill.unity is left exactly as it is, still
    /// wired to the simulation car, so the two can be driven back to back on identical geometry -
    /// which is the only way to judge whether the arcade model actually holds up on a real course.
    /// Overwriting the original would answer the question and destroy the control at the same time.
    ///
    /// The track itself is reused rather than regenerated: the road, verge and guardrail meshes are
    /// saved assets, so opening the scene and saving it under a new name keeps the identical surface,
    /// identical barriers and identical corner radii.
    ///
    /// What gets stripped is everything bound to the simulation assemblies - the car prefab, the
    /// old camera rig, the debug HUD and the game mode. They are removed by GameObject NAME rather
    /// than by type, which is what lets this script do the job without its assembly referencing
    /// Touge.Vehicle at all.
    /// </summary>
    public static class AkinaArcadeSceneBuilder
    {
        private const string SourceScene = "Assets/Scenes/AkinaDownhill.unity";
        private const string TargetScene = "Assets/Scenes/AkinaArcade.unity";

        /// <summary>Lift above the simulation car's spawn, so the box starts clear of the road.</summary>
        private const float SpawnClearance = 0.6f;

        /// <summary>Objects belonging to the simulation build. Removed by name, never by type.</summary>
        private static readonly string[] SimulationObjects =
        {
            "PlayerCar",    // CarController, the whole simulation vehicle
            "IsoCamera",    // Touge.CameraRig, references the vehicle assembly
            "DebugHUD",     // Touge.UI, reads CarController telemetry
            "GameMode"      // Touge.Modes, drives the run against the simulation car
        };

        [MenuItem("Touge/Arcade Drift/Build Akina Arcade Scene", false, 41)]
        public static void BuildAkinaArcade()
        {
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(SourceScene) == null)
            {
                Debug.LogError($"[ArcadeDrift] {SourceScene} not found. Build the mountain pass first " +
                               "with Touge > Build Mountain Pass Scene.");
                return;
            }

            Scene scene = EditorSceneManager.OpenScene(SourceScene, OpenSceneMode.Single);

            // Take the simulation car's pose before deleting it, so both versions launch from the
            // same place on the same grade and the comparison is honest.
            Vector3 position = new Vector3(0f, 95f, 6f);
            Quaternion rotation = Quaternion.identity;

            GameObject player = Find(scene, "PlayerCar");
            if (player != null)
            {
                position = player.transform.position;
                rotation = player.transform.rotation;
            }
            else
            {
                Debug.LogWarning("[ArcadeDrift] No PlayerCar in the source scene; " +
                                 "falling back to a default start pose.");
            }

            foreach (string name in SimulationObjects)
            {
                GameObject go = Find(scene, name);
                if (go != null) Object.DestroyImmediate(go);
            }

            GameObject car = DriftLabSceneBuilder.CreateCar(
                DriftLabSceneBuilder.CreateMaterial("ArcadeDrift_Body", new Color(0.80f, 0.20f, 0.18f)),
                DriftLabSceneBuilder.CreateMaterial("ArcadeDrift_Nose", new Color(0.95f, 0.95f, 0.95f)));

            car.name = "ArcadeCar";
            car.transform.SetPositionAndRotation(position + Vector3.up * SpawnClearance, rotation);

            DriftLabSceneBuilder.CreateCamera(car.transform);
            ConfigureCameraForTouge();
            RetargetCheckpoints(car);

            // Scenery only reads the shared road meshes, so the course stays identical to the
            // simulation scene's - the two differ in what surrounds the road, never in the road.
            AkinaSceneryBuilder.Build(scene, System.IO.Path.GetFileNameWithoutExtension(TargetScene));

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, TargetScene);
            AssetDatabase.SaveAssets();

            Debug.Log($"[ArcadeDrift] {TargetScene} built. {SourceScene} is untouched - open either " +
                      "to drive the same course with the two different models.");
        }

        /// <summary>
        /// The drift lab's camera settings do not survive a real course. A pad can be watched from a
        /// pinned angle; a descent that turns through 180 degrees cannot, because half the corners
        /// end up behind the car. A partial yaw follow keeps the road on screen while still leaving
        /// the car visibly rotating against the view, which is what makes a drift readable.
        ///
        /// It follows the direction of TRAVEL rather than the nose, so going sideways does not swing
        /// the camera. Pulled back and zoomed out as well, because closing speed on a downhill needs
        /// more warning than a skidpad does.
        /// </summary>
        private static void ConfigureCameraForTouge()
        {
            ArcadeChaseCamera camera = Object.FindFirstObjectByType<ArcadeChaseCamera>();
            if (camera == null) return;

            camera.yawFollow = 0.75f;
            camera.yawSmoothTime = 1.1f;
            camera.viewSize = 19f;
            camera.distance = 30f;
            camera.pitch = 38f;
        }

        /// <summary>
        /// Point the existing checkpoint timer at the arcade car.
        ///
        /// CheckpointSequence filters by Rigidbody and has no reference to any vehicle code, so the
        /// course's own sector timing works unchanged - which means a run in this scene is directly
        /// comparable against a run in the simulation scene.
        /// </summary>
        private static void RetargetCheckpoints(GameObject car)
        {
            CheckpointSequence sequence = Object.FindFirstObjectByType<CheckpointSequence>();
            if (sequence == null) return;

            sequence.SetTrackedBody(car.GetComponent<Rigidbody>());
            EditorUtility.SetDirty(sequence);
        }

        private static GameObject Find(Scene scene, string name)
        {
            foreach (GameObject root in scene.GetRootGameObjects())
                if (root.name == name) return root;
            return null;
        }
    }
}
