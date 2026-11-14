using System.Collections.Generic;
using Letsail.Networking;
using Letsail.Ocean;
using Letsail.World.Islands;
using Unity.Netcode;
using UnityEngine;

namespace Letsail.Wildlife
{
    /// <summary>
    /// 섬마다 동물을 놓는다 (서버만 한다).
    /// 1. 섬이 만들어지면 섬마다 어떤 동물을 몇 마리, 어디에 둘지 계획을 세운다 (바이옴별 규칙, README 추천 마릿수).
    /// 2. 선원이 섬 가까이 오면 그 섬의 동물을 만들고, 멀리 떠나면 지운다 (살아 있는 것만. 잡은 동물은 다시 생기지 않는다).
    ///    모든 섬의 동물을 계속 두면 네트워크 오브젝트가 수백 개가 되므로 가까운 섬만 둔다.
    /// 항해 씬에 하나 둔다 (NetworkObject 필요).
    /// </summary>
    public sealed class WildlifeSpawner : NetworkBehaviour
    {
        enum Spot { BossLair, Beach, Shore, SkyLow, Inland, Jungle, DeepJungle, Volcanic, Perch, SkyCoast, SkyHigh, Cave, Reef, Shallow, OpenSea, Circle, SeaFloor }

        [System.Flags]
        enum Where { Tropical = 1, Volcanic = 2, Both = 3 }

        struct Rule
        {
            public string species;
            public Where where;
            public Spot spot;
            public int min;
            public int max;
            public float homeRadius;
            /// <summary>무리면 같은 집을 쓴다.</summary>
            public bool group;
            /// <summary>물고기 무리 하나의 마릿수 (최소, 최대). 0 이면 무리가 아니다.</summary>
            public int schoolMin;
            public int schoolMax;
            /// <summary>이 종이 이미 있는 섬에는 놓지 않는다 (예: 호랑이와 재규어).</summary>
            public string excludes;
            /// <summary>섬마다 이 규칙이 쓰일 확률 (0~1).</summary>
            public float chance;
            /// <summary>판타지 생물. 섬 하나에 MaxFantasyKinds 종까지만 놓는다.</summary>
            public bool fantasy;
            /// <summary>밤에만 나온다 (선원이 다가올 때 밤이면 만든다).</summary>
            public bool nightOnly;

            public Rule(string species, Where where, Spot spot, int min, int max, float homeRadius, bool group = false, int schoolMin = 0, int schoolMax = 0,
                string excludes = null, float chance = 1f, bool fantasy = false, bool nightOnly = false)
            {
                this.species = species; this.where = where; this.spot = spot; this.min = min; this.max = max;
                this.homeRadius = homeRadius; this.group = group; this.schoolMin = schoolMin; this.schoolMax = schoolMax; this.excludes = excludes;
                this.chance = chance; this.fantasy = fantasy; this.nightOnly = nightOnly;
            }
        }

        // README 의 섬 하나 기준 추천 마릿수
        static readonly Rule[] Rules =
        {
            // 섬 보스 (섬마다 하나, 한가운데 근처)
            new Rule("Tropical", Where.Tropical, Spot.BossLair, 1, 1, 10f),
            new Rule("Volcanic", Where.Volcanic, Spot.BossLair, 1, 1, 10f),
            // 해변 (화산섬은 조금)
            new Rule("Crab", Where.Tropical, Spot.Beach, 4, 8, 6f),
            new Rule("Crab", Where.Volcanic, Spot.Beach, 2, 4, 6f),
            new Rule("Seagull", Where.Both, Spot.SkyCoast, 3, 6, 0f),
            new Rule("Turtle", Where.Tropical, Spot.Beach, 0, 2, 8f),
            new Rule("Pelican", Where.Tropical, Spot.SkyCoast, 1, 3, 0f),
            new Rule("SeaLion", Where.Tropical, Spot.Beach, 0, 4, 6f, group: true),
            // 정글
            new Rule("Parrot", Where.Tropical, Spot.Perch, 2, 5, 0f),
            new Rule("Toucan", Where.Tropical, Spot.Perch, 1, 3, 0f),
            new Rule("Monkey", Where.Tropical, Spot.Jungle, 2, 4, 10f, group: true),
            new Rule("Frog", Where.Tropical, Spot.Jungle, 3, 6, 4f),
            new Rule("Snake", Where.Tropical, Spot.Jungle, 0, 2, 5f),
            new Rule("Boar", Where.Tropical, Spot.Jungle, 1, 3, 15f),
            new Rule("Jaguar", Where.Tropical, Spot.DeepJungle, 0, 1, 20f),
            // 큰 동물 (섬이 작아 README 추천보다 조금 적게)
            new Rule("Elephant", Where.Tropical, Spot.Jungle, 0, 2, 15f, group: true),
            new Rule("Giraffe", Where.Tropical, Spot.Jungle, 1, 3, 15f, group: true),
            new Rule("Lion", Where.Tropical, Spot.Jungle, 0, 3, 14f, group: true),
            new Rule("Rhino", Where.Tropical, Spot.Jungle, 0, 1, 14f),
            new Rule("Gorilla", Where.Tropical, Spot.Jungle, 1, 3, 10f, group: true),
            new Rule("Tiger", Where.Tropical, Spot.DeepJungle, 0, 1, 18f, excludes: "Jaguar"),
            new Rule("Hippo", Where.Tropical, Spot.Shore, 0, 2, 8f, group: true),
            new Rule("Crocodile", Where.Tropical, Spot.Shore, 0, 2, 8f),
            // 화산
            new Rule("Lizard", Where.Volcanic, Spot.Volcanic, 3, 6, 8f),
            new Rule("Bat", Where.Volcanic, Spot.Cave, 3, 8, 0f, group: true),
            new Rule("LavaBeetle", Where.Volcanic, Spot.Volcanic, 2, 5, 5f),
            new Rule("Vulture", Where.Volcanic, Spot.SkyHigh, 1, 3, 0f),
            new Rule("GiantTortoise", Where.Volcanic, Spot.Volcanic, 1, 2, 15f),
            new Rule("Bear", Where.Volcanic, Spot.Volcanic, 0, 2, 12f),
            new Rule("Komodo", Where.Volcanic, Spot.Volcanic, 1, 2, 10f),
            // 바다
            new Rule("Fish_Striped", Where.Both, Spot.Reef, 2, 3, 6f, schoolMin: 5, schoolMax: 12),
            new Rule("Fish_Tang", Where.Both, Spot.Shallow, 1, 3, 8f, schoolMin: 6, schoolMax: 15),
            new Rule("Jellyfish", Where.Both, Spot.Shallow, 3, 6, 6f),
            new Rule("Dolphin", Where.Both, Spot.Circle, 2, 4, 0f, group: true),
            new Rule("Shark", Where.Both, Spot.Circle, 0, 1, 0f),
            new Rule("MantaRay", Where.Both, Spot.OpenSea, 0, 2, 18f),
            new Rule("Octopus", Where.Both, Spot.SeaFloor, 0, 1, 4f),
            // 판타지 생물 (드물게, 섬 하나에 2종까지)
            new Rule("BarrelCrab", Where.Both, Spot.Beach, 1, 1, 6f, chance: 0.5f, fantasy: true),
            new Rule("TideSnail", Where.Both, Spot.Shore, 1, 1, 5f, chance: 0.4f, fantasy: true),
            new Rule("BloomToad", Where.Tropical, Spot.Jungle, 1, 1, 5f, chance: 0.5f, fantasy: true),
            new Rule("Mossback", Where.Tropical, Spot.Jungle, 1, 1, 12f, chance: 0.3f, fantasy: true),
            new Rule("EmberGolem", Where.Volcanic, Spot.Volcanic, 1, 1, 6f, chance: 0.3f, fantasy: true),
            new Rule("MagmaSalamander", Where.Volcanic, Spot.Volcanic, 1, 1, 8f, chance: 0.5f, fantasy: true),
            new Rule("AshHound", Where.Volcanic, Spot.Volcanic, 2, 3, 12f, group: true, chance: 0.35f, fantasy: true),
            new Rule("CinderMoth", Where.Volcanic, Spot.SkyLow, 1, 2, 0f, chance: 0.5f, fantasy: true),
            new Rule("LanternAngler", Where.Both, Spot.OpenSea, 1, 1, 15f, chance: 0.5f, fantasy: true, nightOnly: true),
        };

        const int MaxFantasyKinds = 2;

        sealed class Entry
        {
            public GameObject prefab;
            public bool nightOnly;
            public Vector3 position;
            public float yaw;
            public Vector3 home;
            public float homeRadius;
            public int schoolCount;
            public bool alive = true;
            public NetworkObject spawned;
        }

        sealed class IslandPlan
        {
            public IslandGenerator island;
            public readonly List<Entry> entries = new List<Entry>();
            public List<Vector3> perches;
            /// <summary>박쥐 무리가 매달릴 가지 끝 (마지막으로 고른 나무).</summary>
            public List<Vector3> hangPoints;
            public bool active;
        }

        [SerializeField] WildlifeCatalog catalog;
        [SerializeField, Tooltip("선원이 섬(얕은 바닥 바깥)에서 이만큼 안에 오면 동물을 만든다 (m)")] float activateDistance = 140f;
        [SerializeField, Tooltip("이만큼 멀어지면 지운다 (m)")] float deactivateDistance = 260f;
        [SerializeField, Range(0f, 2f), Tooltip("마릿수 배율")] float density = 1f;

        readonly List<IslandPlan> plans = new List<IslandPlan>();
        IslandWorld world;
        int plannedSeed;
        float checkTimer;

        static float SeaLevel => OceanSystem.Instance != null ? OceanSystem.Instance.SeaLevel : 0f;

        /// <summary>지금 살아 있는(만들어진) 동물 수 (테스트용).</summary>
        public int SpawnedCount
        {
            get
            {
                int n = 0;
                foreach (var p in plans) foreach (var e in p.entries) if (e.spawned != null && e.spawned.IsSpawned) n++;
                return n;
            }
        }

        /// <summary>계획한 동물 수 (테스트용).</summary>
        public int PlannedCount
        {
            get
            {
                int n = 0;
                foreach (var p in plans) n += p.entries.Count;
                return n;
            }
        }

        public override void OnNetworkDespawn()
        {
            if (!IsServer) return;
            foreach (var p in plans) Deactivate(p);
        }

        void Update()
        {
            if (!IsServer || !IsSpawned || catalog == null) return;
            if (world == null) world = FindAnyObjectByType<IslandWorld>();
            if (world == null || world.Islands.Count == 0) return;
            if (plannedSeed != world.Seed)
            {
                foreach (var p in plans) Deactivate(p);
                plans.Clear();
                plannedSeed = world.Seed;
                Plan();
            }
            checkTimer -= Time.deltaTime;
            if (checkTimer > 0f) return;
            checkTimer = 1f;
            foreach (var plan in plans)
            {
                if (plan.island == null) continue;
                float distance = NearestPlayer(plan.island.transform.position) - plan.island.OuterRadius;
                if (!plan.active && distance < activateDistance) Activate(plan);
                else if (plan.active && distance > deactivateDistance) Deactivate(plan);
            }
        }

        // ───────────────────────── 계획 ─────────────────────────

        void Plan()
        {
            for (int i = 0; i < world.Islands.Count; i++)
            {
                var island = world.Islands[i];
                if (island == null) continue;
                var random = new System.Random(unchecked(world.Seed * 131 + i * 977));
                var plan = new IslandPlan { island = island, perches = FindPerches(island) };
                var placedSpecies = new HashSet<string>();
                Where where = island.Biome == IslandBiome.Volcanic ? Where.Volcanic : Where.Tropical;
                // 판타지 생물은 섬마다 순서를 섞어 고르고, MaxFantasyKinds 종까지만 놓는다.
                var order = new List<Rule>();
                var fantasyRules = new List<Rule>();
                foreach (var r in Rules) (r.fantasy ? fantasyRules : order).Add(r);
                for (int k = fantasyRules.Count - 1; k > 0; k--)
                {
                    int swap = random.Next(k + 1);
                    (fantasyRules[k], fantasyRules[swap]) = (fantasyRules[swap], fantasyRules[k]);
                }
                order.AddRange(fantasyRules);
                int fantasyKinds = 0;
                foreach (var rule in order)
                {
                    if ((rule.where & where) == 0) continue;
                    if (!string.IsNullOrEmpty(rule.excludes) && placedSpecies.Contains(rule.excludes)) continue;
                    if (rule.fantasy && fantasyKinds >= MaxFantasyKinds) continue;
                    if (random.NextDouble() > rule.chance) continue;
                    var prefab = catalog.Find(rule.species);
                    if (prefab == null) continue;
                    int count = Mathf.RoundToInt(random.Next(rule.min, rule.max + 1) * density);
                    Vector3 groupHome = Vector3.zero;
                    bool haveGroupHome = false;
                    for (int n = 0; n < count; n++)
                    {
                        Vector3 home;
                        if (rule.group && haveGroupHome)
                        {
                            home = groupHome;
                        }
                        else if (!TryPickSpot(island, plan, rule, random, out home))
                        {
                            continue;
                        }
                        if (rule.group && !haveGroupHome)
                        {
                            groupHome = home;
                            haveGroupHome = true;
                        }
                        var entry = MakeEntry(island, plan, rule, prefab, home, random);
                        if (entry != null)
                        {
                            entry.nightOnly = rule.nightOnly;
                            plan.entries.Add(entry);
                            if (rule.fantasy && !placedSpecies.Contains(rule.species)) fantasyKinds++;
                            placedSpecies.Add(rule.species);
                        }
                    }
                }
                plans.Add(plan);
            }
        }

        Entry MakeEntry(IslandGenerator island, IslandPlan plan, Rule rule, GameObject prefab, Vector3 home, System.Random random)
        {
            var entry = new Entry
            {
                prefab = prefab,
                home = home,
                homeRadius = rule.homeRadius,
                yaw = (float)random.NextDouble() * 360f,
            };
            Vector3 center = island.transform.position;
            switch (rule.spot)
            {
                case Spot.SkyCoast:
                case Spot.SkyHigh:
                {
                    // 섬 가운데를 도는 원. HomeRadius 가 원의 반지름이고, 처음 높이에서 돈다.
                    bool high = rule.spot == Spot.SkyHigh;
                    entry.home = center;
                    entry.homeRadius = island.Radius * (high ? Lerp(0.25f, 0.5f, random) : Lerp(0.7f, 1.05f, random));
                    float angle = (float)random.NextDouble() * Mathf.PI * 2f;
                    entry.position = center + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * entry.homeRadius;
                    entry.position.y = SeaLevel + (high ? Lerp(15f, 25f, random) : Lerp(6f, 12f, random));
                    return entry;
                }
                case Spot.SkyLow:
                {
                    // 섬 가운데(분화구) 위 낮게 돈다 (불나방). 높이는 그 아래 지면 기준.
                    entry.home = center;
                    entry.homeRadius = island.Radius * Lerp(0.15f, 0.35f, random);
                    float angle = (float)random.NextDouble() * Mathf.PI * 2f;
                    entry.position = center + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * entry.homeRadius;
                    float ground = island.SampleGround(entry.position, out Vector3 g, out _, out _, out _) ? g.y : SeaLevel;
                    entry.position.y = Mathf.Max(ground, SeaLevel) + Lerp(4f, 9f, random);
                    return entry;
                }
                case Spot.Circle:
                {
                    // 섬 둘레를 돈다 (돌고래, 상어).
                    entry.home = new Vector3(center.x, SeaLevel, center.z);
                    entry.homeRadius = island.OuterRadius + Lerp(5f, 25f, random);
                    float angle = (float)random.NextDouble() * Mathf.PI * 2f;
                    entry.position = entry.home + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * entry.homeRadius;
                    entry.position.y = SeaLevel - 1.5f;
                    return entry;
                }
                case Spot.Reef:
                case Spot.Shallow:
                case Spot.OpenSea:
                {
                    var info = prefab.GetComponent<WildInfo>();
                    float depth = info != null ? Lerp(info.depthMin, info.depthMax, random) : -2f;
                    entry.position = new Vector3(home.x, SeaLevel + depth, home.z);
                    if (island.SampleGround(entry.position, out Vector3 floor, out _, out _, out _)) entry.position.y = Mathf.Max(entry.position.y, floor.y + 0.7f);
                    entry.position.y = Mathf.Min(entry.position.y, SeaLevel - 0.5f);
                    entry.home = entry.position;
                    if (rule.schoolMax > 0) entry.schoolCount = random.Next(rule.schoolMin, rule.schoolMax + 1);
                    return entry;
                }
                case Spot.Perch:
                    entry.position = home;
                    return entry;
                case Spot.Cave:
                {
                    // 같은 나무의 다른 가지에 매달린다 (가지가 모자라면 조금씩 떨어뜨린다).
                    if (plan.hangPoints != null && plan.hangPoints.Count > 0)
                    {
                        int index = random.Next(plan.hangPoints.Count);
                        entry.home = plan.hangPoints[index];
                        if (plan.hangPoints.Count > 1) plan.hangPoints.RemoveAt(index);
                    }
                    else
                    {
                        entry.home = home + new Vector3(Lerp(-0.7f, 0.7f, random), Lerp(-0.4f, 0.3f, random), Lerp(-0.7f, 0.7f, random));
                    }
                    entry.position = entry.home;
                    return entry;
                }
                default:
                {
                    // 땅: 무리면 집 근처에 조금씩 흩어 놓는다.
                    Vector3 p = home;
                    if (rule.group)
                    {
                        for (int i = 0; i < 6; i++)
                        {
                            Vector3 q = home + new Vector3(Lerp(-1f, 1f, random), 0f, Lerp(-1f, 1f, random)) * Mathf.Max(1.5f, rule.homeRadius * 0.4f);
                            if (island.SampleGround(q, out Vector3 g, out float h, out float s, out _) && h > -0.3f && s < 32f)
                            {
                                p = g;
                                break;
                            }
                        }
                    }
                    entry.position = p;
                    return entry;
                }
            }
        }

        bool TryPickSpot(IslandGenerator island, IslandPlan plan, Rule rule, System.Random random, out Vector3 spot)
        {
            spot = Vector3.zero;
            Vector3 center = island.transform.position;
            float radius = island.Radius;
            switch (rule.spot)
            {
                case Spot.Perch:
                    if (plan.perches == null || plan.perches.Count == 0) return false;
                    spot = plan.perches[random.Next(plan.perches.Count)];
                    return true;
                case Spot.SkyCoast:
                case Spot.SkyHigh:
                case Spot.SkyLow:
                case Spot.Circle:
                    spot = center;
                    return true;
                case Spot.Cave:
                    return TryPickCave(island, plan, random, out spot);
            }

            for (int attempt = 0; attempt < 40; attempt++)
            {
                float angle = (float)random.NextDouble() * Mathf.PI * 2f;
                float distance;
                switch (rule.spot)
                {
                    case Spot.Beach: distance = radius * Lerp(0.6f, 1.15f, random); break;
                    case Spot.Shore: distance = radius * Lerp(0.85f, 1.15f, random); break;
                    case Spot.BossLair: distance = radius * Lerp(0f, 0.4f, random); break;
                    case Spot.DeepJungle: distance = radius * Lerp(0f, 0.45f, random); break;
                    case Spot.Reef:
                    case Spot.Shallow: distance = radius * Lerp(0.95f, 1.25f, random); break;
                    case Spot.OpenSea: distance = island.OuterRadius + Lerp(0f, 15f, random); break;
                    case Spot.SeaFloor: distance = radius * Lerp(1f, 1.3f, random); break;
                    default: distance = radius * Lerp(0f, 0.85f, random); break;
                }
                Vector3 p = center + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * distance;
                bool hit = island.SampleGround(p, out Vector3 ground, out float height, out float slope, out IslandSurface surface);
                bool volcanic = island.Biome == IslandBiome.Volcanic;
                switch (rule.spot)
                {
                    case Spot.BossLair:
                        // 보스: 섬 가운데 근처의 평평한 내륙 (싸울 자리가 있게)
                        if (hit && height > 1.2f && slope < 22f && surface != IslandSurface.Beach && surface != IslandSurface.Rock)
                        {
                            spot = ground;
                            return true;
                        }
                        break;
                    case Spot.Shore:
                        // 물가 모래 (물에 바로 들어갈 수 있는 곳)
                        if (hit && height > -0.25f && height < 0.5f && slope < 22f && (surface == IslandSurface.Beach || (volcanic && surface == IslandSurface.Volcanic)))
                        {
                            spot = ground;
                            return true;
                        }
                        break;
                    case Spot.Beach:
                        if (hit && height > -0.2f && height < 1.6f && slope < 25f && (surface == IslandSurface.Beach || (volcanic && surface == IslandSurface.Volcanic)))
                        {
                            spot = ground;
                            return true;
                        }
                        break;
                    case Spot.Jungle:
                    case Spot.Inland:
                    case Spot.DeepJungle:
                        if (hit && height > (rule.spot == Spot.DeepJungle ? 2.5f : 0.9f) && slope < 30f
                            && (surface == IslandSurface.Grass || surface == IslandSurface.Jungle || surface == IslandSurface.Dirt))
                        {
                            spot = ground;
                            return true;
                        }
                        break;
                    case Spot.Volcanic:
                        if (hit && height > 0.4f && slope < 30f && (surface == IslandSurface.Volcanic || surface == IslandSurface.Dirt))
                        {
                            spot = ground;
                            return true;
                        }
                        break;
                    case Spot.Reef:
                    case Spot.Shallow:
                        // 물이 1.5m 이상 깊은 얕은 바다 (산호가 있으면 그 근처)
                        if (rule.spot == Spot.Reef && TryNearReef(island, random, out spot)) return true;
                        if (!hit || height < -1.8f)
                        {
                            spot = new Vector3(p.x, SeaLevel, p.z);
                            return true;
                        }
                        break;
                    case Spot.OpenSea:
                        spot = new Vector3(p.x, SeaLevel, p.z);
                        return true;
                    case Spot.SeaFloor:
                        if (hit && height < -2f && height > -9f)
                        {
                            spot = ground;
                            return true;
                        }
                        break;
                }
            }
            return false;
        }

        bool TryNearReef(IslandGenerator island, System.Random random, out Vector3 spot)
        {
            spot = Vector3.zero;
            var props = island.transform.Find("Props");
            if (props == null) return false;
            var reefs = new List<Vector3>();
            foreach (Transform t in props)
            {
                var info = t.GetComponent<PcgInfo>();
                if (info != null && info.category == "reef") reefs.Add(t.position);
            }
            // 물이 1.8m 이상 깊은 산호 근처만 (물고기가 수면에 뜨지 않게)
            for (int attempt = 0; attempt < 8 && reefs.Count > 0; attempt++)
            {
                int index = random.Next(reefs.Count);
                Vector3 r = reefs[index];
                reefs.RemoveAt(index);
                if (island.SampleGround(r, out Vector3 floor, out _, out _, out _) && SeaLevel - floor.y >= 1.8f)
                {
                    spot = new Vector3(r.x, SeaLevel, r.z);
                    return true;
                }
            }
            return false;
        }

        bool TryPickCave(IslandGenerator island, IslandPlan plan, System.Random random, out Vector3 spot)
        {
            // 박쥐: 마른 나무 가지 끝 바로 아래에 매단다 (메시의 높은 꼭짓점 중 줄기에서 떨어진 곳).
            spot = Vector3.zero;
            var props = island.transform.Find("Props");
            if (props == null) return false;
            var trees = new List<Transform>();
            foreach (Transform t in props)
            {
                var info = t.GetComponent<PcgInfo>();
                if (info != null && info.category == "tree") trees.Add(t);
            }
            while (trees.Count > 0)
            {
                int index = random.Next(trees.Count);
                var points = BranchTips(trees[index]);
                trees.RemoveAt(index);
                if (points.Count >= 2)
                {
                    plan.hangPoints = points;
                    spot = points[random.Next(points.Count)];
                    return true;
                }
            }
            plan.hangPoints = null;
            // 나무가 없으면 바위 옆면 위쪽에 매단다.
            var rocks = new List<Renderer>();
            foreach (Transform t in props)
            {
                var info = t.GetComponent<PcgInfo>();
                if (info == null || (info.category != "rock" && info.category != "cliff")) continue;
                var r = t.GetComponentInChildren<Renderer>();
                if (r != null && r.bounds.size.y > 2f) rocks.Add(r);
            }
            if (rocks.Count == 0) return false;
            var rock = rocks[random.Next(rocks.Count)];
            var b = rock.bounds;
            Vector3 outward = new Vector3(b.center.x - island.transform.position.x, 0f, b.center.z - island.transform.position.z).normalized;
            spot = new Vector3(b.center.x, b.min.y + b.size.y * 0.75f, b.center.z) + outward * (Mathf.Min(b.extents.x, b.extents.z) * 0.9f);
            return true;
        }

        // 나무 메시의 위쪽 30% 꼭짓점 중 줄기에서 0.5m 넘게 떨어진 곳 (가지 끝). 그 바로 아래가 매달리는 자리.
        static List<Vector3> BranchTips(Transform tree)
        {
            var list = new List<Vector3>();
            foreach (var filter in tree.GetComponentsInChildren<MeshFilter>())
            {
                var mesh = filter.sharedMesh;
                if (mesh == null || !mesh.isReadable) continue;
                var vertices = mesh.vertices;
                float minY = float.MaxValue, maxY = float.MinValue;
                var world = new Vector3[vertices.Length];
                for (int i = 0; i < vertices.Length; i++)
                {
                    world[i] = filter.transform.TransformPoint(vertices[i]);
                    minY = Mathf.Min(minY, world[i].y);
                    maxY = Mathf.Max(maxY, world[i].y);
                }
                float cut = Mathf.Lerp(minY, maxY, 0.6f);
                foreach (var p in world)
                {
                    float radial = new Vector2(p.x - tree.position.x, p.z - tree.position.z).magnitude;
                    if (p.y < cut || p.y > maxY - 0.2f || radial < 0.5f) continue;
                    bool near = false;
                    foreach (var q in list) near |= (q - p).sqrMagnitude < 0.25f;
                    if (!near) list.Add(p - Vector3.up * 0.1f);
                }
            }
            return list;
        }

        static List<Vector3> FindPerches(IslandGenerator island)
        {
            // 나무 꼭대기 (잎 사이)
            var list = new List<Vector3>();
            var props = island.transform.Find("Props");
            if (props == null) return list;
            foreach (Transform t in props)
            {
                var info = t.GetComponent<PcgInfo>();
                if (info == null || info.category != "tree") continue;
                var r = t.GetComponentInChildren<Renderer>();
                if (r == null || r.bounds.size.y < 3f) continue;
                var b = r.bounds;
                list.Add(new Vector3(t.position.x, b.max.y - Mathf.Min(0.6f, b.size.y * 0.1f), t.position.z));
            }
            return list;
        }

        // ───────────────────────── 만들기·지우기 ─────────────────────────

        void Activate(IslandPlan plan)
        {
            plan.active = true;
            foreach (var entry in plan.entries)
            {
                if (!entry.alive || (entry.spawned != null && entry.spawned.IsSpawned)) continue;
                // 밤에만 나오는 생물은 다가올 때 밤일 때만 만든다.
                if (entry.nightOnly && (Letsail.World.SkyController.Instance == null || !Letsail.World.SkyController.Instance.IsNight)) continue;
                var go = Instantiate(entry.prefab, entry.position, Quaternion.Euler(0f, entry.yaw, 0f));
                var animal = go.GetComponent<Animal>();
                if (animal != null)
                {
                    animal.Island = plan.island;
                    animal.Home = entry.home;
                    animal.HomeRadius = entry.homeRadius;
                    animal.Perches = plan.perches;
                    var captured = entry;
                    animal.ServerDied += _ => captured.alive = false;
                }
                var school = go.GetComponent<FishSchool>();
                if (school != null)
                {
                    school.Island = plan.island;
                    school.Home = entry.home;
                    school.HomeRadius = entry.homeRadius;
                    school.SetCount(entry.schoolCount);
                    var captured = entry;
                    school.ServerDied += _ => captured.alive = false;
                }
                var networkObject = go.GetComponent<NetworkObject>();
                networkObject.Spawn(true);
                entry.spawned = networkObject;
            }
        }

        void Deactivate(IslandPlan plan)
        {
            plan.active = false;
            foreach (var entry in plan.entries)
            {
                if (entry.spawned != null && entry.spawned.IsSpawned)
                {
                    // 다친 채로 떠나도 다음에 오면 다시 멀쩡하다 (지금은 체력을 기억하지 않는다).
                    entry.spawned.Despawn(true);
                }
                entry.spawned = null;
            }
        }

        static float Lerp(float a, float b, System.Random random) => a + (float)random.NextDouble() * (b - a);

        static float NearestPlayer(Vector3 position)
        {
            float best = float.MaxValue;
            foreach (var p in NetworkPlayer.All)
            {
                if (p == null) continue;
                float d = Vector3.Distance(new Vector3(p.transform.position.x, 0f, p.transform.position.z), new Vector3(position.x, 0f, position.z));
                if (d < best) best = d;
            }
            return best;
        }
    }
}
