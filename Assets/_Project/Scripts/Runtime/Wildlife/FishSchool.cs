using System.Collections.Generic;
using Letsail.Core;
using Letsail.Items;
using Letsail.Networking;
using Letsail.Ocean;
using Letsail.World.Islands;
using Unity.Netcode;
using UnityEngine;

namespace Letsail.Wildlife
{
    /// <summary>
    /// 물고기 무리. 무리 하나가 네트워크 오브젝트 하나다 (서버가 무리 가운데를 움직인다).
    /// 물고기 한 마리 한 마리는 각 PC 가 같은 규칙(번호로 정한 자리 + 흔들림)으로 그린다.
    /// 한 번 맞을 때마다 한 마리가 죽고 생선을 떨어뜨린다 (수면에 뜬다). 다 잡으면 무리가 사라진다.
    /// 선원이 가까이 오면 빠르게 멀어진다.
    /// </summary>
    public sealed class FishSchool : NetworkBehaviour, IDamageable
    {
        [SerializeField] WildInfo info;
        [SerializeField, Tooltip("물고기 한 마리 모델 (FBX)")] GameObject fishModel;
        [SerializeField] RuntimeAnimatorController controller;
        [SerializeField, Tooltip("무리가 퍼지는 반경 (m)")] float spread = 1.2f;
        [SerializeField] float hitRadius = 0.18f;
        [SerializeField] string meatItem = "Item_Fish";
        [SerializeField] ItemCatalog catalog;
        [SerializeField] WorldItem worldItemPrefab;
        [SerializeField] float activeRadius = 120f;

        readonly NetworkVariable<byte> total = new NetworkVariable<byte>(8);
        readonly NetworkVariable<byte> remaining = new NetworkVariable<byte>(8);
        readonly NetworkVariable<bool> fast = new NetworkVariable<bool>();

        readonly List<(Transform t, Animator animator, Vector3 offset, float phase)> fish = new List<(Transform, Animator, Vector3, float)>();
        bool shownFast;
        int shownCount = -1;

        // 서버
        float orbitAngle;
        float orbitRadius;
        float depth;
        float retargetTimer;
        float fleeTimer;
        Vector3 threat;
        bool despawning;
        System.Random random;

        public IslandGenerator Island { get; set; }
        public Vector3 Home { get; set; }
        public float HomeRadius { get; set; } = 10f;

        /// <summary>서버: 다 잡혔을 때.</summary>
        public event System.Action<FishSchool> ServerDied;

        public bool IsDead => remaining.Value == 0;

        static float SeaLevel => OceanSystem.Instance != null ? OceanSystem.Instance.SeaLevel : 0f;

        /// <summary>서버: 스폰 전에 마릿수를 정한다.</summary>
        public void SetCount(int count)
        {
            count = Mathf.Clamp(count, 1, 30);
            total.Value = (byte)count;
            remaining.Value = (byte)count;
        }

        public override void OnNetworkSpawn()
        {
            BuildFish();
            remaining.OnValueChanged += (_, _) => RefreshCount();
            fast.OnValueChanged += (_, _) => RefreshAnimation();
            RefreshCount();
            RefreshAnimation();
            if (!IsServer) return;
            random = new System.Random(unchecked((int)NetworkObjectId * 31 + 7));
            orbitAngle = (float)random.NextDouble() * Mathf.PI * 2f;
            orbitRadius = Mathf.Max(3f, HomeRadius * 0.6f);
            PickDepth();
        }

        void BuildFish()
        {
            if (fishModel == null) return;
            // 번호로 자리를 정한다: 모든 PC 가 같은 모양의 무리를 그린다.
            var r = new System.Random(unchecked((int)NetworkObjectId * 97 + 1));
            for (int i = 0; i < total.Value; i++)
            {
                var go = Instantiate(fishModel, transform);
                go.name = "Fish_" + i;
                var offset = new Vector3((float)r.NextDouble() * 2f - 1f, ((float)r.NextDouble() * 2f - 1f) * 0.4f, (float)r.NextDouble() * 2f - 1f) * spread;
                go.transform.localPosition = offset;
                var animator = go.GetComponentInChildren<Animator>();
                if (animator != null && controller != null) animator.runtimeAnimatorController = controller;
                var sphere = go.AddComponent<SphereCollider>();
                sphere.isTrigger = true;
                sphere.radius = hitRadius;
                go.layer = LayerMaskIndex.Animal;
                fish.Add((go.transform, animator, offset, (float)r.NextDouble() * 10f));
            }
        }

        void RefreshCount()
        {
            if (shownCount == remaining.Value) return;
            shownCount = remaining.Value;
            for (int i = 0; i < fish.Count; i++) fish[i].t.gameObject.SetActive(i < remaining.Value);
        }

        void RefreshAnimation()
        {
            shownFast = fast.Value;
            foreach (var f in fish)
            {
                if (f.animator != null && f.animator.runtimeAnimatorController != null)
                {
                    f.animator.CrossFadeInFixedTime(shownFast ? "SwimFast" : "Swim", 0.2f, 0, f.phase % 1f);
                }
            }
        }

        void LateUpdate()
        {
            // 한 마리씩 무리 안에서 흔들린다.
            float t = Time.time;
            foreach (var f in fish)
            {
                if (!f.t.gameObject.activeSelf) continue;
                Vector3 wobble = new Vector3(Mathf.Sin(t * 0.7f + f.phase), Mathf.Sin(t * 1.1f + f.phase * 1.3f) * 0.3f, Mathf.Sin(t * 0.9f + f.phase * 0.7f)) * 0.25f;
                f.t.localPosition = f.offset + wobble;
            }
        }

        void Update()
        {
            if (!IsServer || !IsSpawned || despawning) return;
            float dt = Time.deltaTime;
            float nearest = NearestPlayer(out Vector3 player);
            if (nearest > activeRadius) return;
            float flee = info != null ? Mathf.Max(info.fleeRadius, 2f) : 3f;
            if (nearest < flee)
            {
                threat = player;
                fleeTimer = 2.5f;
            }
            fleeTimer -= dt;
            bool scared = fleeTimer > 0f;
            if (fast.Value != scared) fast.Value = scared;

            float speed = info != null ? Mathf.Max(0.5f, info.speed) : 1.5f;
            Vector3 position = transform.position;
            Vector3 direction;
            if (scared)
            {
                direction = position - threat;
                direction.y = 0f;
                if (direction.sqrMagnitude < 0.01f) direction = transform.forward;
                direction.Normalize();
                speed *= 2.5f;
            }
            else
            {
                retargetTimer -= dt;
                if (retargetTimer <= 0f)
                {
                    retargetTimer = 5f + (float)random.NextDouble() * 8f;
                    orbitRadius = Mathf.Clamp(orbitRadius + ((float)random.NextDouble() * 2f - 1f) * 3f, 2f, Mathf.Max(3f, HomeRadius));
                    PickDepth();
                }
                orbitAngle += speed / Mathf.Max(2f, orbitRadius) * dt;
                Vector3 wanted = new Vector3(Home.x + Mathf.Cos(orbitAngle) * orbitRadius, depth, Home.z + Mathf.Sin(orbitAngle) * orbitRadius);
                direction = (wanted - position).normalized;
            }
            Vector3 next = position + direction * speed * dt;
            float floor = FloorAt(next);
            float top = SeaLevel - 0.5f;
            if (floor > top - 0.6f)
            {
                // 너무 얕다: 섬 반대쪽(깊은 곳)으로 빠져나가고, 집도 그만큼 옮긴다.
                Vector3 outward = Island != null ? Flat(position - Island.transform.position).normalized : -direction;
                Home += outward * speed * dt;
                next = position + outward * speed * dt;
                floor = FloorAt(next);
            }
            next.y = Mathf.Min(Mathf.Max(next.y, floor + 0.6f), top);
            if (Flat(next - Home).magnitude > HomeRadius * 2.5f) next = position + (Home - position).normalized * speed * dt;
            Vector3 move = next - position;
            if (move.sqrMagnitude > 1e-6f) transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(move.normalized), 180f * dt);
            transform.position = next;
        }

        void PickDepth()
        {
            float min = info != null ? info.depthMin : -4f;
            float max = info != null ? info.depthMax : -1f;
            depth = SeaLevel + min + (float)random.NextDouble() * (max - min);
        }

        public void ServerTakeDamage(float amount, Vector3 from)
        {
            if (!IsServer || remaining.Value == 0) return;
            remaining.Value = (byte)(remaining.Value - 1);
            threat = from;
            fleeTimer = 4f;
            // 생선은 수면에 뜬다.
            if (catalog != null && worldItemPrefab != null)
            {
                int id = catalog.IndexOf(meatItem);
                if (id >= 0)
                {
                    Vector3 p = transform.position;
                    p.y = SeaLevel;
                    WorldItem.ServerSpawn(worldItemPrefab, id, p, (float)random.NextDouble() * 360f);
                }
            }
            if (remaining.Value == 0)
            {
                despawning = true;
                ServerDied?.Invoke(this);
                NetworkObject.Despawn(true);
            }
        }

        float FloorAt(Vector3 p)
        {
            if (Island != null && Island.SampleGround(p, out Vector3 ground, out _, out _, out _)) return ground.y;
            return SeaLevel - 30f;
        }

        static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);

        float NearestPlayer(out Vector3 position)
        {
            float best = float.MaxValue;
            position = transform.position + Vector3.forward * 1000f;
            foreach (var p in NetworkPlayer.All)
            {
                if (p == null) continue;
                float d = Vector3.Distance(p.transform.position, transform.position);
                if (d < best)
                {
                    best = d;
                    position = p.transform.position;
                }
            }
            return best;
        }
    }
}
