using Touge.CameraRig;
using Touge.UI;
using Touge.Vehicle;
using Touge.Vehicle.Data;
using Touge.Vehicle.Drivers;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Touge.Editor
{
    /// <summary>
    /// Builds the playable test scene from nothing: layers, ground, the car prefab, the camera rig
    /// and the debug HUD, all wired together.
    ///
    /// Exists because the editor cannot be driven from outside, so every reference this project needs
    /// has to be assignable in code. It is also genuinely useful on its own - the scene can be thrown
    /// away and rebuilt in a second, and tuning survives because the CarSpec asset is never
    /// overwritten once it exists.
    /// </summary>
    public static class TougeSceneBuilder
    {
        private const string ScenePath = "Assets/Scenes/TougeTest.unity";
        private const string PrefabFolder = "Assets/Prefabs";
        private const string CarPrefabPath = PrefabFolder + "/PlayerCar.prefab";
        private const string MaterialFolder = CarSpecFactory.SettingsFolder + "/Materials";
        internal const string InputAssetPath = "Assets/Scripts/Vehicle/Drivers/TougeControls.inputactions";

        internal const string VehicleLayerName = "Vehicle";
        internal const string GroundLayerName = "Ground";

        [MenuItem("Touge/Build Test Scene", false, 20)]
        public static void BuildTestScene()
        {
            if (!EditorUtility.DisplayDialog(
                    "Build Touge Test Scene",
                    $"This creates (or replaces) {ScenePath} and {CarPrefabPath}.\n\n" +
                    "Your CarSpec asset is NOT overwritten if it already exists, so tuning is safe.\n\n" +
                    "Any unsaved changes in the current scene will be lost.",
                    "Build", "Cancel"))
            {
                return;
            }

            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

            int vehicleLayer = EnsureLayer(VehicleLayerName);
            int groundLayer = EnsureLayer(GroundLayerName);

            // Create the scene BEFORE loading any assets. Changing scenes unloads unused assets, and
            // an asset loaded but not yet referenced by anything counts as unused - so loading first
            // leaves these variables holding DESTROYED objects. Assigning one of those to a
            // SerializedProperty silently writes a null reference, which is exactly as hard to
            // diagnose as it sounds.
            UnityEngine.SceneManagement.Scene scene =
                EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);

            CarSpec spec = CarSpecFactory.CreateOrLoadDefault();
            ConfigureGroundMask(spec, vehicleLayer);

            InputActionAsset inputAsset = AssetDatabase.LoadAssetAtPath<InputActionAsset>(InputAssetPath);
            if (inputAsset == null)
                Debug.LogWarning($"[Touge] Could not find {InputAssetPath}. Input will need assigning by hand.");

            BuildGround(groundLayer);
            GameObject car = BuildCar(spec, inputAsset, vehicleLayer);
            BuildCamera(car, inputAsset);
            BuildHud(car.GetComponent<CarController>(), inputAsset);

            CarSpecFactory.EnsureFolder("Assets/Scenes");
            EditorSceneManager.SaveScene(scene, ScenePath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log($"[Touge] Test scene built at {ScenePath}. Press Play to drive. " +
                      "F1 telemetry, F2 camera yaw mode, F3 projection, R respawn.");
        }

        // -----------------------------------------------------------------------------------------
        // Ground
        // -----------------------------------------------------------------------------------------

        /// <summary>
        /// A flat proving ground plus a few features worth having while tuning suspension: ramps to
        /// load and unload the springs, and a banked section to check that the tyre model behaves on
        /// a cambered surface. The real mountain pass replaces this in the track step.
        /// </summary>
        private static void BuildGround(int groundLayer)
        {
            GameObject root = new GameObject("TestGround");
            Material asphalt = CreateMaterial("Greybox_Asphalt", new Color(0.30f, 0.30f, 0.32f));
            Material marker = CreateMaterial("Greybox_Marker", new Color(0.55f, 0.50f, 0.35f));

            GameObject plane = GameObject.CreatePrimitive(PrimitiveType.Plane);
            plane.name = "Ground";
            plane.transform.SetParent(root.transform);
            plane.transform.localScale = Vector3.one * 30f;   // Unity's plane is 10 m, so 300 m square.
            SetLayerRecursive(plane, groundLayer);
            plane.GetComponent<Renderer>().sharedMaterial = asphalt;

            // Two ramps of different severity, for testing damping and landings.
            CreateBox(root.transform, "Ramp_Gentle", new Vector3(20f, 0f, 30f),
                      new Vector3(12f, 0.5f, 16f), new Vector3(6f, 0f, 0f), marker, groundLayer);
            CreateBox(root.transform, "Ramp_Steep", new Vector3(-20f, 0f, 30f),
                      new Vector3(12f, 0.5f, 10f), new Vector3(14f, 0f, 0f), marker, groundLayer);

            // A banked corner surface, to confirm suspension forces resolve correctly off-level.
            CreateBox(root.transform, "Bank", new Vector3(0f, 0f, -40f),
                      new Vector3(40f, 0.5f, 20f), new Vector3(0f, 0f, 12f), marker, groundLayer);

            // A kerb to bump one wheel at a time - the quickest way to see anti-roll behaviour.
            CreateBox(root.transform, "Kerb", new Vector3(35f, 0.06f, -10f),
                      new Vector3(2f, 0.12f, 30f), Vector3.zero, marker, groundLayer);
        }

        private static void CreateBox(Transform parent, string name, Vector3 position, Vector3 scale,
                                      Vector3 eulerAngles, Material material, int layer)
        {
            GameObject box = GameObject.CreatePrimitive(PrimitiveType.Cube);
            box.name = name;
            box.transform.SetParent(parent);
            box.transform.SetPositionAndRotation(position, Quaternion.Euler(eulerAngles));
            box.transform.localScale = scale;
            box.GetComponent<Renderer>().sharedMaterial = material;
            SetLayerRecursive(box, layer);
        }

        // -----------------------------------------------------------------------------------------
        // Car
        // -----------------------------------------------------------------------------------------

        internal static GameObject BuildCar(CarSpec spec, InputActionAsset inputAsset, int vehicleLayer)
        {
            Material bodyMaterial = CreateMaterial("Greybox_CarBody", new Color(0.75f, 0.20f, 0.18f));
            Material wheelMaterial = CreateMaterial("Greybox_Wheel", new Color(0.09f, 0.09f, 0.10f));

            GameObject car = new GameObject("PlayerCar");
            // Spawned above the ground so the springs settle into their static ride height on the
            // first few physics steps rather than starting already compressed.
            car.transform.position = new Vector3(0f, 0.6f, 0f);
            SetLayerRecursive(car, vehicleLayer);

            Rigidbody body = car.AddComponent<Rigidbody>();
            body.mass = spec.chassis.massKg;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;

            // The chassis collider only handles collisions with walls and other cars. It plays no
            // part in the suspension, which is why it can be a plain box sitting above the wheels.
            BoxCollider collider = car.AddComponent<BoxCollider>();
            collider.size = new Vector3(1.7f, 0.9f, 4.0f);
            collider.center = new Vector3(0f, 0.30f, 0f);

            GameObject visual = GameObject.CreatePrimitive(PrimitiveType.Cube);
            visual.name = "BodyMesh";
            visual.transform.SetParent(car.transform);
            visual.transform.localPosition = collider.center;
            visual.transform.localScale = collider.size;
            Object.DestroyImmediate(visual.GetComponent<BoxCollider>());
            visual.GetComponent<Renderer>().sharedMaterial = bodyMaterial;
            SetLayerRecursive(visual, vehicleLayer);

            // A nose marker, because a symmetrical box gives no clue which way the car is facing from
            // an isometric camera.
            GameObject nose = GameObject.CreatePrimitive(PrimitiveType.Cube);
            nose.name = "NoseMarker";
            nose.transform.SetParent(car.transform);
            nose.transform.localPosition = new Vector3(0f, 0.55f, 1.6f);
            nose.transform.localScale = new Vector3(0.5f, 0.2f, 0.8f);
            Object.DestroyImmediate(nose.GetComponent<BoxCollider>());
            nose.GetComponent<Renderer>().sharedMaterial = CreateMaterial("Greybox_Nose", Color.white);
            SetLayerRecursive(nose, vehicleLayer);

            Transform[] wheelVisuals =
            {
                CreateWheelVisual(car.transform, "WheelVisual_FL", spec, true, -1, wheelMaterial, vehicleLayer),
                CreateWheelVisual(car.transform, "WheelVisual_FR", spec, true, 1, wheelMaterial, vehicleLayer),
                CreateWheelVisual(car.transform, "WheelVisual_RL", spec, false, -1, wheelMaterial, vehicleLayer),
                CreateWheelVisual(car.transform, "WheelVisual_RR", spec, false, 1, wheelMaterial, vehicleLayer)
            };

            PlayerVehicleInput input = car.AddComponent<PlayerVehicleInput>();
            if (inputAsset != null) SetObjectField(input, "actionAsset", inputAsset);

            CarController controller = car.AddComponent<CarController>();
            SetObjectField(controller, "spec", spec);
            SetObjectArrayField(controller, "wheelVisuals", wheelVisuals);

            CarResetHandler resetHandler = car.AddComponent<CarResetHandler>();
            if (inputAsset != null) SetObjectField(resetHandler, "actionAsset", inputAsset);

            // Save as a prefab and keep the scene instance connected to it, so later phases can spawn
            // the same car for AI opponents and split-screen without rebuilding this hierarchy.
            CarSpecFactory.EnsureFolder(PrefabFolder);
            PrefabUtility.SaveAsPrefabAssetAndConnect(car, CarPrefabPath, InteractionMode.AutomatedAction);

            return car;
        }

        /// <summary>
        /// Build one wheel's visual hierarchy.
        ///
        /// The outer transform is what <see cref="CarController"/> drives; it is steered about the
        /// strut axis and spun about its own local X. Unity's cylinder primitive runs along its local
        /// Y, so the mesh is parented inside and rotated 90 degrees about Z to lay the axle over onto
        /// X. Doing it with a child rather than by baking the rotation into the driven transform keeps
        /// the spin maths in the controller simple.
        /// </summary>
        private static Transform CreateWheelVisual(Transform parent, string name, CarSpec spec,
                                                   bool isFront, int side, Material material, int layer)
        {
            AxleSpec axle = spec.GetAxle(isFront);
            AxleSuspensionSpec suspension = spec.suspension.ForAxle(isFront);

            GameObject pivot = new GameObject(name);
            pivot.transform.SetParent(parent);
            pivot.transform.localRotation = Quaternion.identity;

            // Place it at its resting position so the prefab looks right before Play is ever pressed.
            Vector3 mount = axle.GetMountPoint(side);
            pivot.transform.localPosition = mount - Vector3.up * suspension.restLength;

            GameObject mesh = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            mesh.name = "Mesh";
            mesh.transform.SetParent(pivot.transform);
            mesh.transform.localPosition = Vector3.zero;
            mesh.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
            // Unity's cylinder is radius 0.5 and height 2, so these scales give the real radius and
            // a 0.2 m tyre width.
            mesh.transform.localScale = new Vector3(axle.wheelRadius * 2f, 0.1f, axle.wheelRadius * 2f);
            Object.DestroyImmediate(mesh.GetComponent<CapsuleCollider>());
            mesh.GetComponent<Renderer>().sharedMaterial = material;

            SetLayerRecursive(pivot, layer);
            return pivot.transform;
        }

        // -----------------------------------------------------------------------------------------
        // Camera and HUD
        // -----------------------------------------------------------------------------------------

        internal static void BuildCamera(GameObject car, InputActionAsset inputAsset)
        {
            Camera camera = Object.FindFirstObjectByType<Camera>();
            if (camera == null)
            {
                GameObject cameraObject = new GameObject("Main Camera");
                cameraObject.tag = "MainCamera";
                camera = cameraObject.AddComponent<Camera>();
                cameraObject.AddComponent<AudioListener>();
            }

            camera.gameObject.name = "IsoCamera";
            camera.orthographic = true;
            camera.orthographicSize = 9f;
            camera.farClipPlane = 600f;

            if (!camera.TryGetComponent(out IsoFollowCamera follow))
                follow = camera.gameObject.AddComponent<IsoFollowCamera>();

            SetObjectField(follow, "target", car.transform);
            SetObjectField(follow, "targetBody", car.GetComponent<Rigidbody>());
            if (inputAsset != null) SetObjectField(follow, "actionAsset", inputAsset);
        }

        internal static void BuildHud(CarController car, InputActionAsset inputAsset)
        {
            GameObject hud = new GameObject("DebugHUD");
            DebugHud component = hud.AddComponent<DebugHud>();
            SetObjectField(component, "car", car);
            if (inputAsset != null) SetObjectField(component, "actionAsset", inputAsset);
        }

        // -----------------------------------------------------------------------------------------
        // Helpers
        // -----------------------------------------------------------------------------------------

        /// <summary>
        /// Point the suspension casts at everything EXCEPT the vehicle layer.
        ///
        /// This matters more than it looks: the strut mount sits inside the chassis collider, so if
        /// the car's own body is castable the wheels immediately "find ground" at zero distance and
        /// the car launches itself into the sky.
        /// </summary>
        internal static void ConfigureGroundMask(CarSpec spec, int vehicleLayer)
        {
            if (vehicleLayer < 0) return;

            LayerMask mask = ~(1 << vehicleLayer);
            if (spec.suspension.groundMask == mask) return;

            spec.suspension.groundMask = mask;
            EditorUtility.SetDirty(spec);
        }

        /// <summary>Find a layer by name, claiming the first free user slot if it does not exist.</summary>
        internal static int EnsureLayer(string layerName)
        {
            Object[] assets = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset");
            if (assets == null || assets.Length == 0)
            {
                Debug.LogWarning($"[Touge] Could not open TagManager to create layer '{layerName}'.");
                return -1;
            }

            SerializedObject tagManager = new SerializedObject(assets[0]);
            SerializedProperty layers = tagManager.FindProperty("layers");

            for (int i = 0; i < layers.arraySize; i++)
            {
                if (layers.GetArrayElementAtIndex(i).stringValue == layerName) return i;
            }

            // User layers start at 8; 0-7 are reserved by Unity even where they appear blank.
            for (int i = 8; i < layers.arraySize; i++)
            {
                SerializedProperty element = layers.GetArrayElementAtIndex(i);
                if (!string.IsNullOrEmpty(element.stringValue)) continue;

                element.stringValue = layerName;
                tagManager.ApplyModifiedPropertiesWithoutUndo();
                return i;
            }

            Debug.LogWarning($"[Touge] No free user layer available for '{layerName}'.");
            return -1;
        }

        internal static void SetLayerRecursive(GameObject target, int layer)
        {
            if (layer < 0) return;
            target.layer = layer;
            foreach (Transform child in target.transform) SetLayerRecursive(child.gameObject, layer);
        }

        internal static Material CreateMaterial(string name, Color colour)
        {
            string path = $"{MaterialFolder}/{name}.mat";
            Material existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing != null) return existing;

            CarSpecFactory.EnsureFolder(MaterialFolder);

            // URP's Lit shader. Falling back to the built-in standard shader keeps this working if
            // the project is ever switched off URP.
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) shader = Shader.Find("Standard");
            Material material = new Material(shader) { name = name };
            material.SetColor("_BaseColor", colour);
            material.SetFloat("_Smoothness", 0.15f);

            AssetDatabase.CreateAsset(material, path);
            return material;
        }

        /// <summary>
        /// Assign a private [SerializeField] from editor code.
        ///
        /// Going through SerializedObject rather than reflection means Unity records the change
        /// properly and it survives the prefab save.
        /// </summary>
        internal static void SetObjectField(Object target, string fieldName, Object value)
        {
            SerializedObject serialized = new SerializedObject(target);
            SerializedProperty property = serialized.FindProperty(fieldName);

            if (property == null)
            {
                Debug.LogWarning($"[Touge] Field '{fieldName}' not found on {target.GetType().Name}.");
                return;
            }

            // Unity's overloaded == reports a DESTROYED object as null, so this catches both a
            // genuinely missing asset and one that has been unloaded out from under us. Assigning
            // either writes a null reference that only shows up much later as a "no X assigned"
            // error at runtime, so fail loudly here instead.
            if (value == null)
            {
                Debug.LogError($"[Touge] Refusing to assign a null or destroyed object to " +
                               $"'{fieldName}' on {target.GetType().Name}. The asset was probably " +
                               "unloaded by a scene change before it could be referenced.");
                return;
            }

            property.objectReferenceValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            // Verify rather than assume: a silently dropped reference is the failure mode this whole
            // builder exists to prevent.
            serialized.Update();
            if (property.objectReferenceValue == null)
                Debug.LogError($"[Touge] Assignment to '{fieldName}' on {target.GetType().Name} " +
                               "did not stick.");
        }

        internal static void SetObjectArrayField(Object target, string fieldName, Object[] values)
        {
            SerializedObject serialized = new SerializedObject(target);
            SerializedProperty property = serialized.FindProperty(fieldName);

            if (property == null || !property.isArray)
            {
                Debug.LogWarning($"[Touge] Array field '{fieldName}' not found on {target.GetType().Name}.");
                return;
            }

            property.arraySize = values.Length;
            for (int i = 0; i < values.Length; i++)
                property.GetArrayElementAtIndex(i).objectReferenceValue = values[i];

            serialized.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
