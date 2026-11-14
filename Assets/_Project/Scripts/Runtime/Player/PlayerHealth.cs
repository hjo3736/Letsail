using Letsail.Core;
using Unity.Netcode;
using UnityEngine;

namespace Letsail.Player
{
    /// <summary>
    /// 선원 체력. 서버가 정하고 모두에게 보낸다.
    /// - 맞으면 줄고 (맞는 동작), 한동안 맞지 않으면 천천히 찬다.
    /// - 0 이 되면 쓰러진다: 움직이지도, 상호작용하지도, 공격하지도 못한다.
    ///   동료가 다가와 E 를 길게 누르면 일으켜 주고 (Revive), 혼자면 한참 뒤 스스로 일어난다.
    /// 플레이어 프리팹에 둔다. 일으키기 상호작용(자식 Interactable)은 다른 선원에게만 보인다.
    /// </summary>
    public sealed class PlayerHealth : NetworkBehaviour
    {
        [SerializeField] float maxHealth = 100f;
        [SerializeField, Tooltip("마지막으로 맞은 뒤 이 시간이 지나면 체력이 찬다 (초)")] float regenDelay = 8f;
        [SerializeField, Tooltip("초당 차는 체력")] float regenPerSecond = 2f;
        [SerializeField, Tooltip("쓰러진 뒤 혼자 일어나기까지 (초)")] float selfReviveSeconds = 25f;
        [SerializeField, Range(0f, 1f), Tooltip("혼자 일어날 때 체력 비율")] float selfReviveHealth = 0.3f;
        [SerializeField, Range(0f, 1f), Tooltip("동료가 일으켜 줄 때 체력 비율")] float reviveHealth = 0.5f;
        [SerializeField, Tooltip("일어난 뒤 맞지 않는 시간 (초). 동물도 노리지 않는다")] float reviveProtection = 5f;
        [SerializeField, Tooltip("일으키기 상호작용 (자식)")] Interactable reviveInteractable;

        float protectedUntil;

        /// <summary>서버: 막 일어나서 잠깐 맞지 않는 중.</summary>
        public bool IsProtected => Time.time < protectedUntil;

        readonly NetworkVariable<float> health = new NetworkVariable<float>(100f);
        readonly NetworkVariable<bool> downed = new NetworkVariable<bool>();
        readonly NetworkVariable<double> downedAt = new NetworkVariable<double>();

        PlayerMotor motor;
        PlayerAnimator playerAnimator;
        PlayerInteractor interactor;
        float lastDamageTime = -100f;

        /// <summary>이 PC 선원의 체력.</summary>
        public static PlayerHealth Local { get; private set; }

        public float Health => health.Value;
        public float MaxHealth => maxHealth;
        public float Health01 => maxHealth > 0f ? Mathf.Clamp01(health.Value / maxHealth) : 0f;
        public bool IsDowned => downed.Value;

        /// <summary>쓰러진 뒤 혼자 일어나기까지 남은 시간 (초).</summary>
        public float SelfReviveRemaining => downed.Value && NetworkManager != null
            ? Mathf.Max(0f, (float)(downedAt.Value + selfReviveSeconds - NetworkManager.ServerTime.Time))
            : 0f;

        /// <summary>맞았을 때 (모든 PC). 피해량.</summary>
        public event System.Action<float> Damaged;

        void Awake()
        {
            motor = GetComponent<PlayerMotor>();
            playerAnimator = GetComponent<PlayerAnimator>();
            interactor = GetComponent<PlayerInteractor>();
            if (reviveInteractable != null) reviveInteractable.Interacted += OnReviveInteracted;
        }

        public override void OnNetworkSpawn()
        {
            if (IsServer) health.Value = maxHealth;
            health.OnValueChanged += OnHealthChanged;
            downed.OnValueChanged += OnDownedChanged;
            if (IsOwner) Local = this;
            ApplyDowned(downed.Value, false);
        }

        public override void OnNetworkDespawn()
        {
            health.OnValueChanged -= OnHealthChanged;
            downed.OnValueChanged -= OnDownedChanged;
            if (Local == this) Local = null;
        }

        void Update()
        {
            if (reviveInteractable != null)
            {
                // 내 몸은 일으키기 대상이 아니다.
                reviveInteractable.Available = IsSpawned && downed.Value && !IsOwner;
            }
            if (IsSpawned && IsOwner && downed.Value && motor != null)
            {
                motor.Locked = true;
            }
            if (!IsServer || !IsSpawned) return;
            if (downed.Value)
            {
                if (NetworkManager.ServerTime.Time >= downedAt.Value + selfReviveSeconds) ServerRevive(selfReviveHealth);
                return;
            }
            if (health.Value < maxHealth && Time.time - lastDamageTime > regenDelay)
            {
                health.Value = Mathf.Min(maxHealth, health.Value + regenPerSecond * Time.deltaTime);
            }
        }

        // ───────────────────────── 서버 ─────────────────────────

        /// <summary>서버: 피해를 준다. 쓰러져 있으면 더 맞지 않는다.</summary>
        public void ServerTakeDamage(float amount, Vector3 from)
        {
            if (!IsServer || downed.Value || amount <= 0f || IsProtected) return;
            lastDamageTime = Time.time;
            float next = health.Value - amount;
            if (next <= 0f)
            {
                health.Value = 0f;
                downedAt.Value = NetworkManager.ServerTime.Time;
                downed.Value = true;
            }
            else
            {
                health.Value = next;
            }
        }

        void ServerRevive(float ratio)
        {
            downed.Value = false;
            health.Value = Mathf.Max(1f, maxHealth * ratio);
            lastDamageTime = Time.time;
            protectedUntil = Time.time + reviveProtection;
        }

        [Rpc(SendTo.Server)]
        void ReviveRpc(RpcParams rpcParams = default)
        {
            if (!downed.Value) return;
            // 일으키는 선원이 옆에 있어야 한다.
            ulong sender = rpcParams.Receive.SenderClientId;
            if (NetworkManager.ConnectedClients.TryGetValue(sender, out var client) && client.PlayerObject != null
                && Vector3.Distance(client.PlayerObject.transform.position, transform.position) > 3.5f)
            {
                return;
            }
            ServerRevive(reviveHealth);
        }

        // ───────────────────────── 모든 PC ─────────────────────────

        void OnReviveInteracted(GameObject who)
        {
            if (downed.Value && who != gameObject) ReviveRpc();
        }

        void OnHealthChanged(float previous, float current)
        {
            if (current >= previous) return;
            Damaged?.Invoke(previous - current);
            if (IsOwner && !downed.Value && playerAnimator != null && (interactor == null || interactor.Seat == null))
            {
                playerAnimator.PlayOnce("Hit_React");
            }
        }

        void OnDownedChanged(bool previous, bool current) => ApplyDowned(current, true);

        void ApplyDowned(bool isDowned, bool animate)
        {
            if (!IsOwner) return;
            if (interactor != null)
            {
                if (isDowned && interactor.Seat != null) interactor.Seat.Leave();
                interactor.Busy = isDowned;
            }
            if (playerAnimator != null)
            {
                playerAnimator.SetBaseAction(isDowned ? "Downed_Idle" : null);
                if (animate) playerAnimator.PlayOnce(isDowned ? "Downed_Fall" : "Downed_GetUp", holdStill: !isDowned);
            }
            if (motor != null)
            {
                motor.Locked = isDowned;
                if (isDowned)
                {
                    motor.SetMoveDirection(Vector3.zero);
                }
            }
        }
    }
}
