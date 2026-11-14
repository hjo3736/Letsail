using System.Collections.Generic;
using Letsail.Core;
using Letsail.Items;
using Letsail.Networking;
using Letsail.Player;
using Unity.Netcode;
using UnityEngine;

namespace Letsail.Ship
{
    /// <summary>
    /// 선장실 선반의 유물 자리 (Shelf_Cabin_Slot_01~04). 배 루트에 둔다.
    /// 유물을 들고 빈 자리에서 E 를 누르면 놓이고, 놓여 있는 동안 그 판의 선원 전체에 효과가 있다 (여러 유물은 더해진다).
    /// 놓인 유물은 빈손으로 E 를 눌러 다시 집을 수 있다 (집으면 효과가 꺼진다).
    /// 어느 자리에 어떤 유물이 있는지는 모두에게 맞춰지고, 효과(Bonus)는 서버가 피해·체력 계산에 쓴다.
    /// </summary>
    public sealed class ShipRelics : NetworkBehaviour
    {
        [SerializeField] ItemCatalog catalog;
        [SerializeField, Tooltip("선반 자리 이름 앞부분")] string slotPrefix = "Shelf_Cabin_Slot_";

        NetworkList<int> slots;
        readonly List<Transform> slotPoints = new List<Transform>();
        readonly List<Interactable> slotInteractables = new List<Interactable>();
        readonly List<GameObject> slotVisuals = new List<GameObject>();

        static ShipRelics active;

        /// <summary>지금 배에 놓인 유물들의 효과 합. 배가 없으면 모두 0.</summary>
        public static RelicBonus Bonus => active != null ? active.bonus : default;

        RelicBonus bonus;

        void Awake()
        {
            slots = new NetworkList<int>();
            var found = new List<Transform>();
            foreach (var t in GetComponentsInChildren<Transform>(true))
            {
                if (t.name.StartsWith(slotPrefix)) found.Add(t);
            }
            found.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
            for (int i = 0; i < found.Count; i++)
            {
                int index = i;
                slotPoints.Add(found[i]);
                var go = new GameObject("RelicSlot");
                go.transform.SetParent(found[i], false);
                var interactable = go.AddComponent<Interactable>();
                interactable.Radius = 1.6f;
                interactable.Available = false;
                interactable.Interacted += who => OnSlotInteracted(index, who);
                slotInteractables.Add(interactable);
                slotVisuals.Add(null);
            }
        }

        public override void OnNetworkSpawn()
        {
            active = this;
            if (IsServer && slots.Count == 0)
            {
                for (int i = 0; i < slotPoints.Count; i++) slots.Add(-1);
            }
            slots.OnListChanged += _ => Refresh();
            Refresh();
        }

        public override void OnNetworkDespawn()
        {
            if (active == this) active = null;
        }

        void Update()
        {
            // 이 PC 의 선원 손에 따라 안내: 유물을 들었으면 빈 자리에 "놓기", 빈손이면 놓인 유물 "집기".
            var hands = PlayerHands.Local;
            var held = hands != null ? hands.Held : null;
            bool carryingRelic = held != null && held.kind == ItemKind.Relic;
            for (int i = 0; i < slotInteractables.Count; i++)
            {
                int id = i < slots.Count ? slots[i] : -1;
                var placed = catalog != null ? catalog.Get(id) : null;
                var interactable = slotInteractables[i];
                if (placed == null)
                {
                    interactable.Available = carryingRelic;
                    interactable.Prompt = carryingRelic ? $"{held.displayName} 놓기  ·  {held.relic.description}" : "";
                }
                else
                {
                    interactable.Available = held == null;
                    interactable.Prompt = $"{placed.displayName} 집기  (놓여 있는 동안: {placed.relic.description})";
                }
            }
        }

        void Refresh()
        {
            var sum = new RelicBonus();
            var names = new List<string>();
            for (int i = 0; i < slotPoints.Count; i++)
            {
                int id = i < slots.Count ? slots[i] : -1;
                var item = catalog != null ? catalog.Get(id) : null;
                if (slotVisuals[i] != null) Destroy(slotVisuals[i]);
                slotVisuals[i] = null;
                if (item == null) continue;
                sum.cannonDamage += item.relic.cannonDamage;
                sum.weaponDamage += item.relic.weaponDamage;
                sum.healthRegen += item.relic.healthRegen;
                sum.maxHealth += item.relic.maxHealth;
                names.Add(item.displayName);
                if (item.model != null)
                {
                    var visual = Instantiate(item.model, slotPoints[i]);
                    visual.transform.localPosition = Vector3.up * item.restHeight;
                    visual.transform.localRotation = Quaternion.identity;
                    foreach (var c in visual.GetComponentsInChildren<Collider>()) Destroy(c);
                    slotVisuals[i] = visual;
                }
            }
            sum.description = names.Count > 0 ? string.Join(", ", names) : "";
            bonus = sum;
        }

        void OnSlotInteracted(int index, GameObject who)
        {
            if (who == null || !who.TryGetComponent<PlayerHands>(out var hands) || !hands.IsOwner) return;
            if (hands.Held != null && hands.Held.kind == ItemKind.Relic) PlaceRpc(index);
            else if (hands.Held == null) TakeRpc(index);
            var animator = who.GetComponent<PlayerAnimator>();
            if (animator != null) animator.PlayOnce(hands.Held != null ? "Carry_PutDown" : "Carry_Pickup", holdStill: true);
        }

        [Rpc(SendTo.Server)]
        void PlaceRpc(int index, RpcParams rpcParams = default)
        {
            if (index < 0 || index >= slots.Count || slots[index] >= 0) return;
            var hands = HandsOf(rpcParams.Receive.SenderClientId);
            if (hands == null || hands.Held == null || hands.Held.kind != ItemKind.Relic) return;
            if (Vector3.Distance(hands.transform.position, slotPoints[index].position) > 3f) return;
            int id = catalog.IndexOf(hands.Held);
            hands.ServerConsume();
            slots[index] = id;
        }

        [Rpc(SendTo.Server)]
        void TakeRpc(int index, RpcParams rpcParams = default)
        {
            if (index < 0 || index >= slots.Count || slots[index] < 0) return;
            var hands = HandsOf(rpcParams.Receive.SenderClientId);
            if (hands == null || hands.Held != null) return;
            if (Vector3.Distance(hands.transform.position, slotPoints[index].position) > 3f) return;
            int id = slots[index];
            slots[index] = -1;
            hands.ServerGive(id);
        }

        PlayerHands HandsOf(ulong clientId)
        {
            foreach (var player in NetworkPlayer.All)
            {
                if (player != null && player.OwnerClientId == clientId) return player.GetComponent<PlayerHands>();
            }
            return null;
        }
    }
}
