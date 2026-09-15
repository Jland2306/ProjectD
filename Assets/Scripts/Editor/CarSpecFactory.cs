using System.IO;
using Touge.Vehicle.Data;
using UnityEditor;
using UnityEngine;

namespace Touge.Editor
{
    /// <summary>
    /// Creates the default car tuning asset.
    ///
    /// The asset is built in code rather than shipped as a hand-written .asset file because a
    /// ScriptableObject asset stores the GUID of its script, and that GUID is assigned by Unity on
    /// import - it cannot be authored reliably outside the editor.
    /// </summary>
    public static class CarSpecFactory
    {
        /// <summary>Folder that holds generated Touge configuration assets.</summary>
        public const string SettingsFolder = "Assets/Settings/Touge";

        /// <summary>Path of the default car spec asset.</summary>
        public const string DefaultSpecPath = SettingsFolder + "/DefaultCarSpec.asset";

        [MenuItem("Touge/Create Default Car Spec", false, 2)]
        public static void CreateDefaultSpecMenuItem()
        {
            CarSpec spec = CreateOrLoadDefault();
            Selection.activeObject = spec;
            EditorGUIUtility.PingObject(spec);
            Debug.Log($"[Touge] Default car spec ready at {DefaultSpecPath}", spec);
        }

        /// <summary>
        /// Load the default spec, creating it if it does not exist yet. Safe to call repeatedly - an
        /// existing asset is returned untouched, so a rebuild of the test scene never discards tuning
        /// work already done on it.
        /// </summary>
        public static CarSpec CreateOrLoadDefault()
        {
            CarSpec existing = AssetDatabase.LoadAssetAtPath<CarSpec>(DefaultSpecPath);
            if (existing != null) return existing;

            EnsureFolder(SettingsFolder);

            CarSpec spec = ScriptableObject.CreateInstance<CarSpec>();
            spec.displayName = "AE86-style Touge Hatch";

            // Every value comes from the field initialisers on the spec sections, which are already
            // tuned for a ~950 kg, ~130 hp, 5-speed front-engine RWD hatch. The only work left is to
            // smooth the authored curves.
            SmoothCurve(spec.engine.torqueCurveNm);
            SmoothCurve(spec.steering.speedSensitivityCurve);
            SmoothCurve(spec.gearbox.clutchEngagementCurve);

            AssetDatabase.CreateAsset(spec, DefaultSpecPath);
            AssetDatabase.SaveAssets();
            return spec;
        }

        /// <summary>
        /// Give every key smooth tangents.
        ///
        /// AnimationCurve's Keyframe constructor leaves tangents at zero, which makes the curve flat
        /// at every key and produces overshoot between them. An engine torque curve built that way
        /// has visible steps in it and will not feel right no matter how the numbers are tuned.
        /// </summary>
        private static void SmoothCurve(AnimationCurve curve)
        {
            if (curve == null) return;
            for (int i = 0; i < curve.length; i++) curve.SmoothTangents(i, 0f);
        }

        /// <summary>Create a project folder and any missing parents.</summary>
        public static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;

            string parent = Path.GetDirectoryName(path)?.Replace('\\', '/');
            string leaf = Path.GetFileName(path);

            if (!string.IsNullOrEmpty(parent) && !AssetDatabase.IsValidFolder(parent))
                EnsureFolder(parent);

            AssetDatabase.CreateFolder(parent, leaf);
        }
    }
}
