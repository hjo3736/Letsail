using Letsail.Player;
using Letsail.Ship;
using Unity.Netcode;
using UnityEngine;

namespace Letsail.Networking
{
    /// <summary>
    /// 배 위에 있는 선원의 위치를 배 기준으로 보낸다.
    /// NetworkTransform 은 월드 위치를 보내므로, 배가 움직이면 다른 선원 화면에서 배와 캐릭터가 따로 늦게 따라와
    /// 캐릭터가 갑판 위에서 뒤처지고 출렁여 보인다. 배 위에 있는 동안은 받는 쪽에서 자기 화면의 배에 붙여 그린다.
    /// 물에서 헤엄치는 동안은 배 영역 안이라도 월드 위치(NetworkTransform)를 쓴다. 배는 계속 가는데 사람은 물에 있으므로.
    /// 두 방식이 바뀌는 순간에는 받는 쪽에서 튀지 않게 이어 붙인다.
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
        [SerializeField, Tooltip("배 위 ↔ 물 방식이 바뀔 때 어긋난 만큼을 없애는 속도 (클수록 빨리)")] float handoverSpeed = 14f;

        readonly NetworkVariable<ShipPose> pose = new NetworkVariable<ShipPose>(
            default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

        PlayerMotor motor;
        Vector3 smoothedLocal;
        float smoothedYaw;
        bool hasSmoothed;

        // 물(월드) 방식으로 바뀐 뒤 남은 어긋남. NetworkTransform 위치에 더해 줄여 간다.
        Vector3 handoverOffset;
        Vector3 lastBase;
        Vector3 lastWritten;
        bool hasWritten;

        /// <summary>배 기준으로 맞추는 중인지 (주인이 정한 값, 모든 PC 가 같다).</summary>
        public bool Aboard => pose.Value.OnShip;

        void Awake()
        {
            motor = GetComponent<PlayerMotor>();
        }

        void Update()
        {
            if (!IsSpawned || !IsOwner)
            {
                return;
            }
            bool swimming = motor != null && motor.IsSwimming;
            var ship = swimming ? null : ShipMotion.FindShipUnder(transform.position);
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
                FollowWorld();
                return;
            }

            Transform ship = shipObject.transform;
            if (!hasSmoothed)
            {
                // 물에서 배로 올라온 순간: 지금 보이는 자리에서 시작해 배 기준 자리로 미끄러져 간다.
                smoothedLocal = ship.InverseTransformPoint(transform.position);
                smoothedYaw = Mathf.DeltaAngle(ship.eulerAngles.y, transform.eulerAngles.y);
                hasSmoothed = true;
            }
            float t = 1f - Mathf.Exp(-smoothing * Time.deltaTime);
            smoothedLocal = Vector3.Lerp(smoothedLocal, value.Local, t);
            smoothedYaw = Mathf.LerpAngle(smoothedYaw, value.Yaw, t);
            Vector3 position = ship.TransformPoint(smoothedLocal);
            transform.SetPositionAndRotation(position, Quaternion.Euler(0f, ship.eulerAngles.y + smoothedYaw, 0f));
            lastWritten = position;
            lastBase = position;
            hasWritten = true;
        }

        // 월드 방식: NetworkTransform 이 정한 자리를 쓰되, 배 방식에서 막 바뀌었으면 어긋남을 점점 줄인다.
        void FollowWorld()
        {
            // NetworkTransform 이 이번 프레임에 위치를 안 썼으면 (내가 지난번에 쓴 그대로면) 지난 기준 위치를 쓴다.
            Vector3 basePosition = hasWritten && transform.position == lastWritten ? lastBase : transform.position;
            if (hasSmoothed)
            {
                // 배에서 물로 바뀐 순간: 보이던 자리와 받은 월드 자리의 차이만큼 이어 붙인다.
                handoverOffset = hasWritten ? lastWritten - basePosition : Vector3.zero;
                hasSmoothed = false;
            }
            if (handoverOffset.sqrMagnitude < 0.0001f)
            {
                handoverOffset = Vector3.zero;
                lastBase = basePosition;
                lastWritten = basePosition;
                hasWritten = true;
                return;
            }
            handoverOffset *= Mathf.Exp(-handoverSpeed * Time.deltaTime);
            Vector3 position = basePosition + handoverOffset;
            transform.position = position;
            lastBase = basePosition;
            lastWritten = position;
            hasWritten = true;
        }
    }
}
