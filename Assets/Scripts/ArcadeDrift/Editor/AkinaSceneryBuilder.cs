using System.Collections.Generic;
using Touge.Track;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Touge.ArcadeDrift.Editor
{
    /// <summary>
    /// Wraps the pass in a mountainside: a sculpted terrain, painted by slope, with conifer forest.
    ///
    /// The terrain is derived from the road, never the other way round. The road meshes are saved
    /// assets shared with the simulation scene, so they are only READ here - the heightmap is carved
    /// to fit under and around them.
    ///
    /// THE CEILING. Every terrain cell is capped by a ceiling that sits just under the road and the
    /// verge, then rises away from the road at no more than <see cref="SlopeLimit"/>. The chase camera
    /// looks down at 38 degrees, i.e. its line of sight climbs at tan(38) = 0.78 m per metre. Ground
    /// that climbs at 0.5 can never rise into that line, so no hillside can hide the car or the road
    /// ahead of it, whichever way the camera is facing. The natural mountain shape is then the lower
    /// of that ceiling and a noise field, which reads as a road cut into a slope.
    ///
    /// The ceiling is computed as a sloped distance transform (two-pass chamfer) seeded along the
    /// road, so it costs the same whether the course is 1 km or 10 km long.
    /// </summary>
    public static class AkinaSceneryBuilder
    {
        private const string Folder = "Assets/Generated/Scenery";
        private const string RootName = "Scenery";

        /// <summary>Terrain beyond the course's bounding box on every side. [m]</summary>
        private const float Margin = 180f;

        private const int HeightmapResolution = 1025;
        private const int AlphamapResolution = 512;

        /// <summary>Steepest the ground may rise away from the road. Must stay under tan(camera pitch).</summary>
        private const float SlopeLimit = 0.5f;

        /// <summary>How far the terrain sits below the road and verge surfaces. [m]</summary>
        private const float Clearance = 0.4f;

        /// <summary>No tree closer than this to the road centreline. [m]</summary>
        private const float TreeClearance = 14f;

        /// <summary>Spacing of the jittered grid trees are placed on. [m]</summary>
        private const float TreeSpacing = 6f;

        /// <summary>Fixed seed, so rebuilding gives the same mountain every time.</summary>
        private const int Seed = 86;

        private struct RoadSample
        {
            public Vector3 Center;

            /// <summary>Height of the lower road edge, so banked sections stay clear on both sides.</summary>
            public float Floor;
        }

        /// <summary>One cone of foliage on a tree. [m]</summary>
        private struct Tier
        {
            public float BaseY;
            public float Radius;
            public float TopY;

            public Tier(float baseY, float radius, float topY)
            {
                BaseY = baseY;
                Radius = radius;
                TopY = topY;
            }
        }

        /// <summary>Per-cell working data at heightmap resolution.</summary>
        private sealed class Grid
        {
            public int Resolution;
            public float Cell;
            public float Size;
            public Vector2 Origin;
            public float[] Ceiling;
            public float[] Distance;
            public float[] Height;

            public Vector2 World(int x, int z) => new Vector2(Origin.x + x * Cell, Origin.y + z * Cell);

            public float SampleDistance(float u, float v)
            {
                int x = Mathf.Clamp(Mathf.RoundToInt(u * (Resolution - 1)), 0, Resolution - 1);
                int z = Mathf.Clamp(Mathf.RoundToInt(v * (Resolution - 1)), 0, Resolution - 1);
                return Distance[z * Resolution + x];
            }
        }

        [MenuItem("Touge/Arcade Drift/Build Scenery In Open Scene", false, 42)]
        public static void BuildInOpenScene()
        {
            Scene scene = SceneManager.GetActiveScene();
            if (!Build(scene)) return;

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
        }

        /// <summary>
        /// Replace the scenery in <paramref name="scene"/>. Returns false if the scene has no road.
        /// Does not save the scene; the caller decides that.
        /// </summary>
        /// <param name="assetName">Prefix for the saved terrain data. Defaults to the scene's name;
        /// pass it explicitly when the scene is about to be saved under a different one.</param>
        public static bool Build(Scene scene, string assetName = null)
        {
            SplineRoadBuilder road = FindRoad(scene);
            List<RoadSample> samples = road != null ? SampleRoad(road) : null;
            if (samples == null || samples.Count < 2)
            {
                Debug.LogError($"[ArcadeDrift] {scene.name} has no generated road to build scenery around.");
                return false;
            }

            foreach (GameObject root in scene.GetRootGameObjects())
                if (root.name == RootName) Object.DestroyImmediate(root);

            DriftLabSceneBuilder.EnsureFolder(Folder);

            try
            {
                EditorUtility.DisplayProgressBar("Akina scenery", "Carving terrain around the road", 0.1f);
                Grid grid = CreateGrid(samples);
                SeedFromRoad(grid, samples, road);
                Propagate(grid);

                EditorUtility.DisplayProgressBar("Akina scenery", "Shaping the mountain", 0.35f);
                ComputeHeights(grid, samples);

                EditorUtility.DisplayProgressBar("Akina scenery", "Creating terrain", 0.55f);
                TerrainData data = CreateTerrainData(grid, assetName ?? scene.name, out float baseHeight);

                EditorUtility.DisplayProgressBar("Akina scenery", "Painting", 0.7f);
                Paint(data, grid);

                EditorUtility.DisplayProgressBar("Akina scenery", "Planting trees", 0.85f);
                int trees = PlantTrees(data, grid);

                CreateTerrainObject(data, grid, baseHeight);
                EditorUtility.SetDirty(data);

                Debug.Log($"[ArcadeDrift] Scenery built in {scene.name}: {grid.Size:F0} m terrain, " +
                          $"{trees} trees.");
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            return true;
        }

        // -----------------------------------------------------------------------------------------
        // Road
        // -----------------------------------------------------------------------------------------

        private static SplineRoadBuilder FindRoad(Scene scene)
        {
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                SplineRoadBuilder road = root.GetComponentInChildren<SplineRoadBuilder>(true);
                if (road != null) return road;
            }
            return null;
        }

        /// <summary>
        /// Read the cross-sections back out of the saved road mesh: vertices come in left/right pairs,
        /// one pair per frame. Reading the mesh rather than re-sampling the spline means the terrain
        /// fits exactly what is rendered and collided with, and nothing is rebuilt.
        /// </summary>
        private static List<RoadSample> SampleRoad(SplineRoadBuilder road)
        {
            Transform surface = road.transform.Find("RoadSurface");
            if (surface == null || !surface.TryGetComponent(out MeshFilter filter) || filter.sharedMesh == null)
                return null;

            Vector3[] vertices = filter.sharedMesh.vertices;
            List<RoadSample> samples = new List<RoadSample>(vertices.Length / 2);

            for (int i = 0; i + 1 < vertices.Length; i += 2)
            {
                Vector3 left = surface.TransformPoint(vertices[i]);
                Vector3 right = surface.TransformPoint(vertices[i + 1]);
                samples.Add(new RoadSample
                {
                    Center = (left + right) * 0.5f,
                    Floor = Mathf.Min(left.y, right.y)
                });
            }

            return samples;
        }

        // -----------------------------------------------------------------------------------------
        // Heightfield
        // -----------------------------------------------------------------------------------------

        private static Grid CreateGrid(List<RoadSample> samples)
        {
            Vector2 min = new Vector2(float.MaxValue, float.MaxValue);
            Vector2 max = new Vector2(float.MinValue, float.MinValue);
            foreach (RoadSample sample in samples)
            {
                min = Vector2.Min(min, new Vector2(sample.Center.x, sample.Center.z));
                max = Vector2.Max(max, new Vector2(sample.Center.x, sample.Center.z));
            }

            // Square, because a terrain's heightmap is square.
            float size = Mathf.Max(max.x - min.x, max.y - min.y) + Margin * 2f;
            Vector2 centre = (min + max) * 0.5f;
            int cells = HeightmapResolution * HeightmapResolution;

            Grid grid = new Grid
            {
                Resolution = HeightmapResolution,
                Size = size,
                Cell = size / (HeightmapResolution - 1),
                Origin = centre - Vector2.one * (size * 0.5f),
                Ceiling = new float[cells],
                Distance = new float[cells],
                Height = new float[cells]
            };

            for (int i = 0; i < cells; i++)
            {
                grid.Ceiling[i] = float.PositiveInfinity;
                grid.Distance[i] = float.PositiveInfinity;
            }

            return grid;
        }

        /// <summary>
        /// Stamp the exact cross-section profile around every road sample: under the road, down the
        /// verge, and a short way up the slope beyond. The chamfer pass carries it on from there.
        /// </summary>
        private static void SeedFromRoad(Grid grid, List<RoadSample> samples, SplineRoadBuilder road)
        {
            float half = road.roadWidth * 0.5f;
            float vergeWidth = Mathf.Max(0.01f, road.vergeWidth);
            float vergeOuter = half + vergeWidth;
            float seedRadius = vergeOuter + 2f;
            int reach = Mathf.CeilToInt(seedRadius / grid.Cell);
            int n = grid.Resolution;

            foreach (RoadSample sample in samples)
            {
                int cx = Mathf.RoundToInt((sample.Center.x - grid.Origin.x) / grid.Cell);
                int cz = Mathf.RoundToInt((sample.Center.z - grid.Origin.y) / grid.Cell);

                for (int z = Mathf.Max(0, cz - reach); z <= Mathf.Min(n - 1, cz + reach); z++)
                for (int x = Mathf.Max(0, cx - reach); x <= Mathf.Min(n - 1, cx + reach); x++)
                {
                    Vector2 world = grid.World(x, z);
                    float d = Vector2.Distance(world, new Vector2(sample.Center.x, sample.Center.z));
                    if (d > seedRadius) continue;

                    float ceiling = sample.Floor - Clearance
                                    - road.vergeDrop * Mathf.Clamp01((d - half) / vergeWidth)
                                    + SlopeLimit * Mathf.Max(0f, d - vergeOuter);

                    int i = z * n + x;
                    if (d < grid.Distance[i]) grid.Distance[i] = d;
                    if (ceiling < grid.Ceiling[i]) grid.Ceiling[i] = ceiling;
                }
            }
        }

        /// <summary>
        /// Two-pass chamfer transform: spreads distance-to-road and the sloped ceiling across the whole
        /// grid. The 8-neighbour chamfer overestimates distance by at most ~8%, which only makes the
        /// effective slope ~0.54 at worst - still well below the camera's 0.78.
        /// </summary>
        private static void Propagate(Grid grid)
        {
            int n = grid.Resolution;
            float straight = grid.Cell;
            float diagonal = grid.Cell * 1.41421356f;

            for (int z = 0; z < n; z++)
            for (int x = 0; x < n; x++)
            {
                int i = z * n + x;
                Relax(grid, i, x - 1, z, straight);
                Relax(grid, i, x - 1, z - 1, diagonal);
                Relax(grid, i, x, z - 1, straight);
                Relax(grid, i, x + 1, z - 1, diagonal);
            }

            for (int z = n - 1; z >= 0; z--)
            for (int x = n - 1; x >= 0; x--)
            {
                int i = z * n + x;
                Relax(grid, i, x + 1, z, straight);
                Relax(grid, i, x + 1, z + 1, diagonal);
                Relax(grid, i, x, z + 1, straight);
                Relax(grid, i, x - 1, z + 1, diagonal);
            }
        }

        private static void Relax(Grid grid, int i, int x, int z, float step)
        {
            int n = grid.Resolution;
            if (x < 0 || z < 0 || x >= n || z >= n) return;

            int j = z * n + x;
            float distance = grid.Distance[j] + step;
            if (distance < grid.Distance[i]) grid.Distance[i] = distance;

            float ceiling = grid.Ceiling[j] + SlopeLimit * step;
            if (ceiling < grid.Ceiling[i]) grid.Ceiling[i] = ceiling;
        }

        /// <summary>
        /// The natural mountain: the road's own height smeared outward, plus ridges and rolling noise,
        /// plus a rim of higher peaks well away from the course. Clamped under the ceiling.
        /// </summary>
        private static void ComputeHeights(Grid grid, List<RoadSample> samples)
        {
            int n = grid.Resolution;
            float[] trend = RoadHeightTrend(grid, samples);

            System.Random rng = new System.Random(Seed);
            Vector2 offsetA = new Vector2(rng.Next(1000, 9000), rng.Next(1000, 9000));
            Vector2 offsetB = new Vector2(rng.Next(1000, 9000), rng.Next(1000, 9000));

            for (int z = 0; z < n; z++)
            for (int x = 0; x < n; x++)
            {
                int i = z * n + x;
                Vector2 world = grid.World(x, z);

                float rolling = Fbm(world / 240f + offsetA, 4) - 0.5f;
                float ridged = 1f - Mathf.Abs(Fbm(world / 380f + offsetB, 3) * 2f - 1f);
                float detail = Fbm(world / 25f + offsetB, 2) - 0.5f;
                float rim = Mathf.Min(80f, 0.35f * Mathf.Max(0f, grid.Distance[i] - 70f));

                float natural = trend[i] + 6f + rolling * 36f + ridged * ridged * 28f + detail * 1.5f + rim;
                grid.Height[i] = Mathf.Min(natural, grid.Ceiling[i]);
            }
        }

        /// <summary>
        /// Road height extended over the whole grid by inverse-distance weighting, so the mountain
        /// falls with the course - high at the start, low at the finish. Solved on a coarse grid and
        /// upsampled; it is a broad trend, detail comes from the noise.
        /// </summary>
        private static float[] RoadHeightTrend(Grid grid, List<RoadSample> samples)
        {
            const int coarse = 65;
            const int stride = 4;
            float coarseCell = grid.Size / (coarse - 1);
            float[] coarseHeights = new float[coarse * coarse];

            for (int z = 0; z < coarse; z++)
            for (int x = 0; x < coarse; x++)
            {
                Vector2 world = grid.Origin + new Vector2(x, z) * coarseCell;
                float weightSum = 0f;
                float heightSum = 0f;

                for (int s = 0; s < samples.Count; s += stride)
                {
                    Vector3 c = samples[s].Center;
                    float d2 = (world - new Vector2(c.x, c.z)).sqrMagnitude + 400f;
                    float w = 1f / (d2 * d2);
                    weightSum += w;
                    heightSum += w * samples[s].Floor;
                }

                coarseHeights[z * coarse + x] = heightSum / weightSum;
            }

            int n = grid.Resolution;
            float[] trend = new float[n * n];
            for (int z = 0; z < n; z++)
            for (int x = 0; x < n; x++)
            {
                float u = x * grid.Cell / coarseCell;
                float v = z * grid.Cell / coarseCell;
                int x0 = Mathf.Min(coarse - 2, Mathf.FloorToInt(u));
                int z0 = Mathf.Min(coarse - 2, Mathf.FloorToInt(v));
                float tx = u - x0;
                float tz = v - z0;

                float bottom = Mathf.Lerp(coarseHeights[z0 * coarse + x0], coarseHeights[z0 * coarse + x0 + 1], tx);
                float top = Mathf.Lerp(coarseHeights[(z0 + 1) * coarse + x0], coarseHeights[(z0 + 1) * coarse + x0 + 1], tx);
                trend[z * n + x] = Mathf.Lerp(bottom, top, tz);
            }

            return trend;
        }

        private static float Fbm(Vector2 p, int octaves)
        {
            float sum = 0f;
            float amplitude = 0.5f;
            float total = 0f;
            for (int o = 0; o < octaves; o++)
            {
                sum += Mathf.PerlinNoise(p.x, p.y) * amplitude;
                total += amplitude;
                p *= 2.03f;
                amplitude *= 0.5f;
            }
            return sum / total;
        }

        // -----------------------------------------------------------------------------------------
        // Terrain
        // -----------------------------------------------------------------------------------------

        private static TerrainData CreateTerrainData(Grid grid, string sceneName, out float baseHeight)
        {
            int n = grid.Resolution;
            float minHeight = float.MaxValue;
            float maxHeight = float.MinValue;
            foreach (float h in grid.Height)
            {
                minHeight = Mathf.Min(minHeight, h);
                maxHeight = Mathf.Max(maxHeight, h);
            }

            baseHeight = minHeight - 1f;
            float range = maxHeight - baseHeight + 1f;

            // Recreated rather than edited, so a rebuild never inherits stale trees or layers.
            string path = $"{Folder}/{sceneName}_Terrain.asset";
            AssetDatabase.DeleteAsset(path);

            // Resolution BEFORE size: setting the resolution rescales the size.
            TerrainData data = new TerrainData
            {
                heightmapResolution = n,
                alphamapResolution = AlphamapResolution,
                baseMapResolution = 1024
            };
            data.size = new Vector3(grid.Size, range, grid.Size);
            AssetDatabase.CreateAsset(data, path);

            float[,] heights = new float[n, n];
            for (int z = 0; z < n; z++)
            for (int x = 0; x < n; x++)
                heights[z, x] = (grid.Height[z * n + x] - baseHeight) / range;

            data.SetHeights(0, 0, heights);
            return data;
        }

        /// <summary>Rock on steep ground, gravel along the shoulders, forest floor in patches, grass elsewhere.</summary>
        private static void Paint(TerrainData data, Grid grid)
        {
            data.terrainLayers = new[]
            {
                CreateLayer("Grass", new Color(0.34f, 0.46f, 0.21f), 0.12f, 8f),
                CreateLayer("ForestFloor", new Color(0.21f, 0.29f, 0.14f), 0.18f, 6f),
                CreateLayer("Rock", new Color(0.46f, 0.44f, 0.41f), 0.22f, 10f),
                CreateLayer("Gravel", new Color(0.45f, 0.40f, 0.32f), 0.20f, 4f)
            };

            int n = AlphamapResolution;
            float[,,] maps = new float[n, n, 4];

            for (int z = 0; z < n; z++)
            for (int x = 0; x < n; x++)
            {
                float u = x / (float)(n - 1);
                float v = z / (float)(n - 1);
                Vector2 world = grid.Origin + new Vector2(u, v) * grid.Size;

                float rock = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(28f, 42f, data.GetSteepness(u, v)));
                float gravel = (1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(9f, 13f, grid.SampleDistance(u, v))))
                               * (1f - rock);
                float forest = ForestMask(world) * (1f - rock) * (1f - gravel);

                maps[z, x, 0] = 1f - rock - gravel - forest;
                maps[z, x, 1] = forest;
                maps[z, x, 2] = rock;
                maps[z, x, 3] = gravel;
            }

            data.SetAlphamaps(0, 0, maps);
        }

        /// <summary>Where the woods are. Shared by painting and planting so the trees stand on forest floor.</summary>
        private static float ForestMask(Vector2 world)
        {
            float noise = Fbm(world / 90f + new Vector2(311f, 97f), 3);
            return Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.45f, 0.58f, noise));
        }

        private static int PlantTrees(TerrainData data, Grid grid)
        {
            Material trunk = InstancedMaterial("Scenery_Trunk", new Color(0.33f, 0.23f, 0.15f));
            Material pine = InstancedMaterial("Scenery_Pine", new Color(0.12f, 0.26f, 0.13f));
            Material fir = InstancedMaterial("Scenery_Fir", new Color(0.19f, 0.33f, 0.15f));

            // Heights kept under ~10 m. A tree 14 m off the road on the uphill side could otherwise
            // poke into the camera's line of sight, which the terrain ceiling cannot prevent.
            data.treePrototypes = new[]
            {
                new TreePrototype
                {
                    prefab = TreePrefab("Pine", 0.25f, 2f, trunk, pine,
                        new Tier(1.4f, 2.1f, 5.2f), new Tier(3.4f, 1.6f, 6.6f), new Tier(5.0f, 1.0f, 8f))
                },
                new TreePrototype
                {
                    prefab = TreePrefab("Fir", 0.3f, 1.6f, trunk, fir,
                        new Tier(1.1f, 2.6f, 4.4f), new Tier(2.8f, 1.9f, 6f))
                }
            };

            System.Random rng = new System.Random(Seed + 1);
            List<TreeInstance> trees = new List<TreeInstance>();
            int steps = Mathf.FloorToInt(grid.Size / TreeSpacing);

            for (int gz = 0; gz < steps; gz++)
            for (int gx = 0; gx < steps; gx++)
            {
                float u = (gx + (float)rng.NextDouble()) / steps;
                float v = (gz + (float)rng.NextDouble()) / steps;
                Vector2 world = grid.Origin + new Vector2(u, v) * grid.Size;

                float chance = Mathf.Lerp(0.08f, 0.9f, ForestMask(world));
                if (rng.NextDouble() > chance) continue;
                if (grid.SampleDistance(u, v) < TreeClearance) continue;
                if (data.GetSteepness(u, v) > 36f) continue;

                float scale = Mathf.Lerp(0.75f, 1.2f, (float)rng.NextDouble());
                trees.Add(new TreeInstance
                {
                    position = new Vector3(u, 0f, v),
                    prototypeIndex = rng.Next(2),
                    heightScale = scale,
                    widthScale = scale * Mathf.Lerp(0.85f, 1.15f, (float)rng.NextDouble()),
                    rotation = (float)rng.NextDouble() * Mathf.PI * 2f,
                    color = Color.white,
                    lightmapColor = Color.white
                });
            }

            data.SetTreeInstances(trees.ToArray(), true);
            return trees.Count;
        }

        private static void CreateTerrainObject(TerrainData data, Grid grid, float baseHeight)
        {
            GameObject go = Terrain.CreateTerrainGameObject(data);
            go.name = RootName;
            go.transform.position = new Vector3(grid.Origin.x, baseHeight, grid.Origin.y);

            int ground = LayerMask.NameToLayer("Ground");
            if (ground >= 0) go.layer = ground;

            Terrain terrain = go.GetComponent<Terrain>();
            terrain.heightmapPixelError = 4f;
            terrain.basemapDistance = 400f;
            terrain.drawInstanced = true;

            // Mesh trees only: billboards for non-SpeedTree meshes look worse than drawing them, and
            // the orthographic camera never sees far enough for the cost to matter.
            terrain.treeDistance = 1000f;
            terrain.treeBillboardDistance = 1000f;
            terrain.treeMaximumFullLODCount = 100000;

            Shader shader = Shader.Find("Universal Render Pipeline/Terrain/Lit");
            if (shader != null)
            {
                string path = Folder + "/Scenery_Terrain.mat";
                Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (material == null)
                {
                    material = new Material(shader);
                    AssetDatabase.CreateAsset(material, path);
                }
                terrain.materialTemplate = material;
            }
        }

        // -----------------------------------------------------------------------------------------
        // Assets
        // -----------------------------------------------------------------------------------------

        private static TerrainLayer CreateLayer(string name, Color colour, float variation, float tileSize)
        {
            string path = $"{Folder}/Layer_{name}.terrainlayer";
            TerrainLayer existing = AssetDatabase.LoadAssetAtPath<TerrainLayer>(path);
            if (existing != null) return existing;

            TerrainLayer layer = new TerrainLayer
            {
                diffuseTexture = NoiseTexture("Tex_" + name, colour, variation),
                tileSize = new Vector2(tileSize, tileSize),
                smoothness = 0f,
                metallic = 0f
            };
            AssetDatabase.CreateAsset(layer, path);
            return layer;
        }

        /// <summary>
        /// A tileable mottled texture: value noise on a wrapped lattice plus per-pixel grain. Enough
        /// variation to break up a flat colour and show speed, without needing imported art.
        /// </summary>
        private static Texture2D NoiseTexture(string name, Color colour, float variation)
        {
            string path = $"{Folder}/{name}.asset";
            Texture2D existing = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (existing != null) return existing;

            const int size = 128;
            const int cells = 16;
            System.Random rng = new System.Random(name.GetHashCode());

            float[] lattice = new float[cells * cells];
            for (int i = 0; i < lattice.Length; i++) lattice[i] = (float)rng.NextDouble();

            Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, true)
            {
                name = name,
                wrapMode = TextureWrapMode.Repeat,
                filterMode = FilterMode.Bilinear
            };

            Color[] pixels = new Color[size * size];
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float fx = x * cells / (float)size;
                float fy = y * cells / (float)size;
                int x0 = Mathf.FloorToInt(fx);
                int y0 = Mathf.FloorToInt(fy);
                float tx = Mathf.SmoothStep(0f, 1f, fx - x0);
                float ty = Mathf.SmoothStep(0f, 1f, fy - y0);
                int x1 = (x0 + 1) % cells;
                int y1 = (y0 + 1) % cells;

                float value = Mathf.Lerp(
                    Mathf.Lerp(lattice[y0 * cells + x0], lattice[y0 * cells + x1], tx),
                    Mathf.Lerp(lattice[y1 * cells + x0], lattice[y1 * cells + x1], tx), ty);
                float grain = (float)rng.NextDouble() - 0.5f;

                float shade = 1f + (value - 0.5f) * 2f * variation + grain * variation * 0.6f;
                Color c = colour * shade;
                c.a = 0f;   // Albedo alpha doubles as smoothness on terrain: keep the ground matte.
                pixels[y * size + x] = c;
            }

            texture.SetPixels(pixels);
            texture.Apply(true);
            AssetDatabase.CreateAsset(texture, path);
            return texture;
        }

        private static Material InstancedMaterial(string name, Color colour)
        {
            Material material = DriftLabSceneBuilder.CreateMaterial(name, colour);
            if (!material.enableInstancing)
            {
                material.enableInstancing = true;
                EditorUtility.SetDirty(material);
            }
            return material;
        }

        /// <summary>
        /// A low-poly conifer: a prism trunk under stacked cones. Built as ONE mesh with two submeshes
        /// because terrain trees need a single renderer at the prefab root. Vertices are not shared,
        /// so the normals come out flat-shaded.
        ///
        /// The mesh is rewritten in place on every build so shape tweaks here take effect without
        /// breaking the prefab's reference to it.
        /// </summary>
        private static GameObject TreePrefab(string name, float trunkRadius, float trunkHeight,
                                             Material trunk, Material foliage, params Tier[] tiers)
        {
            const int sides = 7;
            List<Vector3> vertices = new List<Vector3>();
            List<int> trunkTriangles = new List<int>();
            List<int> foliageTriangles = new List<int>();

            for (int i = 0; i < sides; i++)
            {
                Vector3 a = Ring(i, sides, trunkRadius, 0f);
                Vector3 b = Ring(i + 1, sides, trunkRadius, 0f);
                Vector3 outward = Ring(i + 0.5f, sides, 1f, 0f);
                Vector3 up = Vector3.up * trunkHeight;

                AddTriangle(vertices, trunkTriangles, a, a + up, b + up, outward);
                AddTriangle(vertices, trunkTriangles, a, b + up, b, outward);
            }

            foreach (Tier tier in tiers)
            {
                Vector3 apex = Vector3.up * tier.TopY;
                Vector3 centre = Vector3.up * tier.BaseY;

                for (int i = 0; i < sides; i++)
                {
                    Vector3 a = Ring(i, sides, tier.Radius, tier.BaseY);
                    Vector3 b = Ring(i + 1, sides, tier.Radius, tier.BaseY);
                    Vector3 outward = Ring(i + 0.5f, sides, 1f, 0f) + Vector3.up * 0.5f;

                    AddTriangle(vertices, foliageTriangles, apex, a, b, outward);
                    AddTriangle(vertices, foliageTriangles, centre, a, b, Vector3.down);
                }
            }

            string meshPath = $"{Folder}/Tree_{name}.asset";
            Mesh mesh = AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);
            if (mesh == null)
            {
                mesh = new Mesh { name = "Tree_" + name };
                AssetDatabase.CreateAsset(mesh, meshPath);
            }

            mesh.Clear();
            mesh.SetVertices(vertices);
            mesh.subMeshCount = 2;
            mesh.SetTriangles(trunkTriangles, 0);
            mesh.SetTriangles(foliageTriangles, 1);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            EditorUtility.SetDirty(mesh);

            string prefabPath = $"{Folder}/Tree_{name}.prefab";
            GameObject existing = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (existing != null) return existing;

            GameObject temp = new GameObject("Tree_" + name);
            temp.AddComponent<MeshFilter>().sharedMesh = mesh;
            temp.AddComponent<MeshRenderer>().sharedMaterials = new[] { trunk, foliage };

            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(temp, prefabPath);
            Object.DestroyImmediate(temp);
            return prefab;
        }

        private static Vector3 Ring(float index, int sides, float radius, float y)
        {
            float angle = index / sides * Mathf.PI * 2f;
            return new Vector3(Mathf.Cos(angle) * radius, y, Mathf.Sin(angle) * radius);
        }

        /// <summary>Adds a triangle wound so its front face points along <paramref name="outward"/>.</summary>
        private static void AddTriangle(List<Vector3> vertices, List<int> triangles,
                                        Vector3 a, Vector3 b, Vector3 c, Vector3 outward)
        {
            if (Vector3.Dot(Vector3.Cross(b - a, c - a), outward) < 0f) (b, c) = (c, b);

            int start = vertices.Count;
            vertices.Add(a);
            vertices.Add(b);
            vertices.Add(c);
            triangles.Add(start);
            triangles.Add(start + 1);
            triangles.Add(start + 2);
        }
    }
}
