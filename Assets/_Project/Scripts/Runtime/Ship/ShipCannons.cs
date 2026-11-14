using System.Collections.Generic;
using Letsail.Core;
using Letsail.Interaction;
using Letsail.Items;
using Letsail.Ocean;
using Letsail.Player;
using Unity.Netcode;
using UnityEngine;

namespace Letsail.Ship
{
    /// <summary>
    /// 배의 대포 6문과 포탄 바구니. 배 루트(InteractionHub 와 같은 곳)에 둔다.
    /// - 발사: 대포에 앉은 선원이 왼쪽 클릭 → 서버가 장전됐는지 확인하고 포탄을 날린다.
    ///   모든 PC 에 총구 불꽃·반동·포탄 모습을 보이고, 맞았는지와 피해는 서버가 정한다 (터진 자리 반경 안의 동물).
    /// - 장전: 쏘면 비고, 바구니에서 포탄을 집어 들고(두 손으로 나르기) 포신에서 E 로 넣는다.
    /// - 바구니: 갑판에 놓여 있고, 남은 포탄이 자리마다 하나씩 보인다. 집으면 맨 위 포탄부터 하나씩 사라지고 선원이 그 포탄을 든다.
    ///   대포에 넣으면 손에 든 포탄이 사라진다.
    /// 장전 상태와 바구니 포탄 수는 모두에게 맞춰지고, 나중에 들어온 선원도 같게 본다.
    /// </summary>
    public sealed class ShipCannons : NetworkBehaviour
    {
        [Header("구성 (야생동물·아이템 만들기 메뉴가 채운다)")]
        [SerializeField] ItemCatalog catalog;
        [SerializeField, Tooltip("포탄 아이템 에셋 이름")] string cannonBallItem = "Item_CannonBall";
        [SerializeField] GameObject muzzleFlash;
        [SerializeField, Tooltip("포탄 꼬리 (포탄에 붙어 반복)")] GameObject trailEffect;
        [SerializeField, Tooltip("배·나무·동물에 맞았을 때")] GameObject explosionEffect;
        [SerializeField, Tooltip("물에 떨어졌을 때")] GameObject splashEffect;
        [SerializeField, Tooltip("땅·바위·모래에 맞았을 때")] GameObject impactEffect;
        [SerializeField] GameObject ballModel;
        [SerializeField] GameObject basketFullModel;
        [SerializeField] GameObject basketEmptyModel;

        [Header("포탄")]
        [SerializeField, Tooltip("포구에서 나가는 속도 (m/s)")] float muzzleSpeed = 55f;
        [SerializeField, Tooltip("터진 자리 한가운데 피해")] float damage = 80f;
        [SerializeField, Tooltip("피해 반경 (m). 가장자리는 절반")] float blastRadius = 3f;
        [SerializeField, Tooltip("포탄이 날아가는 최대 시간 (초)")] float maxFlight = 6f;
        [SerializeField] float gravity = 9.81f;

        [Header("바구니")]
        [SerializeField, Tooltip("바구니 자리 (배 기준, 갑판 위)")] Vector3[] basketPositions = { new Vector3(-1.3f, 2.08f, 1.9f), new Vector3(1.3f, 2.08f, 1.9f) };
        [SerializeField] int basketCapacity = 5;

        readonly NetworkVariable<byte> loadedMask = new NetworkVariable<byte>(0x3F);
        NetworkList<byte> basketCounts;

        CannonControl[] cannons = new CannonControl[0];
        Interactable[] seatInteractables = new Interactable[0];
        Interactable[] loadPoints = new Interactable[0];
        InteractionHub hub;
        readonly List<GameObject> basketObjects = new List<GameObject>();
        readonly List<Interactable> basketInteractables = new List<Interactable>();
        readonly List<GameObject> basketVisuals = new List<GameObject>();

        struct Ball
        {
            public Vector3 position;
            public Vector3 velocity;
            public float age;
            public Transform visual;
        }

        readonly List<Ball> serverBalls = new List<Ball>();
        readonly List<Ball> visualBalls = new List<Ball>();

        static float SeaLevel => OceanSystem.Instance != null ? OceanSystem.Instance.SeaLevel : 0f;

        /// <summary>서버: 포탄이 터졌을 때 (자리, 맞힌 수). 물보라·소리 등을 붙일 때 쓴다.</summary>
        public event System.Action<Vector3, int> Exploded;

        void Awake()
        {
            basketCounts = new NetworkList<byte>();
            hub = GetComponent<InteractionHub>();
            var found = new List<CannonControl>(GetComponentsInChildren<CannonControl>(true));
            found.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
            cannons = found.ToArray();
            seatInteractables = new Interactable[cannons.Length];
            loadPoints = new Interactable[cannons.Length];
            for (int i = 0; i < cannons.Length; i++)
            {
                int index = i;
                cannons[i].Index = i;
                seatInteractables[i] = cannons[i].GetComponent<Interactable>();
                // 포탄 넣는 곳: 포신 (자리와 따로 고를 수 있게 포신을 바라보게 한다)
                var barrel = cannons[i].Muzzle != null ? cannons[i].Muzzle.parent : cannons[i].transform;
                var go = new GameObject("LoadPoint");
                go.transform.SetParent(barrel, false);
                var load = go.AddComponent<Interactable>();
                load.Prompt = "포탄 넣기";
                load.Radius = 1.8f;
                load.Available = false;
                load.Interacted += who => OnLoadInteracted(index, who);
                loadPoints[i] = load;
            }
            BuildBaskets();
        }

        public override void OnNetworkSpawn()
        {
            if (IsServer && basketCounts.Count == 0)
            {
                for (int i = 0; i < basketPositions.Length; i++) basketCounts.Add((byte)basketCapacity);
            }
            basketCounts.OnListChanged += _ => RefreshBaskets();
            RefreshBaskets();
        }

        public bool IsLoaded(int index) => index >= 0 && index < 8 && (loadedMask.Value & (1 << index)) != 0;

        // ───────────────────────── 모든 PC: 상호작용 안내 ─────────────────────────

        void Update()
        {
            // 포탄을 든 선원에게만 빈 대포의 "포탄 넣기" 가 보인다 (안내는 이 PC 에서만 쓰인다).
            var hands = PlayerHands.Local;
            bool carryingBall = hands != null && hands.Held != null && hands.Held.name == cannonBallItem;
            for (int i = 0; i < loadPoints.Length; i++)
            {
                if (loadPoints[i] != null) loadPoints[i].Available = carryingBall && !IsLoaded(i);
            }
            for (int i = 0; i < basketInteractables.Count; i++)
            {
                int count = i < basketCounts.Count ? basketCounts[i] : 0;
                // 빈손이면 집기, 포탄을 들었으면 바구니에 넣기 (보스가 떨어뜨린 포탄을 채워 둔다)
                basketInteractables[i].Available = carryingBall ? count < basketCapacity : count > 0 && (hands == null || hands.Held == null);
                basketInteractables[i].Prompt = carryingBall ? $"포탄 넣기  ({count}/{basketCapacity})" : $"포탄 집기  ({count}/{basketCapacity})";
            }

            UpdateBalls(visualBalls, Time.deltaTime, false);
            if (IsServer) UpdateBalls(serverBalls, Time.deltaTime, true);
        }

        // ───────────────────────── 발사 ─────────────────────────

        /// <summary>대포에 앉은 선원의 PC: 쏘겠다고 서버에 요청한다.</summary>
        public void RequestFire(int index)
        {
            if (!IsSpawned || index < 0 || index >= cannons.Length || !IsLoaded(index)) return;
            FireRpc(index);
        }

        [Rpc(SendTo.Server)]
        void FireRpc(int index, RpcParams rpcParams = default)
        {
            if (index < 0 || index >= cannons.Length || !IsLoaded(index)) return;
            // 그 대포에 앉은 선원만 쏠 수 있다.
            if (hub != null && seatInteractables[index] != null)
            {
                int seat = hub.IndexOf(seatInteractables[index]);
                if (seat >= 0 && hub.GetOccupant(seat) != rpcParams.Receive.SenderClientId) return;
            }
            loadedMask.Value = (byte)(loadedMask.Value & ~(1 << index));
            var muzzle = cannons[index].Muzzle != null ? cannons[index].Muzzle : cannons[index].transform;
            Vector3 velocity = muzzle.forward * muzzleSpeed;
            serverBalls.Add(new Ball { position = muzzle.position, velocity = velocity });
            FiredRpc(index, muzzle.position, velocity);
        }

        [Rpc(SendTo.Everyone)]
        void FiredRpc(int index, Vector3 origin, Vector3 velocity)
        {
            if (index < 0 || index >= cannons.Length) return;
            var cannon = cannons[index];
            var muzzle = cannon.Muzzle != null ? cannon.Muzzle : cannon.transform;
            // 불꽃·연기는 이 PC 에 보이는 포구 자리에 (연기는 월드에 남는다).
            MeshEffect.Spawn(muzzleFlash, muzzle.position, muzzle.rotation, 1f);
            cannon.PlayRecoil();
            Transform visual = null;
            if (ballModel != null)
            {
                visual = Instantiate(ballModel, muzzle.position, Quaternion.LookRotation(velocity)).transform;
                foreach (var c in visual.GetComponentsInChildren<Collider>()) Destroy(c);
                // 꼬리: 포탄의 자식, +Z 가 날아가는 방향 (연기는 뒤로 뻗는다)
                if (trailEffect != null)
                {
                    var trail = Instantiate(trailEffect, visual);
                    trail.transform.localPosition = Vector3.zero;
                    trail.transform.localRotation = Quaternion.identity;
                }
            }
            // 모습은 이 PC 의 포구에서, 서버가 정한 속도로 날린다.
            visualBalls.Add(new Ball { position = muzzle.position, velocity = velocity, visual = visual });
        }

        /// <summary>포탄이 맞은 곳 종류. 이펙트를 고른다.</summary>
        enum ImpactKind : byte
        {
            /// <summary>물 (물보라)</summary>
            Water,
            /// <summary>배·나무·동물 (불덩이 폭발과 나무 파편)</summary>
            Explosion,
            /// <summary>땅·바위·모래 (흙먼지와 돌 조각)</summary>
            Ground,
        }

        void UpdateBalls(List<Ball> balls, float dt, bool server)
        {
            for (int i = balls.Count - 1; i >= 0; i--)
            {
                var ball = balls[i];
                Vector3 previous = ball.position;
                ball.velocity += Vector3.down * gravity * dt;
                ball.position += ball.velocity * dt;
                ball.age += dt;

                bool done = ball.age > maxFlight;
                Vector3 impact = ball.position;
                Vector3 normal = Vector3.up;
                ImpactKind kind = ImpactKind.Water;
                if (!done && TryHit(previous, ball.position, out var hitPoint, out var hitNormal, out var hitCollider))
                {
                    done = true;
                    impact = hitPoint;
                    normal = hitNormal;
                    kind = Classify(hitCollider);
                }
                if (!done)
                {
                    float water = OceanSystem.Instance != null ? OceanSystem.Instance.GetWaterHeight(ball.position) : SeaLevel;
                    if (ball.position.y <= water)
                    {
                        done = true;
                        impact = new Vector3(ball.position.x, water, ball.position.z);
                        kind = ImpactKind.Water;
                    }
                }
                if (ball.visual != null)
                {
                    ball.visual.position = done ? impact : ball.position;
                    if (ball.velocity.sqrMagnitude > 0.01f) ball.visual.rotation = Quaternion.LookRotation(ball.velocity);
                }
                if (done)
                {
                    if (server && ball.age <= maxFlight)
                    {
                        Explode(impact);
                        // 이펙트는 서버가 정한 자리·종류로 모든 PC 에 (각자 계산한 포탄 모습과 조금 다를 수 있다)
                        ImpactRpc((byte)kind, impact, normal);
                    }
                    if (ball.visual != null) Destroy(ball.visual.gameObject);
                    balls.RemoveAt(i);
                    continue;
                }
                balls[i] = ball;
            }
        }

        // 나무·통나무·난파선·동물은 폭발 (나무 파편), 지형·바위는 땅 충격.
        static ImpactKind Classify(Collider collider)
        {
            if (collider == null) return ImpactKind.Ground;
            if (collider.GetComponentInParent<IDamageable>() != null) return ImpactKind.Explosion;
            var info = collider.GetComponentInParent<Letsail.World.Islands.PcgInfo>();
            if (info != null && (info.category == "tree" || info.category == "log" || info.category == "landmark" || info.category == "floating"))
            {
                return ImpactKind.Explosion;
            }
            if (collider.GetComponentInParent<ShipMotion>() != null) return ImpactKind.Explosion;
            return ImpactKind.Ground;
        }

        [Rpc(SendTo.Everyone)]
        void ImpactRpc(byte kind, Vector3 point, Vector3 normal)
        {
            switch ((ImpactKind)kind)
            {
                case ImpactKind.Water:
                    // 수면 높이에 똑바로
                    MeshEffect.Spawn(splashEffect, point, Quaternion.identity);
                    break;
                case ImpactKind.Explosion:
                    // +Z = 맞은 면 바깥쪽 (쏜 쪽), 위 = 월드 위
                    Vector3 outward = Vector3.ProjectOnPlane(normal, Vector3.up).sqrMagnitude > 0.01f ? normal : Vector3.forward;
                    MeshEffect.Spawn(explosionEffect, point, Quaternion.LookRotation(outward, Vector3.up));
                    break;
                default:
                    // 위 = 지면 법선
                    MeshEffect.Spawn(impactEffect, point, Quaternion.FromToRotation(Vector3.up, normal));
                    break;
            }
        }

        // 지형·바위·동물에 맞았는지 (이 배는 빼고)
        bool TryHit(Vector3 from, Vector3 to, out Vector3 point, out Vector3 normal, out Collider collider)
        {
            point = to;
            normal = Vector3.up;
            collider = null;
            Vector3 delta = to - from;
            float length = delta.magnitude;
            if (length < 1e-4f) return false;
            int mask = ~((1 << 6) | (1 << 2));
            var hits = Physics.SphereCastAll(from, 0.18f, delta / length, length, mask, QueryTriggerInteraction.Collide);
            float best = float.MaxValue;
            bool found = false;
            foreach (var hit in hits)
            {
                if (hit.collider.transform.IsChildOf(transform)) continue;
                // 동물이 아닌 트리거(상호작용 영역 등)는 지나간다.
                if (hit.collider.isTrigger && hit.collider.GetComponentInParent<IDamageable>() == null) continue;
                if (hit.distance < best)
                {
                    best = hit.distance;
                    point = hit.distance > 0f ? hit.point : from;
                    normal = hit.distance > 0f ? hit.normal : -(delta / length);
                    collider = hit.collider;
                    found = true;
                }
            }
            return found;
        }

        void Explode(Vector3 point)
        {
            var done = new HashSet<IDamageable>();
            var hits = Physics.OverlapSphere(point, blastRadius, ~0, QueryTriggerInteraction.Collide);
            foreach (var c in hits)
            {
                var target = c.GetComponentInParent<IDamageable>();
                if (target == null || target.IsDead || !done.Add(target)) continue;
                float distance = Vector3.Distance(c.ClosestPoint(point), point);
                // 배에 놓인 유물이 대포 피해를 올린다.
                float amount = damage * (1f + ShipRelics.Bonus.cannonDamage) * Mathf.Lerp(1f, 0.5f, Mathf.Clamp01(distance / blastRadius));
                target.ServerTakeDamage(amount, point);
            }
            Exploded?.Invoke(point, done.Count);
        }

        // ───────────────────────── 장전 ─────────────────────────

        void OnLoadInteracted(int index, GameObject who)
        {
            if (who == null || !who.TryGetComponent<PlayerHands>(out var hands) || !hands.IsOwner) return;
            LoadRpc(index);
            var animator = who.GetComponent<PlayerAnimator>();
            if (animator != null) animator.PlayOnce("Cannon_Reload", holdStill: true);
        }

        [Rpc(SendTo.Server)]
        void LoadRpc(int index, RpcParams rpcParams = default)
        {
            if (index < 0 || index >= cannons.Length || IsLoaded(index)) return;
            var hands = HandsOf(rpcParams.Receive.SenderClientId);
            if (hands == null || hands.Held == null || hands.Held.name != cannonBallItem) return;
            if (Vector3.Distance(hands.transform.position, cannons[index].transform.position) > 3.5f) return;
            hands.ServerConsume();
            loadedMask.Value = (byte)(loadedMask.Value | (1 << index));
        }

        // ───────────────────────── 바구니 ─────────────────────────

        void BuildBaskets()
        {
            for (int i = 0; i < basketPositions.Length; i++)
            {
                int index = i;
                var go = new GameObject($"CannonBallBasket_{i + 1}");
                go.transform.SetParent(transform, false);
                go.transform.localPosition = basketPositions[i];
                var interactable = go.AddComponent<Interactable>();
                interactable.Radius = 1.6f;
                interactable.Interacted += who => OnBasketInteracted(index, who);
                basketObjects.Add(go);
                basketInteractables.Add(interactable);
                basketVisuals.Add(null);
            }
        }

        void RefreshBaskets()
        {
            for (int i = 0; i < basketObjects.Count; i++)
            {
                int count = i < basketCounts.Count ? basketCounts[i] : basketCapacity;
                if (basketVisuals[i] != null) Destroy(basketVisuals[i]);
                // 항상 빈 바구니 + 자리마다 포탄 하나로 그린다. 자리는 아래(01) → 테두리(02~04) → 맨 위(05) 순서라,
                // 하나씩 집으면 맨 위 포탄부터 사라진다. (빈 바구니 모델이 없을 때만 포탄 더미 모델)
                bool useEmpty = basketEmptyModel != null;
                var model = useEmpty ? basketEmptyModel : basketFullModel;
                if (model == null) continue;
                var visual = Instantiate(model, basketObjects[i].transform);
                visual.name = "Visual";
                visual.transform.localPosition = Vector3.zero;
                visual.transform.localRotation = Quaternion.identity;
                if (useEmpty && ballModel != null)
                {
                    // 빈 바구니의 자리(Slot_01~05)에 남은 수만큼 포탄을 놓는다.
                    for (int s = 0; s < count; s++)
                    {
                        Transform slot = null;
                        foreach (var t in visual.GetComponentsInChildren<Transform>(true))
                        {
                            if (t.name.EndsWith($"_Slot_{s + 1:00}")) slot = t;
                        }
                        if (slot == null) continue;
                        var ball = Instantiate(ballModel, slot);
                        ball.transform.localPosition = Vector3.zero;
                    }
                }
                foreach (var c in visual.GetComponentsInChildren<Collider>()) Destroy(c);
                basketVisuals[i] = visual;
            }
        }

        void OnBasketInteracted(int index, GameObject who)
        {
            if (who == null || !who.TryGetComponent<PlayerHands>(out var hands) || !hands.IsOwner) return;
            bool putting = hands.Held != null && hands.Held.name == cannonBallItem;
            if (putting) PutBallRpc(index);
            else TakeBallRpc(index);
            var animator = who.GetComponent<PlayerAnimator>();
            if (animator != null) animator.PlayOnce(putting ? "Carry_PutDown" : "Carry_Pickup", holdStill: true);
        }

        [Rpc(SendTo.Server)]
        void TakeBallRpc(int index, RpcParams rpcParams = default)
        {
            if (index < 0 || index >= basketCounts.Count || basketCounts[index] == 0 || catalog == null) return;
            var hands = HandsOf(rpcParams.Receive.SenderClientId);
            if (hands == null || Vector3.Distance(hands.transform.position, basketObjects[index].transform.position) > 3f) return;
            int id = catalog.IndexOf(cannonBallItem);
            if (id < 0) return;
            basketCounts[index] = (byte)(basketCounts[index] - 1);
            hands.ServerGive(id);
        }

        [Rpc(SendTo.Server)]
        void PutBallRpc(int index, RpcParams rpcParams = default)
        {
            if (index < 0 || index >= basketCounts.Count || basketCounts[index] >= basketCapacity) return;
            var hands = HandsOf(rpcParams.Receive.SenderClientId);
            if (hands == null || hands.Held == null || hands.Held.name != cannonBallItem) return;
            if (Vector3.Distance(hands.transform.position, basketObjects[index].transform.position) > 3f) return;
            hands.ServerConsume();
            basketCounts[index] = (byte)(basketCounts[index] + 1);
        }

        PlayerHands HandsOf(ulong clientId)
        {
            if (NetworkManager.ConnectedClients.TryGetValue(clientId, out var client) && client.PlayerObject != null)
            {
                var hands = client.PlayerObject.GetComponent<PlayerHands>();
                if (hands != null) return hands;
            }
            foreach (var player in Letsail.Networking.NetworkPlayer.All)
            {
                if (player != null && player.OwnerClientId == clientId) return player.GetComponent<PlayerHands>();
            }
            return null;
        }

        public override void OnDestroy()
        {
            foreach (var b in visualBalls)
            {
                if (b.visual != null) Destroy(b.visual.gameObject);
            }
            base.OnDestroy();
        }
    }
}
