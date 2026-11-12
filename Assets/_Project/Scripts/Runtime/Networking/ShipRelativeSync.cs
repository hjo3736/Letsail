using Letsail.Ship;
using Unity.Netcode;
using UnityEngine;

namespace Letsail.Networking
{
    /// <summary>
    /// 배 위에 있는 선원의 위치를 배 기준으로 보낸다.
    /// NetworkTransform 은 월드 위치를 보내므로, 배가 움직이면 다른 선원 화면에서 배와 캐릭터가 따로 늦게 따라와
    /// 캐릭터가 갑판 위에서 뒤처지고 출렁여 보인다. 배 위에 있는 동안은 받는 쪽에서 자기 화면의 배에 붙여 그린다.
    /// 플레이어 프리팹에 NetworkPlayer 와 함께 둔다.
    /// </summary>
    // NetworkTransform 이 위치를 정한 뒤에 덮어써야 한다.
    [DefaultExecutionOrder(32000)]
    public sealed class ShipRelativeSync : NetworkBehaviour
    {
        struct ShipPose : INetworkSerializable, System.IEquatable<ShipPose>
        {
            public NetworkObjectReference Ship;
            public bool OnShip;
            public Vector3 Local;
            public float Yaw;

            public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
            {
                serializer.SerializeValue(ref OnShip);
                if (OnShip)
                {
                    serializer.SerializeValue(ref Ship);
                    serializer.SerializeValue(ref Local);
                    serializer.SerializeValue(ref Yaw);
                }
            }

            public bool Equals(ShipPose other) =>
                OnShip == other.OnShip && (!OnShip || (Ship.Equals(other.Ship) && Local == other.Local && Mathf.Approximately(Yaw, other.Yaw)));
        }

        [SerializeField, Tooltip("받는 쪽에서 배 기준 위치를 부드럽게 따라가는 속도")] float smoothing = 18f;

        readonly NetworkVariable<ShipPose> pose = new NetworkVariable<ShipPose>(
            default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

        Vector3 smoothedLocal;
        float smoothedYaw;
        bool hasSmoothed;

        void Update()
        {
            if (!IsSpawned || !IsOwner)
            {
                return;
            }
            var ship = ShipMotion.FindShipUnder(transform.position);
            var value = new ShipPose { OnShip = ship != null };
            if (ship != null)
            {
                Transform t = ship.transform;
                value.Ship = ship.NetworkObject;
                value.Local = t.InverseTransformPoint(transform.position);
                value.Yaw = Mathf.DeltaAngle(t.eulerAngles.y, transform.eulerAngles.y);
            }
            if (!value.Equals(pose.Value))
            {
                pose.Value = value;
            }
        }

        void LateUpdate()
        {
            if (!IsSpawned || IsOwner)
            {
                return;
            }
            var value = pose.Value;
            if (!value.OnShip || !value.Ship.TryGet(out NetworkObject shipObject))
            {
                hasSmoothed = false;
                return;
            }
            float t = hasSmoothed ? 1f - Mathf.Exp(-smoothing * Time.deltaTime) : 1f;
            smoothedLocal = Vector3.Lerp(smoothedLocal, value.Local, t);
            smoothedYaw = Mathf.LerpAngle(smoothedYaw, value.Yaw, t);
            hasSmoothed = true;
            Transform ship = shipObject.transform;
            transform.SetPositionAndRotation(ship.TransformPoint(smoothedLocal), Quaternion.Euler(0f, ship.eulerAngles.y + smoothedYaw, 0f));
        }
    }
}
