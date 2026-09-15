using UnityEditor;
using UnityEngine;

namespace Touge.Editor
{
    /// <summary>
    /// Applies the physics settings the vehicle model assumes.
    ///
    /// These are already committed in ProjectSettings, so this menu item is mostly a way to restore
    /// them after a merge or to verify what the project is currently running.
    /// </summary>
    public static class TougeProjectSettings
    {
        /// <summary>
        /// 200 Hz. Stiff springs on a light car need a short step: at Unity's default 50 Hz a
        /// 28.5 kN/m spring can move far enough in one step to overshoot its own equilibrium, which
        /// shows up as a car that jitters at rest or launches off kerbs.
        /// </summary>
        private const float FixedTimestep = 0.005f;

        /// <summary>
        /// Caps how much simulation a single frame may try to catch up on. Without a low cap, one
        /// long editor hitch dumps dozens of physics steps into one frame and the car explodes.
        /// </summary>
        private const float MaximumAllowedTimestep = 0.1f;

        [MenuItem("Touge/Apply Recommended Physics Settings", false, 1)]
        public static void Apply()
        {
            Time.fixedDeltaTime = FixedTimestep;
            Time.maximumDeltaTime = MaximumAllowedTimestep;

            UnityEngine.Physics.defaultSolverIterations = 8;
            UnityEngine.Physics.defaultSolverVelocityIterations = 4;

            // Suspension casts must not be stopped by checkpoint volumes sitting over the road.
            UnityEngine.Physics.queriesHitTriggers = false;

            AssetDatabase.SaveAssets();

            Debug.Log($"[Touge] Physics settings applied: fixed timestep {FixedTimestep}s " +
                      $"({1f / FixedTimestep:F0} Hz), max allowed {MaximumAllowedTimestep}s, " +
                      "solver 8/4, queries ignore triggers.");
        }

        [MenuItem("Touge/Log Current Physics Settings", false, 2)]
        public static void LogCurrent()
        {
            Debug.Log($"[Touge] Fixed timestep {Time.fixedDeltaTime}s ({1f / Time.fixedDeltaTime:F0} Hz) | " +
                      $"max allowed {Time.maximumDeltaTime}s | " +
                      $"solver {UnityEngine.Physics.defaultSolverIterations}/" +
                      $"{UnityEngine.Physics.defaultSolverVelocityIterations} | " +
                      $"queriesHitTriggers {UnityEngine.Physics.queriesHitTriggers}");
        }
    }
}
