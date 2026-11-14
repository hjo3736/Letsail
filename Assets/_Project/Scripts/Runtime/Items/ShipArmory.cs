using Letsail.Ship;
using Unity.Netcode;
using UnityEngine;

namespace Letsail.Items
{
    /// <summary>
    /// 출항할 때 배 갑판에 무기를 놓아 둔다 (서버만). 선원이 E 로 주워 든다.
    /// 자리는 배 기준 좌표 (선장실 벽 앞).
    /// 항해 씬에 하나 둔다 (NetworkObject 필요).
    /// </summary>
    public sealed class ShipArmory : NetworkBehaviour
    {
        [System.Serializable]
        public struct Slot
        {
            [Tooltip("아이템 에셋 이름 (예: Item_Cutlass)")] public string item;
            [Tooltip("배 기준 자리 (갑판 위)")] public Vector3 localPosition;
            [Tooltip("배 기준 방향 (도)")] public float yaw;
        }

        [SerializeField] ItemCatalog catalog;
        [SerializeField] WorldItem worldItemPrefab;
        [SerializeField] Slot[] slots =
        {
            new Slot { item = "Item_Cutlass", localPosition = new Vector3(-2.6f, 2.1f, -3.3f), yaw = 90f },
            new Slot { item = "Item_Dagger", localPosition = new Vector3(-1.9f, 2.1f, -3.3f), yaw = 90f },
            new Slot { item = "Item_Flintlock", localPosition = new Vector3(-1.2f, 2.1f, -3.3f), yaw = 90f },
            new Slot { item = "Item_Musket", localPosition = new Vector3(1.1f, 2.1f, -3.3f), yaw = 90f },
            new Slot { item = "Item_WoodenSword", localPosition = new Vector3(1.8f, 2.1f, -3.3f), yaw = 90f },
            new Slot { item = "Item_Dagger", localPosition = new Vector3(2.5f, 2.1f, -3.3f), yaw = 90f },
        };

        bool placed;

        void Update()
        {
            if (placed || !IsServer || !IsSpawned || catalog == null || worldItemPrefab == null) return;
            var ship = FindAnyObjectByType<ShipMotion>();
            if (ship == null || ship.NetworkObject == null || !ship.NetworkObject.IsSpawned) return;
            placed = true;
            foreach (var slot in slots)
            {
                int id = catalog.IndexOf(slot.item);
                if (id < 0) continue;
                Vector3 position = ship.transform.TransformPoint(slot.localPosition);
                // 갑판 높이에 맞춘다.
                if (Physics.Raycast(position + Vector3.up * 0.8f, Vector3.down, out var hit, 2f, ~((1 << 6) | (1 << 2)), QueryTriggerInteraction.Ignore))
                {
                    position = hit.point;
                }
                WorldItem.ServerSpawn(worldItemPrefab, id, position, ship.transform.eulerAngles.y + slot.yaw);
            }
        }
    }
}
