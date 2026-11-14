using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Letsail.World.Islands
{
    public enum IslandBiome
    {
        Tropical,
        Volcanic,
    }

    /// <summary>섬 지면의 종류 (동물을 놓을 때 쓴다).</summary>
    public enum IslandSurface : byte
    {
        None,
        Seabed,
        Beach,
        Grass,
        Jungle,
        Dirt,
        Rock,
        /// <summary>화산재·현무암.</summary>
        Volcanic,
    }

    /// <summary>
    /// 섬 하나를 시드로 만든다. 같은 시드면 어느 PC 에서든 똑같은 섬이 나온다 (System.Random 과 Perlin 노이즈만 쓴다).
    /// 1. 섬 모양: 타원 + 해안선 노이즈. 해변 띠 → 내륙 평원 → 언덕, 한쪽에 바위 절벽 (화산섬은 가운데 분화구).
    /// 2. 지형: 1.5m 격자를 흔든 로우폴리 메쉬. 면마다 정점을 따로 둬서 각진 면이 보이고, 높이·경사로 면 색을 정한다 (공용 컬러맵 UV).
    /// 3. 소품: 큰 것부터 작은 것 순서로, 소품마다 PcgInfo 규칙(바이옴, 높이, 경사, 반경)에 맞는 자리에 흩뿌린다.
    /// 이 오브젝트 위치가 섬 가운데이고, 높이는 해수면에 맞춰 둔다 (로컬 y = 0 이 해수면).
    /// </summary>
    public sealed class IslandGenerator : MonoBehaviour
    {
        // 면 색 구역
        enum Zone : byte { Seabed, SandWet, Sand, Dirt, Rock, Grass, JungleGrass, Basalt, Ash }

        static readonly string[] ZoneColors =
        {
            "Terrain_Seabed", "Terrain_SandWet", "Terrain_Sand", "Terrain_Dirt", "Terrain_Rock",
            "Terrain_Grass", "Terrain_JungleGrass", "Terrain_Basalt", "Terrain_Ash",
        };

        // 큰 것부터 놓는다. 숫자는 그 종류가 덮을 땅(바다 종류는 물) 넓이 비율.
        static readonly (string category, float fill)[] PlacementOrder =
        {
            ("landmark", 0f), ("cliff", 0.04f), ("hazard", 0.02f), ("tree", 0.08f), ("rock", 0.02f),
            ("bush", 0.025f), ("log", 0.006f), ("plant", 0.015f), ("detail", 0.006f),
            ("sea_rock", 0.01f), ("reef", 0.004f), ("floating", 0.0015f),
        };

        [Header("에셋")]
        [SerializeField] ColorPalette palette;
        [SerializeField, Tooltip("컬러맵을 쓰는 지형 머티리얼")] Material terrainMaterial;
        [SerializeField] IslandPropCatalog catalog;

        [Header("지형")]
        [SerializeField, Tooltip("격자 한 칸 (m)")] float cellSize = 1.5f;
        [SerializeField, Range(0f, 0.3f), Tooltip("정점을 흔드는 정도 (칸 크기 비율)")] float jitter = 0.17f;
        [SerializeField, Tooltip("해안에서 바다 쪽으로 얕은 바닥이 이어지는 폭 (m)")] float shelfWidth = 14f;
        [SerializeField, Tooltip("얕은 바닥 끝의 깊이 (m)")] float seabedDepth = -3.5f;
        [SerializeField, Tooltip("해변 폭 (m)")] float beachWidth = 7f;

        [Header("소품")]
        [SerializeField, Range(0f, 2f), Tooltip("소품 밀도 배율")] float propDensity = 1f;

        [Header("에디터 미리보기")]
        [SerializeField] int previewSeed = 1;
        [SerializeField] IslandBiome previewBiome;
        [SerializeField] float previewRadius = 20f;

        static int islandLayer = -2;

        /// <summary>섬 지형과 바위 레이어 ("Island"). 배 좌초 판정이 쓴다. 레이어가 없으면 Default.</summary>
        public static int IslandLayer
        {
            get
            {
                if (islandLayer == -2) islandLayer = Mathf.Max(0, LayerMask.NameToLayer("Island"));
                return islandLayer;
            }
        }

        static void SetLayer(Transform t, int layer)
        {
            t.gameObject.layer = layer;
            for (int i = 0; i < t.childCount; i++) SetLayer(t.GetChild(i), layer);
        }

        Shape shape;
        Zone[] triangleZones;
        MeshCollider terrainCollider;

        public IslandBiome Biome => shape != null ? shape.biome : previewBiome;

        /// <summary>섬(해안선)의 대략 반지름 (m).</summary>
        public float Radius => shape != null ? shape.radius * shape.elongationMax : 0f;

        /// <summary>얕은 바닥까지 포함한 반지름 (m). 섬끼리 이만큼 떨어뜨린다.</summary>
        public float OuterRadius => Radius + shelfWidth + 4f;

        /// <summary>
        /// 이 위치(월드) 바로 아래 섬 지형. 섬 밖이면 false.
        /// point 는 지면 위치(월드), height 는 해수면 기준 높이, slope 는 기울기(도).
        /// </summary>
        public bool SampleGround(Vector3 worldPosition, out Vector3 point, out float height, out float slope, out IslandSurface surface)
        {
            point = worldPosition;
            height = -100f;
            slope = 0f;
            surface = IslandSurface.None;
            if (terrainCollider == null) return false;
            Vector3 top = new Vector3(worldPosition.x, transform.position.y + 80f, worldPosition.z);
            if (!terrainCollider.Raycast(new Ray(top, Vector3.down), out var hit, 200f)) return false;
            point = hit.point;
            height = hit.point.y - transform.position.y;
            slope = Vector3.Angle(hit.normal, Vector3.up);
            Zone zone = hit.triangleIndex >= 0 && triangleZones != null && hit.triangleIndex < triangleZones.Length ? triangleZones[hit.triangleIndex] : Zone.Seabed;
            surface = zone switch
            {
                Zone.Seabed => IslandSurface.Seabed,
                Zone.SandWet or Zone.Sand => IslandSurface.Beach,
                Zone.Grass => IslandSurface.Grass,
                Zone.JungleGrass => IslandSurface.Jungle,
                Zone.Dirt => IslandSurface.Dirt,
                Zone.Rock => IslandSurface.Rock,
                _ => IslandSurface.Volcanic,
            };
            return true;
        }

        [ContextMenu("미리 만들기")]
        void Preview() => Generate(previewSeed, previewBiome, previewRadius);

        [ContextMenu("지우기")]
        public void Clear()
        {
            for (int i = transform.childCount - 1; i >= 0; i--)
            {
                var child = transform.GetChild(i).gameObject;
                if (Application.isPlaying) Destroy(child);
                else DestroyImmediate(child);
            }
            shape = null;
        }

        public void Generate(int seed, IslandBiome biome, float radius)
        {
            Clear();
            var random = new System.Random(seed);
            shape = new Shape(random, biome, radius, shelfWidth, seabedDepth, beachWidth);
            BuildTerrain(random);
            if (catalog != null)
            {
                ScatterProps(random);
            }
        }

        // ───────────────────────── 지형 ─────────────────────────

        void BuildTerrain(System.Random random)
        {
            float half = shape.Bound;
            int cells = Mathf.CeilToInt(half * 2f / cellSize);
            float start = -cells * cellSize * 0.5f;
            int points = cells + 1;

            var grid = new Vector3[points * points];
            for (int z = 0; z < points; z++)
            {
                for (int x = 0; x < points; x++)
                {
                    float px = start + x * cellSize;
                    float pz = start + z * cellSize;
                    // 가장자리는 흔들지 않아 네모 모양을 지킨다.
                    bool edge = x == 0 || z == 0 || x == cells || z == cells;
                    if (!edge)
                    {
                        px += ((float)random.NextDouble() * 2f - 1f) * jitter * cellSize;
                        pz += ((float)random.NextDouble() * 2f - 1f) * jitter * cellSize;
                    }
                    grid[z * points + x] = new Vector3(px, shape.Height(px, pz), pz);
                }
            }

            int triangleCount = cells * cells * 2;
            var vertices = new Vector3[triangleCount * 3];
            var normals = new Vector3[vertices.Length];
            var uvs = new Vector2[vertices.Length];
            var indices = new int[vertices.Length];
            triangleZones = new Zone[triangleCount];
            int t = 0;
            for (int z = 0; z < cells; z++)
            {
                for (int x = 0; x < cells; x++)
                {
                    Vector3 a = grid[z * points + x];
                    Vector3 b = grid[z * points + x + 1];
                    Vector3 c = grid[(z + 1) * points + x];
                    Vector3 d = grid[(z + 1) * points + x + 1];
                    // 대각선 방향을 번갈아 규칙성을 깬다.
                    if (((x + z) & 1) == 0)
                    {
                        AddTriangle(a, c, b, ref t, vertices, normals, uvs, indices);
                        AddTriangle(b, c, d, ref t, vertices, normals, uvs, indices);
                    }
                    else
                    {
                        AddTriangle(a, c, d, ref t, vertices, normals, uvs, indices);
                        AddTriangle(a, d, b, ref t, vertices, normals, uvs, indices);
                    }
                }
            }

            var mesh = new Mesh { name = "IslandTerrain" };
            mesh.indexFormat = vertices.Length > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16;
            mesh.vertices = vertices;
            mesh.normals = normals;
            mesh.uv = uvs;
            mesh.triangles = indices;
            mesh.RecalculateBounds();

            var terrain = new GameObject("Terrain");
            terrain.transform.SetParent(transform, false);
            terrain.isStatic = true;
            terrain.layer = IslandLayer;
            terrain.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = terrain.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = terrainMaterial;
            terrainCollider = terrain.AddComponent<MeshCollider>();
            terrainCollider.sharedMesh = mesh;
        }

        void AddTriangle(Vector3 a, Vector3 b, Vector3 c, ref int t, Vector3[] vertices, Vector3[] normals, Vector2[] uvs, int[] indices)
        {
            Vector3 normal = Vector3.Cross(b - a, c - a).normalized;
            float slope = Vector3.Angle(normal, Vector3.up);
            Vector3 center = (a + b + c) / 3f;
            Zone zone = shape.ZoneAt(center, slope);
            triangleZones[t] = zone;
            Vector2 uv = palette != null ? palette.Uv(ZoneColors[(int)zone]) : Vector2.zero;
            int i = t * 3;
            vertices[i] = a; vertices[i + 1] = b; vertices[i + 2] = c;
            normals[i] = normals[i + 1] = normals[i + 2] = normal;
            uvs[i] = uvs[i + 1] = uvs[i + 2] = uv;
            indices[i] = i; indices[i + 1] = i + 1; indices[i + 2] = i + 2;
            t++;
        }

        // ───────────────────────── 소품 ─────────────────────────

        struct Placed
        {
            public Vector2 position;
            public float radius;
            public bool detail;
        }

        void ScatterProps(System.Random random)
        {
            Physics.SyncTransforms();
            var root = new GameObject("Props").transform;
            root.SetParent(transform, false);

            var byCategory = new Dictionary<string, List<PcgInfo>>();
            foreach (var prefab in catalog.Props)
            {
                var info = prefab != null ? prefab.GetComponent<PcgInfo>() : null;
                if (info == null || !BiomeAllowed(prefab.name, info))
                {
                    continue;
                }
                if (!byCategory.TryGetValue(info.category, out var list))
                {
                    byCategory[info.category] = list = new List<PcgInfo>();
                }
                list.Add(info);
            }

            float landArea = Mathf.PI * shape.radius * shape.radius * shape.elongationMax;
            float outer = shape.radius * shape.elongationMax + shelfWidth;
            float seaArea = Mathf.PI * outer * outer - landArea;
            var placed = new List<Placed>();

            foreach (var (category, fill) in PlacementOrder)
            {
                if (!byCategory.TryGetValue(category, out var options))
                {
                    continue;
                }
                bool sea = category == "sea_rock" || category == "reef" || category == "floating" || category == "landmark";
                int target;
                if (category == "landmark")
                {
                    target = random.NextDouble() < 0.4 ? 1 : 0;
                }
                else
                {
                    float averageRadius = 0f;
                    foreach (var o in options) averageRadius += Mathf.Max(0.3f, o.radius);
                    averageRadius /= options.Count;
                    float biomeFill = shape.biome == IslandBiome.Volcanic && category == "tree" ? 0.5f : 1f;
                    target = Mathf.RoundToInt(fill * propDensity * biomeFill * (sea ? seaArea : landArea) / (Mathf.PI * averageRadius * averageRadius));
                }
                int made = 0;
                for (int attempt = 0; attempt < target * 6 && made < target; attempt++)
                {
                    var info = options[random.Next(options.Count)];
                    if (TryPlace(info, sea, random, placed, root))
                    {
                        made++;
                    }
                }
            }
        }

        bool BiomeAllowed(string propName, PcgInfo info)
        {
            bool volcanicName = propName.Contains("Volcanic");
            switch (shape.biome)
            {
                case IslandBiome.Volcanic:
                    return info.biome == "volcanic" || info.biome == "sea" || (info.biome == "common" && (volcanicName || info.category == "detail"));
                default:
                    return info.biome == "beach" || info.biome == "jungle" || info.biome == "sea" || (info.biome == "common" && !volcanicName);
            }
        }

        bool ZoneAllowed(PcgInfo info, Zone zone)
        {
            switch (info.biome)
            {
                case "beach": return zone == Zone.Sand || zone == Zone.SandWet || (info.category == "tree" && zone == Zone.Grass);
                case "jungle": return zone == Zone.Grass || zone == Zone.JungleGrass || (info.category == "rock" && zone == Zone.Dirt);
                case "volcanic": return zone == Zone.Ash || zone == Zone.Basalt || zone == Zone.Dirt;
                case "common": return info.category == "cliff" ? zone == Zone.Rock || zone == Zone.Dirt || zone == Zone.Basalt : zone != Zone.Seabed;
                default: return true;
            }
        }

        bool TryPlace(PcgInfo info, bool sea, System.Random random, List<Placed> placed, Transform root)
        {
            float outer = shape.radius * shape.elongationMax + shelfWidth;
            float range = sea ? outer : shape.radius * shape.elongationMax * 1.1f;
            float angle = (float)random.NextDouble() * Mathf.PI * 2f;
            float distance = Mathf.Sqrt((float)random.NextDouble()) * range;
            var p = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * distance;
            float radius = Mathf.Max(0.2f, info.radius);
            bool detail = info.category == "detail";

            foreach (var other in placed)
            {
                if (detail != other.detail) continue;
                float min = detail ? radius + other.radius : Mathf.Max(radius, other.radius) + Mathf.Min(radius, other.radius) * 0.5f;
                if ((other.position - p).sqrMagnitude < min * min) return false;
            }

            // 지형 높이와 경사 (로컬)
            Vector3 top = transform.TransformPoint(new Vector3(p.x, 60f, p.y));
            bool hit = terrainCollider.Raycast(new Ray(top, -transform.up), out var hitInfo, 200f);
            float groundY = hit ? transform.InverseTransformPoint(hitInfo.point).y : -100f;
            Vector3 groundNormal = hit ? transform.InverseTransformDirection(hitInfo.normal) : Vector3.up;
            Zone zone = hit && hitInfo.triangleIndex >= 0 && hitInfo.triangleIndex < triangleZones.Length ? triangleZones[hitInfo.triangleIndex] : Zone.Seabed;

            float y;
            if (info.SitsOnWater)
            {
                // 물 위에 원점. 아래 바닥 깊이가 규칙 범위 안이어야 한다 (지형 밖은 깊은 바다).
                if (hit ? groundY < info.heightMin || groundY > info.heightMax : info.heightMin > -6f) return false;
                if (hit && groundY > -0.5f) return false;
                y = 0f;
            }
            else
            {
                if (!hit) return false;
                if (groundY < info.heightMin || groundY > info.heightMax) return false;
                if (Vector3.Angle(groundNormal, Vector3.up) > info.slopeMax) return false;
                if (!info.SitsOnSeabed && !ZoneAllowed(info, zone)) return false;
                // 절벽 소품은 해안 가까이에만 (산꼭대기에 서지 않게)
                if (info.category == "cliff" && groundY > 3.5f) return false;
                y = groundY;
            }

            float scale = Mathf.Lerp(info.scaleMin, info.scaleMax, (float)random.NextDouble());
            float yaw = (float)random.NextDouble() * 360f;
            Quaternion rotation = Quaternion.Euler(0f, yaw, 0f);
            if (info.alignNormal && !info.SitsOnWater)
            {
                rotation = Quaternion.FromToRotation(Vector3.up, groundNormal) * rotation;
            }

            var instance = Instantiate(info.gameObject, root);
            instance.name = info.gameObject.name;
            instance.transform.localPosition = new Vector3(p.x, y, p.y);
            instance.transform.localRotation = rotation;
            instance.transform.localScale = Vector3.one * scale;
            // 배가 막히는 것(지형·바위·절벽 등)은 Island 레이어. 떠다니는 것과 해저 산호는 배가 지나간다.
            if (info.category != "floating" && info.category != "reef")
            {
                SetLayer(instance.transform, IslandLayer);
            }
            placed.Add(new Placed { position = p, radius = radius * scale, detail = detail });
            return true;
        }

        // ───────────────────────── 섬 모양 ─────────────────────────

        sealed class Shape
        {
            public readonly IslandBiome biome;
            public readonly float radius;
            public readonly float elongationMax;
            readonly float elongation;
            readonly float rotation;
            readonly Vector2[] offsets = new Vector2[6];
            readonly float shelfWidth;
            readonly float seabedDepth;
            readonly float beachWidth;
            readonly float plainHeight;
            readonly float hillHeight;
            readonly bool hasCliff;
            readonly float cliffAngle;
            readonly float cliffCos;
            readonly float cliffHeight;
            readonly float coneHeight;
            readonly float craterRadius;
            readonly float craterDepth;

            public float Bound => radius * elongationMax * 1.3f + shelfWidth + 6f;

            public Shape(System.Random random, IslandBiome biome, float radius, float shelfWidth, float seabedDepth, float beachWidth)
            {
                this.biome = biome;
                this.radius = radius;
                this.shelfWidth = shelfWidth;
                this.seabedDepth = seabedDepth;
                this.beachWidth = beachWidth;
                for (int i = 0; i < offsets.Length; i++)
                {
                    offsets[i] = new Vector2((float)random.NextDouble() * 1000f, (float)random.NextDouble() * 1000f);
                }
                elongation = Mathf.Lerp(0.8f, 1.25f, (float)random.NextDouble());
                elongationMax = Mathf.Max(elongation, 1f / elongation);
                rotation = (float)random.NextDouble() * Mathf.PI * 2f;
                plainHeight = Mathf.Lerp(1.2f, 2.5f, (float)random.NextDouble());
                hillHeight = Mathf.Lerp(2.5f, 6f, (float)random.NextDouble()) * Mathf.Clamp(radius / 20f, 0.6f, 1.5f);
                hasCliff = random.NextDouble() < 0.75;
                cliffAngle = (float)random.NextDouble() * Mathf.PI * 2f;
                cliffCos = Mathf.Cos(Mathf.Lerp(35f, 65f, (float)random.NextDouble()) * Mathf.Deg2Rad);
                cliffHeight = Mathf.Lerp(3.5f, 6.5f, (float)random.NextDouble());
                bool volcanic = biome == IslandBiome.Volcanic;
                coneHeight = volcanic ? Mathf.Lerp(8f, 13f, (float)random.NextDouble()) * Mathf.Clamp(radius / 22f, 0.7f, 1.4f) : 0f;
                craterRadius = volcanic ? radius * Mathf.Lerp(0.12f, 0.18f, (float)random.NextDouble()) : 0f;
                craterDepth = volcanic ? Mathf.Lerp(1.5f, 3f, (float)random.NextDouble()) : 0f;
            }

            float Noise(int layer, float x, float y) => Mathf.PerlinNoise(x + offsets[layer].x, y + offsets[layer].y);

            float Fbm(int layer, float x, float y)
            {
                return Noise(layer, x, y) * 0.65f + Noise(layer, x * 2.1f + 17f, y * 2.1f + 31f) * 0.35f;
            }

            public float Height(float x, float z)
            {
                // 섬 축으로 돌리고 타원으로 늘인다.
                float cs = Mathf.Cos(rotation), sn = Mathf.Sin(rotation);
                float lx = (x * cs + z * sn) / elongation;
                float lz = (-x * sn + z * cs) * elongation;
                float r = Mathf.Sqrt(lx * lx + lz * lz);
                float ux = r > 0.001f ? lx / r : 1f;
                float uz = r > 0.001f ? lz / r : 0f;

                float coast = radius * (1f + 0.3f * (Noise(0, ux * 1.1f, uz * 1.1f) * 2f - 1f) + 0.14f * (Noise(1, ux * 2.4f, uz * 2.4f) * 2f - 1f) + 0.05f * (Noise(4, ux * 5f, uz * 5f) * 2f - 1f));
                float d = coast - r;
                float worldAngle = Mathf.Atan2(z, x);
                float cliff = hasCliff ? Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(cliffCos - 0.3f, 1f, Mathf.Cos(worldAngle - cliffAngle))) : 0f;

                float h;
                if (d < 0f)
                {
                    float width = Mathf.Lerp(shelfWidth, 5f, cliff);
                    float t = Mathf.Clamp01(-d / width);
                    h = Mathf.Lerp(0f, seabedDepth, Mathf.SmoothStep(0f, 1f, t));
                    // 지형 끝은 아래로 접어 바다 속에서 끝이 안 보이게 한다.
                    float edge = Mathf.InverseLerp(Bound - 6f, Bound, Mathf.Max(Mathf.Abs(x), Mathf.Abs(z)));
                    h = Mathf.Lerp(h, -14f, edge);
                }
                else
                {
                    float beach = Mathf.Lerp(beachWidth, 1.5f, cliff);
                    float beachHeight = 0.9f * Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(d / beach));
                    float inland = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((d - beach) / 8f));
                    h = Mathf.Lerp(beachHeight, plainHeight, inland);
                    float hillMask = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((d - beach - 3f) / 12f));
                    float hills = Mathf.Pow(Fbm(2, x * 0.045f, z * 0.045f), 1.6f);
                    h += hills * hillHeight * hillMask;
                    h += (Noise(3, x * 0.11f, z * 0.11f) - 0.5f) * 0.5f * inland;
                    // 절벽: 해변 바로 뒤에서 가파르게 솟는다.
                    // 안쪽으로 갈수록 언덕과 이어지게 줄인다 (섬 가운데를 가르는 벽이 생기지 않게).
                    float rise = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((d - beach * 0.6f) / 3f));
                    float fade = 1f - 0.6f * Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((d - 8f) / 14f));
                    h += cliff * cliffHeight * rise * fade;
                    if (coneHeight > 0f)
                    {
                        float cone = Mathf.Pow(Mathf.Clamp01(1f - r / (radius * 0.8f)), 1.3f) * coneHeight;
                        if (r < craterRadius)
                        {
                            float k = 1f - r / craterRadius;
                            cone -= craterDepth * k * k;
                        }
                        h += cone * Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((d - beach) / 6f));
                    }
                }
                return h;
            }

            public Zone ZoneAt(Vector3 center, float slope)
            {
                float h = center.y;
                bool volcanic = biome == IslandBiome.Volcanic;
                if (slope > 38f) return volcanic ? Zone.Basalt : Zone.Rock;
                if (h < -0.6f) return Zone.Seabed;
                if (h < 0.15f) return Zone.SandWet;
                if (h < 1.05f && slope < 28f) return volcanic ? Zone.Ash : Zone.Sand;
                if (slope > 28f) return volcanic ? Zone.Basalt : Zone.Dirt;
                if (volcanic)
                {
                    if (h > coneHeight * 0.55f) return Zone.Basalt;
                    return Noise(4, center.x * 0.07f, center.z * 0.07f) > 0.62f ? Zone.Basalt : Zone.Ash;
                }
                return Noise(5, center.x * 0.05f, center.z * 0.05f) > 0.5f ? Zone.JungleGrass : Zone.Grass;
            }
        }
    }
}
