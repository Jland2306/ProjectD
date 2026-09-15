using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Splines;

namespace Touge.Track
{
    /// <summary>
    /// Generates road, verge and guardrail meshes along a <see cref="SplineContainer"/>.
    ///
    /// The spline is the single source of truth for the course. Everything else - the driving
    /// surface, the barriers that stop you leaving it, the ground falling away at the edges, and the
    /// checkpoint placement - is derived from it, so reshaping a corner is a matter of dragging a
    /// knot and rebuilding rather than editing geometry by hand.
    ///
    /// SAMPLING: the spline is walked at even ARC LENGTH intervals, not at even values of the
    /// normalised parameter t. Those are not the same thing - t advances faster through tight curves
    /// than through straights, so sampling in t directly would give sparse, faceted hairpins and
    /// wastefully dense straights, and the UVs would stretch and compress along the road.
    ///
    /// BANKING is derived from curvature: the tighter the corner, the more the surface tilts into it.
    /// </summary>
    [RequireComponent(typeof(SplineContainer))]
    [DisallowMultipleComponent]
    public class SplineRoadBuilder : MonoBehaviour
    {
        [Header("Road surface")]
        [Tooltip("Full width of the driving surface. [m] A real touge is narrow - 6-7 m is about right.")]
        public float roadWidth = 6.5f;

        [Tooltip("Distance between generated cross-sections. [m]\n" +
                 "Smaller = smoother hairpins and more triangles. 2 m is a good greybox compromise.")]
        public float sampleSpacing = 2f;

        [Tooltip("How many times the road texture repeats per metre travelled. Affects UV.y only.")]
        public float uvTilingPerMeter = 0.12f;

        [Header("Banking")]
        [Tooltip("Tilt the surface into corners, based on how tight they are.")]
        public bool autoBankFromCurvature = true;

        [Tooltip("Degrees of bank per degree-per-metre of curvature.\n" +
                 "A 15 m hairpin curves at about 3.8 deg/m, so a gain of 2 gives it ~7.6 degrees of bank.")]
        public float bankGain = 2f;

        [Tooltip("Upper limit on bank angle. [degrees] Real mountain roads rarely exceed 8-10.")]
        public float maxBankDeg = 8f;

        [Tooltip("Smoothing passes over the banking profile, so bank eases in and out of corners " +
                 "instead of switching on at the corner entry.")]
        [Range(0, 20)]
        public int bankSmoothingPasses = 8;

        [Header("Verge")]
        [Tooltip("Width of the ground shoulder either side of the road. [m]")]
        public float vergeWidth = 4f;

        [Tooltip("How far the verge falls away from the road edge. [m]\n" +
                 "Makes the road read as a ledge on a mountainside from the isometric camera.")]
        public float vergeDrop = 5f;

        [Header("Guardrails")]
        public bool generateGuardrails = true;

        [Tooltip("Height of the barrier above the road surface. [m]")]
        public float railHeight = 0.75f;

        [Tooltip("Thickness of the barrier. [m] Non-zero so it is a solid volume rather than a plane - " +
                 "a zero-thickness mesh collider can be tunnelled through at speed.")]
        public float railThickness = 0.2f;

        [Tooltip("How far the barrier sits outside the road edge. [m]")]
        public float railOffset = 0.15f;

        [Header("Materials")]
        public Material roadMaterial;
        public Material vergeMaterial;
        public Material railMaterial;

        /// <summary>The generated driving surface.</summary>
        public Mesh RoadMesh { get; private set; }

        /// <summary>The generated verge/cliff skirt.</summary>
        public Mesh VergeMesh { get; private set; }

        /// <summary>The generated guardrails.</summary>
        public Mesh RailMesh { get; private set; }

        /// <summary>Total arc length of the spline. [m] Valid after a rebuild.</summary>
        public float TrackLength { get; private set; }

        /// <summary>One sampled cross-section of the road.</summary>
        public struct RoadFrame
        {
            /// <summary>World-space centreline point.</summary>
            public Vector3 Center;

            /// <summary>Unit vector across the road, pointing right relative to travel.</summary>
            public Vector3 Right;

            /// <summary>Unit vector out of the road surface, after banking.</summary>
            public Vector3 Up;

            /// <summary>Unit vector along the direction of travel.</summary>
            public Vector3 Forward;

            /// <summary>Distance travelled along the spline to reach this frame. [m]</summary>
            public float Distance;
        }

        private readonly List<RoadFrame> _frames = new List<RoadFrame>();

        /// <summary>The cross-sections produced by the last rebuild. Used to place checkpoints.</summary>
        public IReadOnlyList<RoadFrame> Frames => _frames;

        /// <summary>
        /// Regenerate all geometry from the current spline.
        /// Safe to call repeatedly; existing child meshes are replaced.
        /// </summary>
        public void Rebuild()
        {
            SplineContainer container = GetComponent<SplineContainer>();
            if (container == null || container.Spline == null || container.Spline.Count < 2)
            {
                Debug.LogWarning($"[Touge] {name}: spline needs at least two knots to build a road.", this);
                return;
            }

            BuildFrames(container.Spline);
            if (_frames.Count < 2) return;

            RoadMesh = BuildRoadMesh();
            VergeMesh = BuildVergeMesh();
            RailMesh = generateGuardrails ? BuildRailMesh() : null;

            AssignMesh("RoadSurface", RoadMesh, roadMaterial, true);
            AssignMesh("Verge", VergeMesh, vergeMaterial, true);
            AssignMesh("Guardrails", RailMesh, railMaterial, true);
        }

        // -----------------------------------------------------------------------------------------
        // Sampling
        // -----------------------------------------------------------------------------------------

        /// <summary>
        /// Walk the spline at even arc-length intervals and build a cross-section frame at each step.
        /// </summary>
        private void BuildFrames(Spline spline)
        {
            _frames.Clear();

            // Arc-length lookup table: sample densely in t, accumulate real distance, then invert the
            // table to convert a target distance back into a t value.
            const int lutResolution = 1024;
            float[] cumulative = new float[lutResolution + 1];
            Vector3 previous = transform.TransformPoint(spline.EvaluatePosition(0f));

            for (int i = 1; i <= lutResolution; i++)
            {
                Vector3 point = transform.TransformPoint(spline.EvaluatePosition((float)i / lutResolution));
                cumulative[i] = cumulative[i - 1] + Vector3.Distance(previous, point);
                previous = point;
            }

            TrackLength = cumulative[lutResolution];
            if (TrackLength <= 0.01f) return;

            int steps = Mathf.Max(2, Mathf.CeilToInt(TrackLength / Mathf.Max(sampleSpacing, 0.1f)));

            // First pass: geometry without banking.
            List<float> curvatures = new List<float>(steps + 1);
            Vector3 previousForward = Vector3.zero;

            for (int i = 0; i <= steps; i++)
            {
                float distance = TrackLength * i / steps;
                float t = DistanceToT(cumulative, lutResolution, distance);

                Vector3 center = transform.TransformPoint(spline.EvaluatePosition(t));
                Vector3 forward = transform.TransformDirection(spline.EvaluateTangent(t));

                if (forward.sqrMagnitude < 1e-8f) forward = previousForward.sqrMagnitude > 0f ? previousForward : Vector3.forward;
                forward.Normalize();

                // Build the frame from world up rather than carrying a rotation-minimising frame
                // along the spline. A road is always "up relative to gravity" - it never rolls past
                // vertical - so this is both correct here and immune to drift over a long course.
                Vector3 right = Vector3.Cross(Vector3.up, forward);
                if (right.sqrMagnitude < 1e-6f) right = Vector3.right;   // Spline pointing straight up.
                right.Normalize();

                Vector3 up = Vector3.Cross(forward, right).normalized;

                // Signed curvature in degrees per metre. Positive = turning right.
                float curvature = 0f;
                if (i > 0 && sampleSpacing > 0f)
                {
                    float turn = Vector3.SignedAngle(previousForward, forward, Vector3.up);
                    curvature = turn / (TrackLength / steps);
                }

                curvatures.Add(curvature);
                previousForward = forward;

                _frames.Add(new RoadFrame
                {
                    Center = center,
                    Right = right,
                    Up = up,
                    Forward = forward,
                    Distance = distance
                });
            }

            if (!autoBankFromCurvature) return;

            ApplyBanking(curvatures);
        }

        /// <summary>
        /// Tilt each cross-section into its corner.
        ///
        /// The raw per-sample curvature is noisy and steps abruptly at corner entry, so it is smoothed
        /// with a few box-blur passes first. Without that the road visibly creases where a straight
        /// meets a corner.
        /// </summary>
        private void ApplyBanking(List<float> curvatures)
        {
            for (int pass = 0; pass < bankSmoothingPasses; pass++)
            {
                float[] smoothed = new float[curvatures.Count];
                for (int i = 0; i < curvatures.Count; i++)
                {
                    int previous = Mathf.Max(0, i - 1);
                    int next = Mathf.Min(curvatures.Count - 1, i + 1);
                    smoothed[i] = (curvatures[previous] + curvatures[i] + curvatures[next]) / 3f;
                }
                for (int i = 0; i < curvatures.Count; i++) curvatures[i] = smoothed[i];
            }

            for (int i = 0; i < _frames.Count; i++)
            {
                // Negative sign banks INTO the corner: a right-hand turn (positive curvature) needs
                // its right-hand edge lowered, like a velodrome.
                float bank = Mathf.Clamp(-curvatures[i] * bankGain, -maxBankDeg, maxBankDeg);

                RoadFrame frame = _frames[i];
                Quaternion roll = Quaternion.AngleAxis(bank, frame.Forward);
                frame.Right = roll * frame.Right;
                frame.Up = roll * frame.Up;
                _frames[i] = frame;
            }
        }

        /// <summary>Invert the arc-length table: given a distance along the spline, find its t.</summary>
        private static float DistanceToT(float[] cumulative, int resolution, float distance)
        {
            if (distance <= 0f) return 0f;
            if (distance >= cumulative[resolution]) return 1f;

            int low = 0;
            int high = resolution;
            while (high - low > 1)
            {
                int mid = (low + high) / 2;
                if (cumulative[mid] < distance) low = mid; else high = mid;
            }

            // Linear interpolation inside the bracketing pair is accurate enough at this resolution.
            float span = cumulative[high] - cumulative[low];
            float fraction = span > 1e-6f ? (distance - cumulative[low]) / span : 0f;
            return (low + fraction) / resolution;
        }

        // -----------------------------------------------------------------------------------------
        // Mesh building
        // -----------------------------------------------------------------------------------------

        private Mesh BuildRoadMesh()
        {
            int count = _frames.Count;
            Vector3[] vertices = new Vector3[count * 2];
            Vector2[] uvs = new Vector2[count * 2];
            int[] triangles = new int[(count - 1) * 6];

            float half = roadWidth * 0.5f;

            for (int i = 0; i < count; i++)
            {
                RoadFrame frame = _frames[i];
                // Vertices are stored relative to this transform so the mesh is reusable and the
                // GameObject can still be moved afterwards.
                vertices[i * 2] = transform.InverseTransformPoint(frame.Center - frame.Right * half);
                vertices[i * 2 + 1] = transform.InverseTransformPoint(frame.Center + frame.Right * half);

                float v = frame.Distance * uvTilingPerMeter;
                uvs[i * 2] = new Vector2(0f, v);
                uvs[i * 2 + 1] = new Vector2(1f, v);
            }

            for (int i = 0; i < count - 1; i++)
            {
                int baseIndex = i * 2;
                int t = i * 6;
                triangles[t] = baseIndex;
                triangles[t + 1] = baseIndex + 2;
                triangles[t + 2] = baseIndex + 1;
                triangles[t + 3] = baseIndex + 1;
                triangles[t + 4] = baseIndex + 2;
                triangles[t + 5] = baseIndex + 3;
            }

            return CreateMesh("TougeRoad", vertices, triangles, uvs);
        }

        private Mesh BuildVergeMesh()
        {
            int count = _frames.Count;
            // Four vertices per cross-section: road edge and outer edge, on each side.
            Vector3[] vertices = new Vector3[count * 4];
            Vector2[] uvs = new Vector2[count * 4];
            int[] triangles = new int[(count - 1) * 12];

            float half = roadWidth * 0.5f;

            for (int i = 0; i < count; i++)
            {
                RoadFrame frame = _frames[i];
                Vector3 leftEdge = frame.Center - frame.Right * half;
                Vector3 rightEdge = frame.Center + frame.Right * half;

                // The verge falls away vertically rather than along the banked surface normal, so it
                // reads as terrain rather than as an extension of the road.
                Vector3 leftOuter = leftEdge - frame.Right * vergeWidth - Vector3.up * vergeDrop;
                Vector3 rightOuter = rightEdge + frame.Right * vergeWidth - Vector3.up * vergeDrop;

                int b = i * 4;
                vertices[b] = transform.InverseTransformPoint(leftOuter);
                vertices[b + 1] = transform.InverseTransformPoint(leftEdge);
                vertices[b + 2] = transform.InverseTransformPoint(rightEdge);
                vertices[b + 3] = transform.InverseTransformPoint(rightOuter);

                float v = frame.Distance * uvTilingPerMeter;
                uvs[b] = new Vector2(0f, v);
                uvs[b + 1] = new Vector2(1f, v);
                uvs[b + 2] = new Vector2(0f, v);
                uvs[b + 3] = new Vector2(1f, v);
            }

            for (int i = 0; i < count - 1; i++)
            {
                int b = i * 4;
                int t = i * 12;

                // Left verge strip.
                triangles[t] = b;
                triangles[t + 1] = b + 4;
                triangles[t + 2] = b + 1;
                triangles[t + 3] = b + 1;
                triangles[t + 4] = b + 4;
                triangles[t + 5] = b + 5;

                // Right verge strip, wound the other way so it faces upward too.
                triangles[t + 6] = b + 2;
                triangles[t + 7] = b + 6;
                triangles[t + 8] = b + 3;
                triangles[t + 9] = b + 3;
                triangles[t + 10] = b + 6;
                triangles[t + 11] = b + 7;
            }

            return CreateMesh("TougeVerge", vertices, triangles, uvs);
        }

        /// <summary>
        /// Guardrails on both edges, built as a solid extrusion rather than a plane so the car cannot
        /// tunnel through at speed.
        /// </summary>
        private Mesh BuildRailMesh()
        {
            int count = _frames.Count;
            // Eight vertices per cross-section: inner/outer x bottom/top, on each side.
            Vector3[] vertices = new Vector3[count * 8];
            Vector2[] uvs = new Vector2[count * 8];

            // Three quads per side per segment: inner face, top face, outer face.
            int[] triangles = new int[(count - 1) * 36];

            float half = roadWidth * 0.5f;

            for (int i = 0; i < count; i++)
            {
                RoadFrame frame = _frames[i];
                int b = i * 8;

                float leftInner = -(half + railOffset);
                float leftOuter = leftInner - railThickness;
                float rightInner = half + railOffset;
                float rightOuter = rightInner + railThickness;

                vertices[b] = LocalRailPoint(frame, leftInner, 0f);
                vertices[b + 1] = LocalRailPoint(frame, leftInner, railHeight);
                vertices[b + 2] = LocalRailPoint(frame, leftOuter, railHeight);
                vertices[b + 3] = LocalRailPoint(frame, leftOuter, 0f);

                vertices[b + 4] = LocalRailPoint(frame, rightInner, 0f);
                vertices[b + 5] = LocalRailPoint(frame, rightInner, railHeight);
                vertices[b + 6] = LocalRailPoint(frame, rightOuter, railHeight);
                vertices[b + 7] = LocalRailPoint(frame, rightOuter, 0f);

                float v = frame.Distance * uvTilingPerMeter;
                for (int k = 0; k < 8; k++) uvs[b + k] = new Vector2((k % 2 == 0) ? 0f : 1f, v);
            }

            int index = 0;
            for (int i = 0; i < count - 1; i++)
            {
                int b = i * 8;

                // Left rail: inner face, top, outer face.
                AddQuad(triangles, ref index, b + 0, b + 1, b + 8, b + 9);
                AddQuad(triangles, ref index, b + 1, b + 2, b + 9, b + 10);
                AddQuad(triangles, ref index, b + 2, b + 3, b + 10, b + 11);

                // Right rail, wound opposite so its inner face points back at the road.
                AddQuad(triangles, ref index, b + 5, b + 4, b + 13, b + 12);
                AddQuad(triangles, ref index, b + 6, b + 5, b + 14, b + 13);
                AddQuad(triangles, ref index, b + 7, b + 6, b + 15, b + 14);
            }

            return CreateMesh("TougeGuardrails", vertices, triangles, uvs);
        }

        private Vector3 LocalRailPoint(RoadFrame frame, float lateralOffset, float height)
        {
            Vector3 world = frame.Center + frame.Right * lateralOffset + frame.Up * height;
            return transform.InverseTransformPoint(world);
        }

        private static void AddQuad(int[] triangles, ref int index, int a, int b, int c, int d)
        {
            triangles[index++] = a;
            triangles[index++] = c;
            triangles[index++] = b;
            triangles[index++] = b;
            triangles[index++] = c;
            triangles[index++] = d;
        }

        private static Mesh CreateMesh(string name, Vector3[] vertices, int[] triangles, Vector2[] uvs)
        {
            Mesh mesh = new Mesh { name = name };

            // A long pass at 2 m spacing comfortably exceeds the 16-bit vertex limit once rails and
            // verge are included, so use a 32-bit index buffer.
            mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;

            mesh.vertices = vertices;
            mesh.triangles = triangles;
            mesh.uv = uvs;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>Create or update the child object that renders and collides one generated mesh.</summary>
        private void AssignMesh(string childName, Mesh mesh, Material material, bool addCollider)
        {
            Transform child = transform.Find(childName);

            if (mesh == null)
            {
                if (child != null) DestroyImmediate(child.gameObject);
                return;
            }

            if (child == null)
            {
                GameObject created = new GameObject(childName);
                created.transform.SetParent(transform, false);
                child = created.transform;
            }

            // TryGetComponent, never "GetComponent() ?? AddComponent()". A missing component comes
            // back as Unity's fake null - a live C# reference wrapping a null native pointer - so ??
            // never falls through to AddComponent, and the next line throws MissingComponentException.
            if (!child.TryGetComponent(out MeshFilter filter))
                filter = child.gameObject.AddComponent<MeshFilter>();

            if (!child.TryGetComponent(out MeshRenderer renderer))
                renderer = child.gameObject.AddComponent<MeshRenderer>();

            filter.sharedMesh = mesh;
            if (material != null) renderer.sharedMaterial = material;

            if (!addCollider) return;

            if (!child.TryGetComponent(out MeshCollider collider))
                collider = child.gameObject.AddComponent<MeshCollider>();
            collider.sharedMesh = null;      // Force the collider to rebuild from the new mesh data.
            collider.sharedMesh = mesh;
        }

        private void OnDrawGizmosSelected()
        {
            if (_frames == null || _frames.Count < 2) return;

            Gizmos.color = Color.yellow;
            float half = roadWidth * 0.5f;

            // Draw every tenth cross-section: enough to see banking and width without flooding the view.
            for (int i = 0; i < _frames.Count; i += 10)
            {
                RoadFrame frame = _frames[i];
                Gizmos.DrawLine(frame.Center - frame.Right * half, frame.Center + frame.Right * half);
                Gizmos.DrawLine(frame.Center, frame.Center + frame.Up * 1.5f);
            }
        }
    }
}
