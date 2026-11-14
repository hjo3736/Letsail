using Letsail.Core;
using Letsail.Ocean;
using Letsail.Ship;
using Unity.Netcode;
using UnityEngine;

namespace Letsail.Items
{
    /// <summary>
    /// 바닥에 놓인 아이템 (무기, 고기). E 로 주우면 손에 든다 (PlayerHands).
    /// 서버가 만들고 지운다. 어떤 아이템인지(번호)와 놓인 자리만 네트워크로 보낸다.
    /// 배 위에 놓이면 배 기준 자리를 보내서, 배가 움직여도 갑판에 붙어 있다.
    /// </summary>
    [RequireComponent(typeof(Interactable))]
    public sealed class WorldItem : NetworkBehaviour
    {
        struct Placement : INetworkSerializable, System.IEquatable<Placement>
        {
            public NetworkObjectReference Ship;
            public bool OnShip;
            public Vector3 Position;
            public float Yaw;

            public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
            {
                serializer.SerializeValue(ref OnShip);
                if (OnShip) serializer.SerializeValue(ref Ship);
                serializer.SerializeValue(ref Position);
                serializer.SerializeValue(ref Yaw);
            }

            public bool Equals(Placement o) => OnShip == o.OnShip && Position == o.Position && Yaw == o.Yaw && (!OnShip || Ship.Equals(o.Ship));
        }

        [SerializeField] ItemCatalog catalog;

        readonly NetworkVariable<int> itemId = new NetworkVariable<int>(-1);
        readonly NetworkVariable<Placement> placement = new NetworkVariable<Placement>();

        Interactable interactable;
        GameObject visual;
        int shownId = -1;
        bool taken;

        public ItemDefinition Definition => catalog != null ? catalog.Get(itemId.Value) : null;
        public int ItemId => itemId.Value;

        void Awake()
        {
            interactable = GetComponent<Interactable>();
            interactable.Interacted += OnInteracted;
            interactable.Radius = 1.7f;
        }

        public override void OnNetworkSpawn()
        {
            itemId.OnValueChanged += (_, _) => RefreshVisual();
            RefreshVisual();
            ApplyPlacement();
        }

        /// <summary>서버: 아이템을 이 자리에 놓는다. 배 위면 배에 붙인다.</summary>
        public static WorldItem ServerSpawn(WorldItem prefab, int id, Vector3 position, float yaw)
        {
            var item = Instantiate(prefab, position, Quaternion.Euler(0f, yaw, 0f));
            item.itemId.Value = id;
            var place = new Placement { Position = position, Yaw = yaw };
            var ship = ShipMotion.FindShipUnder(position);
            if (ship != null && ship.NetworkObject != null && ship.NetworkObject.IsSpawned)
            {
                place.OnShip = true;
                place.Ship = ship.NetworkObject;
                place.Position = ship.transform.InverseTransformPoint(position);
                place.Yaw = Mathf.DeltaAngle(ship.transform.eulerAngles.y, yaw);
            }
            item.placement.Value = place;
            item.NetworkObject.Spawn(true);
            return item;
        }

        /// <summary>
        /// 서버: 선원 앞 바닥에 놓을 자리. 바닥이 없으면(물 위) 수면에 띄운다.
        /// </summary>
        public static Vector3 FindDropPoint(Vector3 origin, Vector3 forward)
        {
            Vector3 start = origin + Vector3.up * 1.2f + forward * 0.8f;
            int mask = ~((1 << 6) | (1 << LayerMaskIndex.Animal) | (1 << 2));
            float sea = OceanSystem.Instance != null ? OceanSystem.Instance.SeaLevel : 0f;
            if (Physics.Raycast(start, Vector3.down, out var hit, 4f, mask, QueryTriggerInteraction.Ignore) && hit.point.y > sea - 0.3f)
            {
                return hit.point;
            }
            return new Vector3(start.x, sea, start.z);
        }

        void LateUpdate()
        {
            ApplyPlacement();
        }

        void ApplyPlacement()
        {
            if (!IsSpawned) return;
            var place = placement.Value;
            Vector3 position = place.Position;
            float yaw = place.Yaw;
            if (place.OnShip && place.Ship.TryGet(out NetworkObject shipObject))
            {
                position = shipObject.transform.TransformPoint(place.Position);
                yaw += shipObject.transform.eulerAngles.y;
            }
            else if (!place.OnShip)
            {
                // 물 위에 놓였으면 파도를 따라 뜬다.
                var ocean = OceanSystem.Instance;
                if (ocean != null && position.y <= ocean.SeaLevel + 0.01f)
                {
                    position.y = ocean.GetWaterHeight(position);
                }
            }
            transform.SetPositionAndRotation(position, Quaternion.Euler(0f, yaw, 0f));
        }

        void RefreshVisual()
        {
            if (shownId == itemId.Value) return;
            shownId = itemId.Value;
            if (visual != null) Destroy(visual);
            var def = Definition;
            interactable.Prompt = def != null ? $"{def.displayName} 줍기" : "줍기";
            if (def == null || def.model == null) return;
            visual = Instantiate(def.model, transform);
            visual.name = "Visual";
            visual.transform.localPosition = Vector3.up * def.restHeight;
            visual.transform.localRotation = Quaternion.Euler(def.restEuler);
            foreach (var c in visual.GetComponentsInChildren<Collider>()) Destroy(c);
        }

        void OnInteracted(GameObject who)
        {
            if (taken) return;
            var hands = who != null ? who.GetComponent<PlayerHands>() : null;
            if (hands != null) hands.RequestPickup(this);
        }

        /// <summary>서버: 주워 갔다. 지운다.</summary>
        public bool ServerTake()
        {
            if (taken || !IsSpawned) return false;
            taken = true;
            NetworkObject.Despawn(true);
            return true;
        }
    }

    /// <summary>레이어 번호.</summary>
    public static class LayerMaskIndex
    {
        static int animal = -2;

        /// <summary>"Animal" 레이어. 없으면 Default.</summary>
        public static int Animal
        {
            get
            {
                if (animal == -2) animal = Mathf.Max(0, LayerMask.NameToLayer("Animal"));
                return animal;
            }
        }
    }
}
