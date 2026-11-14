using Letsail.Core;
using Letsail.Player;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Letsail.Items
{
    /// <summary>
    /// 선원이 손에 든 아이템 하나 (무기 또는 고기).
    /// - 바닥의 아이템을 E 로 주우면 손에 든다. 들고 있던 것은 그 자리에 내려놓는다.
    /// - 왼쪽 클릭: 무기로 공격. 빈손이면 주먹. 근접 무기는 앞쪽을, 총은 화면 가운데를 맞힌다.
    /// - G: 들고 있는 것을 앞에 내려놓는다.
    /// - 고기 같은 큰 것은 두 손으로 나르고(Carry 동작), 나르는 동안은 공격하지 못한다.
    /// 무엇을 들었는지는 서버가 정해 모두에게 보낸다. 공격 판정은 주인 PC 가 하고, 서버가 거리를 확인한 뒤 피해를 준다.
    /// 플레이어 프리팹에 둔다.
    /// </summary>
    public sealed class PlayerHands : NetworkBehaviour
    {
        [SerializeField] ItemCatalog catalog;
        [SerializeField, Tooltip("바닥에 놓는 아이템 프리팹")] WorldItem worldItemPrefab;
        [SerializeField, Tooltip("손 소켓 이름 (캐릭터 뼈 아래)")] string socketName = "Socket_Hand_R";

        [Header("맨손")]
        [SerializeField] string punchAction = "Punch";
        [SerializeField] float punchDamage = 5f;
        [SerializeField] float punchRange = 1.2f;
        [SerializeField] float punchCooldown = 0.7f;

        [Header("판정")]
        [SerializeField, Tooltip("근접 공격이 닿는 폭 (m)")] float meleeRadius = 0.7f;
        [SerializeField, Tooltip("서버가 공격 거리를 확인할 때 더 봐주는 거리 (m, 지연 때문에)")] float rangeTolerance = 3f;

        readonly NetworkVariable<int> held = new NetworkVariable<int>(-1);

        PlayerAnimator playerAnimator;
        PlayerMotor motor;
        PlayerInteractor interactor;
        Transform socket;
        Transform socketLeft;
        GameObject visual;
        int shownId = int.MinValue;
        InputAction attackAction;
        float nextAttackTime;
        float pendingHitTime = -1f;
        ItemDefinition pendingItem;

        /// <summary>이 PC 의 선원 손.</summary>
        public static PlayerHands Local { get; private set; }

        /// <summary>들고 있는 아이템. 빈손이면 null.</summary>
        public ItemDefinition Held => catalog != null ? catalog.Get(held.Value) : null;

        public bool IsCarrying => Held != null && Held.kind == ItemKind.Carry;

        /// <summary>화면 안내 (들고 있을 때 조작법). 빈손이면 null.</summary>
        public string ControlsHint
        {
            get
            {
                var item = Held;
                if (item == null) return null;
                return item.kind == ItemKind.Carry
                    ? $"{item.displayName} 나르는 중  ·  G 내려놓기"
                    : $"{item.displayName}  ·  좌클릭 공격  ·  G 내려놓기";
            }
        }

        void Awake()
        {
            playerAnimator = GetComponent<PlayerAnimator>();
            motor = GetComponent<PlayerMotor>();
            interactor = GetComponent<PlayerInteractor>();
            socket = FindChild(transform, socketName);
            socketLeft = FindChild(transform, "Socket_Hand_L");
        }

        void LateUpdate()
        {
            if (visual == null || !IsCarrying || socketLeft == null || socket == null) return;
            Vector3 middle = (socket.position + socketLeft.position) * 0.5f;
            visual.transform.SetPositionAndRotation(middle - Vector3.up * 0.05f, Quaternion.LookRotation(transform.forward, Vector3.up));
        }

        public override void OnNetworkSpawn()
        {
            held.OnValueChanged += (_, _) => RefreshVisual();
            RefreshVisual();
            if (IsOwner)
            {
                Local = this;
                attackAction = InputSystem.actions.FindAction("Player/Attack", true);
            }
        }

        public override void OnNetworkDespawn()
        {
            if (Local == this) Local = null;
        }

        static Transform FindChild(Transform root, string name)
        {
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
            {
                if (t.name == name) return t;
            }
            return null;
        }

        void Update()
        {
            if (playerAnimator != null)
            {
                playerAnimator.Carrying = IsCarrying;
            }
            if (!IsSpawned || !IsOwner) return;

            if (pendingHitTime > 0f && Time.time >= pendingHitTime)
            {
                pendingHitTime = -1f;
                ResolveHit(pendingItem);
            }

            bool busy = interactor != null && (interactor.Busy || interactor.Seat != null);
            if (busy || Cursor.lockState != CursorLockMode.Locked) return;

            if (Keyboard.current != null && Keyboard.current.gKey.wasPressedThisFrame && held.Value >= 0)
            {
                DropRpc(transform.position, transform.forward);
                if (playerAnimator != null) playerAnimator.PlayOnce(IsCarrying ? "Carry_PutDown" : "Pickup", holdStill: true);
                return;
            }

            if (attackAction != null && attackAction.WasPressedThisFrame() && Time.time >= nextAttackTime)
            {
                TryAttack();
            }
        }

        // ───────────────────────── 공격 ─────────────────────────

        void TryAttack()
        {
            var item = Held;
            if (item != null && item.kind == ItemKind.Carry) return;
            if (item != null && item.kind == ItemKind.Gun && motor != null && motor.IsSwimming) return;

            string action = item != null ? item.attackAction : punchAction;
            float cooldown = item != null ? item.cooldown : punchCooldown;
            float length = playerAnimator != null ? playerAnimator.GetClipLength(action) : 0.5f;
            float followLength = item != null && !string.IsNullOrEmpty(item.followAction) && playerAnimator != null ? playerAnimator.GetClipLength(item.followAction) : 0f;
            nextAttackTime = Time.time + Mathf.Max(cooldown, length + followLength + 0.05f);

            // 카메라가 보는 쪽을 보고 공격한다.
            var cam = Camera.main;
            if (cam != null)
            {
                Vector3 look = Vector3.ProjectOnPlane(cam.transform.forward, Vector3.up);
                if (look.sqrMagnitude > 0.001f) transform.rotation = Quaternion.LookRotation(look.normalized, Vector3.up);
            }
            if (playerAnimator != null)
            {
                playerAnimator.PlayOnce(action);
                if (followLength > 0f) playerAnimator.QueueOnce(item.followAction);
            }
            float hitMoment = item != null ? item.hitMoment : 0.4f;
            pendingItem = item;
            pendingHitTime = Time.time + length * hitMoment;
        }

        void ResolveHit(ItemDefinition item)
        {
            bool gun = item != null && item.kind == ItemKind.Gun;
            float damage = item != null ? item.damage : punchDamage;
            float range = item != null ? item.range : punchRange;
            int animalMask = 1 << LayerMaskIndex.Animal;

            if (gun)
            {
                // 모든 PC 에서 총구 불꽃
                ShotRpc();
                var cam = Camera.main;
                if (cam == null) return;
                // 화면 가운데로 쏜다. 카메라 뒤쪽(캐릭터 사이)은 빼고 캐릭터 앞부터 잰다.
                Ray ray = cam.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
                float skip = Vector3.Dot(transform.position - ray.origin, ray.direction);
                int mask = ~((1 << 6) | (1 << 2));
                if (Physics.Raycast(ray.origin + ray.direction * Mathf.Max(0f, skip), ray.direction, out var hit, range, mask, QueryTriggerInteraction.Collide))
                {
                    var target = hit.collider.GetComponentInParent<IDamageable>() as NetworkBehaviour;
                    if (target != null) DamageRpc(target.NetworkObject, damage, hit.point, true);
                }
                return;
            }

            Vector3 center = transform.position + Vector3.up * 0.9f + transform.forward * (range * 0.6f);
            var hits = Physics.OverlapSphere(center, Mathf.Max(meleeRadius, range * 0.5f), animalMask, QueryTriggerInteraction.Collide);
            NetworkBehaviour best = null;
            float bestDistance = float.MaxValue;
            foreach (var c in hits)
            {
                var target = c.GetComponentInParent<IDamageable>();
                if (target == null || target.IsDead) continue;
                float d = (c.ClosestPoint(center) - center).sqrMagnitude;
                if (d < bestDistance && target is NetworkBehaviour nb)
                {
                    best = nb;
                    bestDistance = d;
                }
            }
            if (best != null) DamageRpc(best.NetworkObject, damage, center, false);
        }

        [Rpc(SendTo.Everyone)]
        void ShotRpc()
        {
            var item = Held;
            if (item == null || item.fireEffect == null || visual == null) return;
            // 총구를 찾는다. 아이템은 +Y 가 총구 방향이라 이펙트(+Z 가 쏘는 방향)를 -90도 돌린다.
            Transform muzzle = null;
            if (!string.IsNullOrEmpty(item.muzzleName))
            {
                foreach (var t in visual.GetComponentsInChildren<Transform>(true))
                {
                    if (t.name == item.muzzleName)
                    {
                        muzzle = t;
                        break;
                    }
                }
            }
            if (muzzle == null) muzzle = visual.transform;
            MuzzleFlash.Spawn(item.fireEffect, muzzle.position, muzzle.rotation * Quaternion.Euler(-90f, 0f, 0f), item.fireEffectScale);
        }

        [Rpc(SendTo.Server)]
        void DamageRpc(NetworkObjectReference targetRef, float damage, Vector3 hitPoint, bool gun)
        {
            if (!targetRef.TryGet(out NetworkObject targetObject)) return;
            var target = targetObject.GetComponentInChildren<IDamageable>();
            if (target == null || target.IsDead) return;
            var item = Held;
            // 들고 있는 무기와 맞는지, 거리가 말이 되는지 확인한다.
            bool holdingGun = item != null && item.kind == ItemKind.Gun;
            if (gun != holdingGun) return;
            float maxDamage = item != null && item.kind != ItemKind.Carry ? item.damage : punchDamage;
            float range = item != null && item.kind != ItemKind.Carry ? item.range : punchRange;
            if (Vector3.Distance(transform.position, targetObject.transform.position) > range + rangeTolerance) return;
            target.ServerTakeDamage(Mathf.Min(damage, maxDamage), transform.position);
        }

        // ───────────────────────── 줍기·내려놓기 ─────────────────────────

        /// <summary>주인 PC: 이 아이템을 줍겠다고 서버에 요청한다.</summary>
        public void RequestPickup(WorldItem item)
        {
            if (!IsOwner || item == null) return;
            PickupRpc(item.NetworkObject);
            if (playerAnimator != null)
            {
                var def = item.Definition;
                playerAnimator.PlayOnce(def != null && def.kind == ItemKind.Carry ? "Carry_Pickup" : "Pickup", holdStill: true);
            }
        }

        [Rpc(SendTo.Server)]
        void PickupRpc(NetworkObjectReference itemRef)
        {
            if (!itemRef.TryGet(out NetworkObject itemObject)) return;
            var item = itemObject.GetComponent<WorldItem>();
            if (item == null || Vector3.Distance(item.transform.position, transform.position) > 3.5f) return;
            int id = item.ItemId;
            if (!item.ServerTake()) return;
            // 들고 있던 것은 그 자리에 내려놓는다.
            if (held.Value >= 0) ServerDrop(transform.position, transform.forward);
            held.Value = id;
        }

        [Rpc(SendTo.Server)]
        void DropRpc(Vector3 position, Vector3 forward)
        {
            if (held.Value < 0) return;
            // 주인이 보낸 위치가 실제 위치와 너무 다르면 서버 위치를 쓴다.
            if (Vector3.Distance(position, transform.position) > 3f) position = transform.position;
            ServerDrop(position, forward);
        }

        void ServerDrop(Vector3 position, Vector3 forward)
        {
            int id = held.Value;
            held.Value = -1;
            if (worldItemPrefab == null || id < 0) return;
            forward = Vector3.ProjectOnPlane(forward, Vector3.up).sqrMagnitude > 0.001f ? Vector3.ProjectOnPlane(forward, Vector3.up).normalized : Vector3.forward;
            Vector3 point = WorldItem.FindDropPoint(position, forward);
            WorldItem.ServerSpawn(worldItemPrefab, id, point, Quaternion.LookRotation(forward).eulerAngles.y + 90f);
        }

        /// <summary>서버: 손에 바로 쥐여 준다 (시작 장비 등). 들고 있던 것은 내려놓는다.</summary>
        public void ServerGive(int id)
        {
            if (!IsServer) return;
            if (held.Value >= 0) ServerDrop(transform.position, transform.forward);
            held.Value = id;
        }

        // ───────────────────────── 모습 ─────────────────────────

        void RefreshVisual()
        {
            if (shownId == held.Value) return;
            shownId = held.Value;
            if (visual != null) Destroy(visual);
            var item = Held;
            if (item == null || item.model == null || socket == null) return;
            // 나르는 것은 두 손 사이에 세워 든다 (LateUpdate 에서 자리를 맞춘다).
            bool carry = item.kind == ItemKind.Carry && socketLeft != null;
            visual = Instantiate(item.model, carry ? transform : socket);
            visual.name = "Held_" + item.name;
            visual.transform.localPosition = Vector3.zero;
            visual.transform.localRotation = Quaternion.identity;
            foreach (var c in visual.GetComponentsInChildren<Collider>()) Destroy(c);
            int layer = gameObject.layer;
            foreach (var t in visual.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = layer;
        }
    }
}
