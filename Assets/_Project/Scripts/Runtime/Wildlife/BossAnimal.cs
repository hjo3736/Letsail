using System.Collections.Generic;
using Letsail.Items;
using Letsail.Ocean;
using Unity.Netcode;
using UnityEngine;

namespace Letsail.Wildlife
{
    /// <summary>
    /// 섬 보스. Animal 과 같은 오브젝트에 둔다 (섬마다 하나, WildlifeSpawner 가 놓는다).
    /// - 체력 비율을 모두에게 보낸다 (화면 위 보스 체력바, BossHealthUI).
    /// - 쓰러지면 주변에 보상 아이템을 잔뜩 흩뿌리고, 한가운데에 그 섬의 유물을 떨어뜨린다.
    ///   유물은 배 선장실 선반에 두면 그 판 동안 선원 전체에 효과가 있다 (ShipRelics).
    /// </summary>
    [RequireComponent(typeof(Animal))]
    public sealed class BossAnimal : NetworkBehaviour
    {
        [System.Serializable]
        public struct Loot
        {
            [Tooltip("아이템 에셋 이름 (예: Item_CannonBall)")] public string item;
            public int min;
            public int max;
        }

        [SerializeField, Tooltip("화면에 보이는 보스 이름")] string displayName = "섬의 주인";
        [SerializeField] Loot[] loot;
        [SerializeField, Tooltip("떨어뜨리는 유물 (아이템 에셋 이름)")] string relicItem;
        [SerializeField] ItemCatalog catalog;
        [SerializeField] WorldItem worldItemPrefab;
        [SerializeField, Tooltip("보상을 흩뿌리는 반경 (m)")] float lootRadius = 3.5f;

        readonly NetworkVariable<float> health01 = new NetworkVariable<float>(1f);

        static readonly List<BossAnimal> all = new List<BossAnimal>();

        Animal animal;

        /// <summary>지금 있는 보스들 (모든 PC).</summary>
        public static IReadOnlyList<BossAnimal> All => all;

        public string DisplayName => displayName;
        public float Health01 => health01.Value;
        public bool IsDead => animal != null && animal.IsDead;

        void Awake()
        {
            animal = GetComponent<Animal>();
        }

        public override void OnNetworkSpawn()
        {
            all.Add(this);
            if (IsServer) animal.ServerDied += OnDied;
        }

        public override void OnNetworkDespawn()
        {
            all.Remove(this);
            if (animal != null) animal.ServerDied -= OnDied;
        }

        void Update()
        {
            if (!IsServer || !IsSpawned || animal == null) return;
            float value = animal.MaxHealth > 0f ? Mathf.Clamp01(animal.Health / animal.MaxHealth) : 0f;
            if (Mathf.Abs(value - health01.Value) > 0.001f) health01.Value = value;
        }

        void OnDied(Animal _)
        {
            health01.Value = 0f;
            if (catalog == null || worldItemPrefab == null) return;
            var random = new System.Random(unchecked((int)NetworkObjectId * 131 + 7));
            Vector3 center = transform.position;
            var drops = new List<int>();
            foreach (var entry in loot)
            {
                int id = catalog.IndexOf(entry.item);
                if (id < 0) continue;
                int count = random.Next(entry.min, entry.max + 1);
                for (int i = 0; i < count; i++) drops.Add(id);
            }
            // 둘레에 고르게 흩뿌린다.
            for (int i = 0; i < drops.Count; i++)
            {
                float angle = (i + (float)random.NextDouble() * 0.5f) / Mathf.Max(1, drops.Count) * Mathf.PI * 2f;
                float distance = lootRadius * (0.5f + 0.5f * (float)random.NextDouble());
                Vector3 p = center + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * distance;
                WorldItem.ServerSpawn(worldItemPrefab, drops[i], GroundAt(p), (float)random.NextDouble() * 360f);
            }
            int relic = catalog.IndexOf(relicItem);
            if (relic >= 0) WorldItem.ServerSpawn(worldItemPrefab, relic, GroundAt(center), 0f);
        }

        static Vector3 GroundAt(Vector3 p)
        {
            float sea = OceanSystem.Instance != null ? OceanSystem.Instance.SeaLevel : 0f;
            int mask = ~((1 << 6) | (1 << LayerMaskIndex.Animal) | (1 << 2));
            if (Physics.Raycast(p + Vector3.up * 6f, Vector3.down, out var hit, 20f, mask, QueryTriggerInteraction.Ignore) && hit.point.y > sea - 0.3f)
            {
                return hit.point;
            }
            return new Vector3(p.x, sea, p.z);
        }
    }
}
