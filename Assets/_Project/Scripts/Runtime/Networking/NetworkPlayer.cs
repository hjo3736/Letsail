using System.Collections.Generic;
using Letsail.Player;
using Letsail.World;
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEngine;

namespace Letsail.Networking
{
    /// <summary>
    /// 네트워크로 생성된 플레이어 캐릭터.
    /// - 내 캐릭터: 입력, 이동, 상호작용, 애니메이터 갱신, 카메라를 켠다. 위치·애니메이션·복장은 내가 정해서 보낸다(Owner 권한).
    /// - 다른 사람 캐릭터: 위 기능을 끄고 받은 위치·애니메이션·복장만 보여 준다.
    /// 서버는 비어 있는 스폰 번호를 정해 주고, 번호에 따라 스폰 위치가 정해진다.
    /// 네트워크 없이(씬에 직접 놓고) 실행하면 아무것도 하지 않아서 혼자 테스트할 수 있다.
    /// </summary>
    [RequireComponent(typeof(NetworkObject))]
    public sealed class NetworkPlayer : NetworkBehaviour
    {
        // 저장된 복장이 없을 때 스폰 번호마다 다르게 입히는 기본 복장 (누가 누군지 구분되게).
        static readonly (string hat, string top)[] DefaultOutfits =
        {
            ("Bandana", "StripedShirt"),
            ("Tricorn", "CaptainCoat"),
            ("Straw", "ExplorerShirt"),
            ("SailorCap", "SailorShirt"),
        };

        static readonly List<NetworkPlayer> spawned = new List<NetworkPlayer>();

        readonly NetworkVariable<int> spawnIndex = new NetworkVariable<int>(-1);
        readonly NetworkVariable<NetworkOutfit> networkOutfit = new NetworkVariable<NetworkOutfit>(
            default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

        PlayerMotor motor;
        PlayerInputHandler input;
        PlayerAnimator playerAnimator;
        PlayerInteractor interactor;
        CharacterOutfit outfit;
        NetworkTransform networkTransform;
        bool applyingRemoteOutfit;

        /// <summary>이 PC에서 조작하는 플레이어. 접속 전에는 null.</summary>
        public static NetworkPlayer Local { get; private set; }

        public static IReadOnlyList<NetworkPlayer> All => spawned;

        public int SpawnIndex => spawnIndex.Value;

        void Awake()
        {
            motor = GetComponent<PlayerMotor>();
            input = GetComponent<PlayerInputHandler>();
            playerAnimator = GetComponent<PlayerAnimator>();
            interactor = GetComponent<PlayerInteractor>();
            outfit = GetComponent<CharacterOutfit>();
            networkTransform = GetComponent<NetworkTransform>();
        }

        public override void OnNetworkSpawn()
        {
            spawned.Add(this);
            if (IsServer)
            {
                spawnIndex.Value = PickFreeSpawnIndex();
            }

            bool mine = IsOwner;
            SetLocalControl(mine);

            spawnIndex.OnValueChanged += OnSpawnIndexChanged;
            networkOutfit.OnValueChanged += OnNetworkOutfitChanged;
            outfit.Changed += OnLocalOutfitChanged;

            if (!networkOutfit.Value.IsEmpty)
            {
                ApplyRemoteOutfit(networkOutfit.Value);
            }
            if (spawnIndex.Value >= 0)
            {
                OnSpawnIndexChanged(-1, spawnIndex.Value);
            }

            if (mine)
            {
                Local = this;
                AttachCamera();
            }
            UnityEngine.SceneManagement.SceneManager.sceneLoaded += OnSceneLoaded;
        }

        // 출항 등으로 씬이 바뀌면 캐릭터는 그대로 넘어오지만, 카메라와 스폰 위치는 새 씬의 것을 써야 한다.
        void OnSceneLoaded(UnityEngine.SceneManagement.Scene scene, UnityEngine.SceneManagement.LoadSceneMode mode)
        {
            if (!IsSpawned || !IsOwner || mode != UnityEngine.SceneManagement.LoadSceneMode.Single)
            {
                return;
            }
            AttachCamera();
            if (spawnIndex.Value >= 0)
            {
                MoveToSpawnPoint(spawnIndex.Value);
            }
        }

        void AttachCamera()
        {
            var cam = FindAnyObjectByType<PlayerCamera>();
            if (cam != null)
            {
                cam.Target = motor;
            }
        }

        public override void OnNetworkDespawn()
        {
            spawned.Remove(this);
            UnityEngine.SceneManagement.SceneManager.sceneLoaded -= OnSceneLoaded;
            spawnIndex.OnValueChanged -= OnSpawnIndexChanged;
            networkOutfit.OnValueChanged -= OnNetworkOutfitChanged;
            outfit.Changed -= OnLocalOutfitChanged;
            if (Local == this)
            {
                Local = null;
                var cam = FindAnyObjectByType<PlayerCamera>();
                if (cam != null && cam.Target == motor)
                {
                    cam.Target = null;
                }
            }
        }

        /// <summary>
        /// 순간이동. 내 캐릭터일 때만 의미가 있다. 다른 사람 화면에서 미끄러져 오지 않고 바로 그 자리에 나타난다.
        /// </summary>
        public void TeleportTo(Vector3 position, Quaternion rotation)
        {
            motor.Teleport(position);
            transform.rotation = rotation;
            if (IsSpawned && networkTransform != null && networkTransform.CanCommitToTransform)
            {
                networkTransform.SetState(position, rotation, null, false);
            }
        }

        void SetLocalControl(bool mine)
        {
            // 다른 사람 캐릭터의 CharacterController 는 켜 둔다: 서로 부딪히는 몸 역할을 한다.
            if (input != null) input.enabled = mine;
            if (motor != null) motor.enabled = mine;
            if (playerAnimator != null) playerAnimator.enabled = mine;
            if (interactor != null) interactor.enabled = mine;
        }

        void OnSpawnIndexChanged(int previous, int index)
        {
            if (index < 0 || !IsOwner || previous >= 0)
            {
                return;
            }

            // 저장된 복장이 있으면 그걸 입고, 없으면 번호별 기본 복장. 그 결과를 다른 사람에게 보낸다.
            if (!OutfitStorage.Load(outfit))
            {
                var (hat, top) = DefaultOutfits[index % DefaultOutfits.Length];
                outfit.Set(CharacterOutfit.Slot.Hat, hat);
                outfit.Set(CharacterOutfit.Slot.Top, top);
            }
            networkOutfit.Value = NetworkOutfit.From(outfit);

            MoveToSpawnPoint(index);
        }

        void OnLocalOutfitChanged()
        {
            // 옷장 등에서 내가 바꾼 복장만 보낸다. 받은 복장을 입히는 중에는 다시 보내지 않는다.
            if (IsSpawned && IsOwner && !applyingRemoteOutfit)
            {
                networkOutfit.Value = NetworkOutfit.From(outfit);
            }
        }

        void OnNetworkOutfitChanged(NetworkOutfit previous, NetworkOutfit current)
        {
            if (!IsOwner && !current.IsEmpty)
            {
                ApplyRemoteOutfit(current);
            }
        }

        void ApplyRemoteOutfit(NetworkOutfit value)
        {
            applyingRemoteOutfit = true;
            value.ApplyTo(outfit);
            applyingRemoteOutfit = false;
        }

        void MoveToSpawnPoint(int index)
        {
            var provider = PlayerSpawn.Find();
            if (provider == null)
            {
                return;
            }
            if (provider is LobbyLayout layout)
            {
                layout.Collect();
            }
            Transform point = provider.GetSpawnPoint(index);
            TeleportTo(point.position + Vector3.up * 0.05f, point.rotation);
        }

        static int PickFreeSpawnIndex()
        {
            for (int candidate = 0; ; candidate++)
            {
                bool used = false;
                foreach (var p in spawned)
                {
                    if (p.spawnIndex.Value == candidate)
                    {
                        used = true;
                        break;
                    }
                }
                if (!used)
                {
                    return candidate;
                }
            }
        }
    }
}
