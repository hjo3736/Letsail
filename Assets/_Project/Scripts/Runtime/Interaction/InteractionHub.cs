using System;
using Letsail.Core;
using Unity.Netcode;
using UnityEngine;

namespace Letsail.Interaction
{
    /// <summary>
    /// 한 물체(배 등) 안의 상호작용 상태를 모든 선원에게 맞춰 준다.
    /// 자식의 Interactable 마다 번호를 매기고, 번호별로 세 가지를 동기화한다.
    /// - 누가 쓰는 중인지 (자리: 키, 대포, 침대 등)
    /// - 켜짐/꺼짐 (서랍, 불, 닻 등)
    /// - 조작 값 (키 각도, 돛 펼침, 대포 각도 등)
    /// 바꾸는 요청은 호스트(서버)가 받아서 확인한 뒤 반영한다. 네트워크 없이 실행하면 혼자 쓰는 상태로 동작한다.
    /// </summary>
    [RequireComponent(typeof(NetworkObject))]
    public sealed class InteractionHub : NetworkBehaviour
    {
        /// <summary>아무도 쓰지 않음.</summary>
        public const ulong Nobody = ulong.MaxValue;
        // 네트워크 없이 혼자 테스트할 때의 내 번호.
        const ulong OfflineClient = 0;

        readonly NetworkList<ulong> occupants = new NetworkList<ulong>();
        readonly NetworkList<byte> states = new NetworkList<byte>();
        readonly NetworkList<float> values = new NetworkList<float>();

        Interactable[] points;
        ulong[] offlineOccupants;
        byte[] offlineStates;
        float[] offlineValues;

        /// <summary>번호의 상태가 바뀌었을 때. -1 이면 전부.</summary>
        public event Action<int> Changed;

        bool Online => IsSpawned;

        /// <summary>이 PC 의 선원 번호.</summary>
        public ulong LocalClientId => Online ? NetworkManager.LocalClientId : OfflineClient;

        void Awake()
        {
            EnsurePoints();
        }

        // 자식 컴포넌트의 Awake 가 이 Awake 보다 먼저 불릴 수 있어서, 처음 쓰일 때 준비한다.
        void EnsurePoints()
        {
            if (points != null)
            {
                return;
            }
            points = GetComponentsInChildren<Interactable>(true);
            offlineOccupants = new ulong[points.Length];
            offlineStates = new byte[points.Length];
            offlineValues = new float[points.Length];
            for (int i = 0; i < points.Length; i++)
            {
                offlineOccupants[i] = Nobody;
                var toggle = points[i].GetComponent<ToggleInteraction>();
                offlineStates[i] = (byte)(toggle != null && toggle.InitialOn ? 1 : 0);
                var control = points[i].GetComponent<SeatControl>();
                offlineValues[i] = control != null ? control.InitialValue : 0f;
            }
        }

        public int IndexOf(Interactable interactable)
        {
            EnsurePoints();
            return Array.IndexOf(points, interactable);
        }

        public ulong GetOccupant(int index)
        {
            if (!Online)
            {
                return offlineOccupants[index];
            }
            return index < occupants.Count ? occupants[index] : Nobody;
        }

        public bool GetState(int index)
        {
            if (!Online)
            {
                return offlineStates[index] != 0;
            }
            return index < states.Count ? states[index] != 0 : offlineStates[index] != 0;
        }

        public float GetValue(int index)
        {
            if (!Online)
            {
                return offlineValues[index];
            }
            return index < values.Count ? values[index] : offlineValues[index];
        }

        // ---------------- 요청 ----------------

        public void RequestToggle(int index)
        {
            if (!Online)
            {
                offlineStates[index] = (byte)(offlineStates[index] == 0 ? 1 : 0);
                Changed?.Invoke(index);
                return;
            }
            ToggleRpc(index);
        }

        /// <summary>켜짐/꺼짐을 정해진 값으로 바꾼다 (사다리를 타면 해치를 여는 등, 누르는 사람 없이 바꿀 때).</summary>
        public void RequestSetState(int index, bool on)
        {
            if (!Online)
            {
                offlineStates[index] = (byte)(on ? 1 : 0);
                Changed?.Invoke(index);
                return;
            }
            SetStateRpc(index, on);
        }

        public void RequestOccupy(int index)
        {
            if (!Online)
            {
                if (offlineOccupants[index] == Nobody)
                {
                    ReleaseAll(offlineOccupants, OfflineClient);
                    offlineOccupants[index] = OfflineClient;
                    Changed?.Invoke(-1);
                }
                return;
            }
            OccupyRpc(index);
        }

        public void RequestRelease(int index)
        {
            if (!Online)
            {
                if (offlineOccupants[index] == OfflineClient)
                {
                    offlineOccupants[index] = Nobody;
                    Changed?.Invoke(index);
                }
                return;
            }
            ReleaseRpc(index);
        }

        /// <summary>자리를 쓰는 사람이 조작 값을 보낸다. 쓰는 사람이 아니면 무시된다.</summary>
        public void SubmitValue(int index, float value)
        {
            if (!Online)
            {
                offlineValues[index] = value;
                Changed?.Invoke(index);
                return;
            }
            ValueRpc(index, value);
        }

        // ---------------- 서버 ----------------

        public override void OnNetworkSpawn()
        {
            EnsurePoints();
            if (IsServer && occupants.Count == 0)
            {
                for (int i = 0; i < points.Length; i++)
                {
                    occupants.Add(Nobody);
                    states.Add(offlineStates[i]);
                    values.Add(offlineValues[i]);
                }
            }
            occupants.OnListChanged += OnOccupantsChanged;
            states.OnListChanged += OnStatesChanged;
            values.OnListChanged += OnValuesChanged;
            if (IsServer)
            {
                NetworkManager.OnClientDisconnectCallback += OnClientDisconnected;
            }
            Changed?.Invoke(-1);
        }

        public override void OnNetworkDespawn()
        {
            occupants.OnListChanged -= OnOccupantsChanged;
            states.OnListChanged -= OnStatesChanged;
            values.OnListChanged -= OnValuesChanged;
            if (NetworkManager != null)
            {
                NetworkManager.OnClientDisconnectCallback -= OnClientDisconnected;
            }
            // 연결이 끝나면 혼자 쓰는 상태로 돌아간다 (앉아 있던 자리에서 일어난다).
            for (int i = 0; i < offlineOccupants.Length; i++)
            {
                offlineOccupants[i] = Nobody;
            }
            Changed?.Invoke(-1);
        }

        [Rpc(SendTo.Server)]
        void ToggleRpc(int index, RpcParams rpcParams = default)
        {
            if (IsValid(index))
            {
                states[index] = (byte)(states[index] == 0 ? 1 : 0);
            }
        }

        [Rpc(SendTo.Server)]
        void SetStateRpc(int index, bool on)
        {
            if (IsValid(index))
            {
                states[index] = (byte)(on ? 1 : 0);
            }
        }

        [Rpc(SendTo.Server)]
        void OccupyRpc(int index, RpcParams rpcParams = default)
        {
            ulong sender = rpcParams.Receive.SenderClientId;
            if (!IsValid(index) || occupants[index] != Nobody)
            {
                return;
            }
            // 한 사람은 한 자리만 쓴다.
            for (int i = 0; i < occupants.Count; i++)
            {
                if (occupants[i] == sender)
                {
                    occupants[i] = Nobody;
                }
            }
            occupants[index] = sender;
        }

        [Rpc(SendTo.Server)]
        void ReleaseRpc(int index, RpcParams rpcParams = default)
        {
            if (IsValid(index) && occupants[index] == rpcParams.Receive.SenderClientId)
            {
                occupants[index] = Nobody;
            }
        }

        [Rpc(SendTo.Server, Delivery = RpcDelivery.Unreliable)]
        void ValueRpc(int index, float value, RpcParams rpcParams = default)
        {
            if (IsValid(index) && occupants[index] == rpcParams.Receive.SenderClientId)
            {
                values[index] = value;
            }
        }

        void OnClientDisconnected(ulong clientId)
        {
            for (int i = 0; i < occupants.Count; i++)
            {
                if (occupants[i] == clientId)
                {
                    occupants[i] = Nobody;
                }
            }
        }

        bool IsValid(int index) => index >= 0 && index < occupants.Count;

        static void ReleaseAll(ulong[] list, ulong client)
        {
            for (int i = 0; i < list.Length; i++)
            {
                if (list[i] == client)
                {
                    list[i] = Nobody;
                }
            }
        }

        void OnOccupantsChanged(NetworkListEvent<ulong> e) => Changed?.Invoke(e.Index);
        void OnStatesChanged(NetworkListEvent<byte> e) => Changed?.Invoke(e.Index);
        void OnValuesChanged(NetworkListEvent<float> e) => Changed?.Invoke(e.Index);
    }
}
