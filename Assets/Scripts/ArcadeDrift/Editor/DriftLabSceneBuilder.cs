using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Touge.ArcadeDrift.Editor
{
    /// <summary>
    /// Builds the drift lab scene.
    ///
    /// Generated in code rather than shipped as a .unity file for the same reason the rest of this
    /// project does it: a scene stores GUIDs that Unity assigns on import, so a hand-authored one
    /// cannot reliably reference a script or a material. Running this is reproducible; a committed
    /// scene full of guessed GUIDs is not.
    ///
    /// The lab is deliberately bare. A skidpad ring to circle, a slalom to transition through, and
    /// enough posts to actually perceive rotation and speed - nothing that needs learning before
    /// you can judge whether a grip number feels right.
    /// </summary>
    public static class DriftLabSceneBuilder
    {
        private const string ScenePath = "Assets/Scenes/DriftLab.unity";
        private const string SettingsFolder = "Assets/Settings/ArcadeDrift";
        private const string SlickPath = SettingsFolder + "/SlickBody.physicsMaterial";

        [MenuItem("Touge/Arcade Drift/Build Drift Lab Scene", false, 40)]
        public static void BuildDriftLab()
        {
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            Material tarmac = CreateMaterial("ArcadeDrift_Tarmac", new Color(0.24f, 0.24f, 0.26f));
            Material post = CreateMaterial("ArcadeDrift_Post", new Color(0.85f, 0.45f, 0.12f));
            Material body = CreateMaterial("ArcadeDrift_Body", new Color(0.80f, 0.20f, 0.18f));
            Material nose = CreateMaterial("ArcadeDrift_Nose", new Color(0.95f, 0.95f, 0.95f));

            CreateLight();
            CreatePad(tarmac);
            CreateSkidpadRing(post);
            CreateSlalom(post);

            GameObject car = CreateCar(body, nose);
            CreateCamera(car.transform);

            EnsureFolder("Assets/Scenes");
            EditorSceneManager.SaveScene(scene, ScenePath);
            AssetDatabase.SaveAssets();

            Debug.Log("[ArcadeDrift] Drift lab built at " + ScenePath +
                      ". Press Play; WASD to drive, space for the handbrake, R to respawn.");
        }

        private static void CreateLight()
        {
            GameObject go = new GameObject("Directional Light");
            Light light = go.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.1f;
            light.shadows = LightShadows.Soft;
            go.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
        }

        /// <summary>A single large flat pad. Flat on purpose: one variable at a time.</summary>
        private static void CreatePad(Material material)
        {
            GameObject pad = GameObject.CreatePrimitive(PrimitiveType.Cube);
            pad.name = "Pad";
            pad.transform.localScale = new Vector3(300f, 1f, 300f);
            pad.transform.position = new Vector3(0f, -0.5f, 0f);
            pad.GetComponent<Renderer>().sharedMaterial = material;
        }

        /// <summary>
        /// A ring of posts at 40 m. Circling it at a constant radius is the single most useful thing
        /// you can do while tuning: it isolates rearGrip from everything else.
        /// </summary>
        private static void CreateSkidpadRing(Material material)
        {
            GameObject parent = new GameObject("Skidpad");
            for (int i = 0; i < 24; i++)
            {
                float angle = i / 24f * Mathf.PI * 2f;
                Vector3 at = new Vector3(Mathf.Cos(angle) * 40f, 0f, Mathf.Sin(angle) * 40f);
                CreatePost(parent.transform, at, material, "Ring_" + i);
            }
        }

        /// <summary>A slalom down one side, for judging how fast the car changes direction.</summary>
        private static void CreateSlalom(Material material)
        {
            GameObject parent = new GameObject("Slalom");
            for (int i = 0; i < 8; i++)
            {
                Vector3 at = new Vector3(-100f + i * 18f, 0f, (i % 2 == 0) ? 8f : -8f);
                CreatePost(parent.transform, at, material, "Slalom_" + i);
            }
        }

        private static void CreatePost(Transform parent, Vector3 at, Material material, string name)
        {
            GameObject post = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            post.name = name;
            post.transform.SetParent(parent, false);
            post.transform.localScale = new Vector3(0.5f, 0.9f, 0.5f);
            post.transform.position = at + Vector3.up * 0.9f;
            post.GetComponent<Renderer>().sharedMaterial = material;

            // Triggers, not walls. Clipping a post while learning the car should cost nothing.
            post.GetComponent<Collider>().isTrigger = true;
        }

        internal static GameObject CreateCar(Material bodyMaterial, Material noseMaterial)
        {
            GameObject car = new GameObject("ArcadeCar");
            car.transform.position = new Vector3(0f, 1.0f, -40f);

            GameObject shell = GameObject.CreatePrimitive(PrimitiveType.Cube);
            shell.name = "Body";
            shell.transform.SetParent(car.transform, false);
            shell.transform.localScale = new Vector3(1.8f, 0.9f, 4.2f);
            shell.GetComponent<Renderer>().sharedMaterial = bodyMaterial;
            Object.DestroyImmediate(shell.GetComponent<BoxCollider>());

            // A white nose block, so which way the car is pointing is readable at a glance while it
            // is sideways. Judging a drift is impossible without it.
            GameObject marker = GameObject.CreatePrimitive(PrimitiveType.Cube);
            marker.name = "Nose";
            marker.transform.SetParent(car.transform, false);
            marker.transform.localScale = new Vector3(1.2f, 0.4f, 0.5f);
            marker.transform.localPosition = new Vector3(0f, 0.35f, 1.9f);
            marker.GetComponent<Renderer>().sharedMaterial = noseMaterial;
            Object.DestroyImmediate(marker.GetComponent<BoxCollider>());

            Rigidbody rb = car.AddComponent<Rigidbody>();
            rb.mass = 1200f;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;

            BoxCollider box = car.AddComponent<BoxCollider>();
            box.size = new Vector3(1.8f, 0.9f, 4.2f);
            box.sharedMaterial = EnsureSlickMaterial();

            car.AddComponent<ArcadeDriftCar>();
            car.AddComponent<ArcadeDriftScorer>();
            return car;
        }

        /// <summary>
        /// A frictionless material for the car's shell.
        ///
        /// This matters more than it sounds. The body is a box resting on the ground, and PhysX
        /// friction between the two would fight the axle model for control of how the car slides -
        /// so rearGrip would stop being the only thing that decides when the back steps out.
        /// Minimum combine means the PAIR is frictionless regardless of what the ground is wearing,
        /// which keeps that true on any surface you drop into the scene later.
        /// </summary>
        internal static PhysicsMaterial EnsureSlickMaterial()
        {
            PhysicsMaterial existing = AssetDatabase.LoadAssetAtPath<PhysicsMaterial>(SlickPath);
            if (existing != null) return existing;

            EnsureFolder(SettingsFolder);

            PhysicsMaterial slick = new PhysicsMaterial("SlickBody")
            {
                dynamicFriction = 0f,
                staticFriction = 0f,
                frictionCombine = PhysicsMaterialCombine.Minimum,
                bounciness = 0f,
                bounceCombine = PhysicsMaterialCombine.Minimum
            };

            AssetDatabase.CreateAsset(slick, SlickPath);
            return slick;
        }

        internal static void CreateCamera(Transform target)
        {
            GameObject go = new GameObject("DriftLabCamera");
            Camera camera = go.AddComponent<Camera>();
            camera.orthographic = true;
            camera.farClipPlane = 600f;
            go.tag = "MainCamera";

            ArcadeChaseCamera follow = go.AddComponent<ArcadeChaseCamera>();
            follow.target = target;
        }

        internal static Material CreateMaterial(string name, Color colour)
        {
            EnsureFolder(SettingsFolder);
            string path = SettingsFolder + "/" + name + ".mat";

            Material existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing != null) return existing;

            Shader shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            Material material = new Material(shader);
            material.SetColor("_BaseColor", colour);
            material.SetColor("_Color", colour);

            AssetDatabase.CreateAsset(material, path);
            return material;
        }

        internal static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;

            string parent = Path.GetDirectoryName(path)?.Replace('\\', '/');
            string leaf = Path.GetFileName(path);
            if (!string.IsNullOrEmpty(parent) && !AssetDatabase.IsValidFolder(parent)) EnsureFolder(parent);

            AssetDatabase.CreateFolder(parent, leaf);
        }
    }
}
