using System.Collections.Generic;
using Letsail.Ocean;
using Unity.Netcode;
using UnityEngine;

namespace Letsail.World.Islands
{
    /// <summary>
    /// 매 판 섬들을 새로 만든다. 호스트가 시드 하나를 정해 보내고, 모든 PC 가 같은 시드로 똑같은 섬을 각자 만든다
    /// (섬과 소품은 네트워크 오브젝트가 아니다). 나중에 들어온 사람도 시드를 받아 같은 섬을 만든다.
    /// 섬 자리는 배 출발점(이 오브젝트 위치)에서 일정 거리 이상 떨어뜨리고, 섬끼리도 겹치지 않게 한다.
    /// 항해 씬에 하나 둔다 (NetworkObject 가 필요하다).
    /// </summary>
    public sealed class IslandWorld : NetworkBehaviour
    {
        [SerializeField, Tooltip("섬 하나의 틀 (IslandGenerator 가 붙은 프리팹)")] IslandGenerator islandPrefab;
        [SerializeField, Tooltip("섬 개수 (최소, 최대)")] Vector2Int islandCount = new Vector2Int(4, 6);
        [SerializeField, Tooltip("섬 해안선 반지름 범위 (m)")] Vector2 islandRadius = new Vector2(40f, 70f);
        [SerializeField, Range(0f, 1f), Tooltip("화산섬이 나올 확률")] float volcanicChance = 0.3f;
        [SerializeField, Tooltip("섬을 놓는 거리 범위 (출발점에서, m)")] Vector2 spawnDistance = new Vector2(150f, 1100f);
        [SerializeField, Tooltip("섬끼리 띄우는 최소 간격 (얕은 바닥 바깥끼리, m)")] float islandGap = 60f;
        [SerializeField, Tooltip("0 이 아니면 매 판 이 시드로 고정 (테스트용)")] int fixedSeed;

        readonly NetworkVariable<int> seed = new NetworkVariable<int>();
        readonly List<IslandGenerator> islands = new List<IslandGenerator>();
        int builtSeed;

        public IReadOnlyList<IslandGenerator> Islands => islands;
        public int Seed => seed.Value;

        public override void OnNetworkSpawn()
        {
            if (IsServer)
            {
                seed.Value = fixedSeed != 0 ? fixedSeed : Random.Range(1, int.MaxValue);
            }
            seed.OnValueChanged += OnSeedChanged;
            Build(seed.Value);
        }

        public override void OnNetworkDespawn()
        {
            seed.OnValueChanged -= OnSeedChanged;
        }

        void OnSeedChanged(int previous, int current) => Build(current);

        /// <summary>이 시드로 섬들을 만든다 (이미 같은 시드로 만들었으면 그대로 둔다).</summary>
        public void Build(int worldSeed)
        {
            if (worldSeed == 0 || worldSeed == builtSeed || islandPrefab == null)
            {
                return;
            }
            Clear();
            builtSeed = worldSeed;
            var random = new System.Random(worldSeed);
            float seaLevel = OceanSystem.Instance != null ? OceanSystem.Instance.SeaLevel : 0f;
            int count = random.Next(islandCount.x, islandCount.y + 1);
            var spots = new List<(Vector2 position, float outer)>();
            for (int i = 0; i < count; i++)
            {
                float radius = Mathf.Lerp(islandRadius.x, islandRadius.y, (float)random.NextDouble());
                var biome = random.NextDouble() < volcanicChance ? IslandBiome.Volcanic : IslandBiome.Tropical;
                int islandSeed = random.Next();
                // 대략 바깥 반지름 (해안선 노이즈와 얕은 바닥 포함)
                float outer = radius * 1.6f + 20f;
                Vector2 spot = Vector2.zero;
                bool found = false;
                for (int attempt = 0; attempt < 120 && !found; attempt++)
                {
                    float angle = (float)random.NextDouble() * Mathf.PI * 2f;
                    float distance = Mathf.Lerp(spawnDistance.x + outer, spawnDistance.y, Mathf.Sqrt((float)random.NextDouble()));
                    spot = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * distance;
                    found = true;
                    foreach (var other in spots)
                    {
                        if ((other.position - spot).magnitude < other.outer + outer + islandGap)
                        {
                            found = false;
                            break;
                        }
                    }
                }
                if (!found)
                {
                    continue;
                }
                spots.Add((spot, outer));
                var island = Instantiate(islandPrefab, transform);
                island.name = $"Island_{i + 1:00}_{biome}";
                island.transform.position = new Vector3(transform.position.x + spot.x, seaLevel, transform.position.z + spot.y);
                island.transform.rotation = Quaternion.Euler(0f, (float)random.NextDouble() * 360f, 0f);
                island.Generate(islandSeed, biome, radius);
                islands.Add(island);
            }
        }

        void Clear()
        {
            foreach (var island in islands)
            {
                if (island != null) Destroy(island.gameObject);
            }
            islands.Clear();
            builtSeed = 0;
        }
    }
}
