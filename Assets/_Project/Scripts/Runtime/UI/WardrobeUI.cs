using System.Collections.Generic;
using Letsail.Networking;
using Letsail.Player;
using Letsail.World;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Letsail.UI
{
    /// <summary>
    /// 옷장 화면. 부위마다 ◀ ▶ 로 부품을 고르면 바로 입혀진다(멀티플레이에서는 다른 사람에게도 바로 보인다).
    /// 완료하면 이 PC에 저장하고, 취소하거나 Esc 를 누르면 열기 전 복장으로 되돌린다.
    /// </summary>
    public sealed class WardrobeUI : MonoBehaviour
    {
        [SerializeField] GameObject panel;
        [SerializeField] RectTransform rowContainer;
        [SerializeField, Tooltip("부위 한 줄의 원본. 자식에 Label, Prev, Value, Next 가 있어야 한다")]
        GameObject rowTemplate;
        [SerializeField] Button doneButton;
        [SerializeField] Button cancelButton;

        struct Row
        {
            public CharacterOutfit.Slot Slot;
            public Text Value;
        }

        readonly List<Row> rows = new List<Row>();
        readonly Dictionary<CharacterOutfit.Slot, string> snapshot = new Dictionary<CharacterOutfit.Slot, string>();
        // 옷장을 쓰는 동안 내 화면에서만 숨긴 다른 선원의 렌더러.
        readonly List<Renderer> hiddenOthers = new List<Renderer>();

        static int escapeFrame = -1;

        GameObject player;
        CharacterOutfit outfit;
        PlayerInputHandler input;
        PlayerInteractor interactor;
        PlayerMotor motor;
        PlayerCamera playerCamera;

        public static WardrobeUI Instance { get; private set; }
        public bool IsOpen => panel.activeSelf;

        /// <summary>이번 프레임의 Esc 를 옷장이 썼는지 (열려 있거나 방금 Esc 로 닫혔다). 설정이 같이 열리지 않게 한다.</summary>
        public static bool UsedEscapeThisFrame => escapeFrame == Time.frameCount || (Instance != null && Instance.IsOpen);

        void Awake()
        {
            Instance = this;
            BuildRows();
            doneButton.onClick.AddListener(Done);
            cancelButton.onClick.AddListener(Cancel);
            panel.SetActive(false);
        }

        void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }
        }

        void Update()
        {
            if (!IsOpen)
            {
                return;
            }
            if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
            {
                escapeFrame = Time.frameCount;
                Cancel();
                return;
            }
            // 같은 거울 앞에 다른 선원이 동시에 서면 겹쳐서 내 모습이 안 보인다.
            // 옷장을 쓰는 동안은 내 화면에서만 다른 선원을 숨긴다 (중간에 들어온 사람 포함).
            HideOtherPlayers();
        }

        public void Open(WardrobeStation station, GameObject who)
        {
            if (IsOpen || who == null)
            {
                return;
            }
            player = who;
            outfit = player.GetComponent<CharacterOutfit>();
            input = player.GetComponent<PlayerInputHandler>();
            interactor = player.GetComponent<PlayerInteractor>();
            motor = player.GetComponent<PlayerMotor>();
            playerCamera = FindAnyObjectByType<PlayerCamera>();
            if (outfit == null)
            {
                return;
            }

            snapshot.Clear();
            foreach (var slot in CharacterOutfit.AllSlots)
            {
                snapshot[slot] = outfit.Get(slot);
            }

            if (input != null) input.enabled = false;
            if (interactor != null) interactor.enabled = false;

            // 거울 앞에 서서 카메라를 바라보게 한다.
            // 이동 처리를 멈춰서, 같은 자리에 선 다른 선원과 몸이 밀려 자리에서 벗어나지 않게 한다.
            if (station.PlayerSpot != null)
            {
                Vector3 toCamera = station.CameraPose != null ? station.CameraPose.position - station.PlayerSpot.position : station.PlayerSpot.forward;
                toCamera.y = 0f;
                Quaternion facing = toCamera.sqrMagnitude > 0.001f ? Quaternion.LookRotation(toCamera) : station.PlayerSpot.rotation;
                Vector3 position = station.PlayerSpot.position + Vector3.up * 0.05f;
                var networkPlayer = player.GetComponent<NetworkPlayer>();
                if (networkPlayer != null && networkPlayer.IsSpawned)
                {
                    networkPlayer.TeleportTo(position, facing);
                }
                else
                {
                    motor.Teleport(position);
                    player.transform.rotation = facing;
                }
            }
            if (motor != null) motor.enabled = false;
            if (playerCamera != null && station.CameraPose != null)
            {
                playerCamera.SetOverridePose(station.CameraPose);
            }

            panel.SetActive(true);
            RefreshAll();
            // 거울을 보며 옷매무새를 고치는 자세.
            var animator = player.GetComponent<PlayerAnimator>();
            if (animator != null)
            {
                animator.SetBaseAction("Wardrobe_Pose");
            }
        }

        void Done()
        {
            if (outfit != null)
            {
                OutfitStorage.Save(outfit);
            }
            Close();
        }

        void Cancel()
        {
            if (outfit != null)
            {
                foreach (var pair in snapshot)
                {
                    outfit.Set(pair.Key, pair.Value);
                }
            }
            Close();
        }

        void Close()
        {
            panel.SetActive(false);
            ShowOtherPlayers();
            var animator = player != null ? player.GetComponent<PlayerAnimator>() : null;
            if (animator != null)
            {
                animator.SetBaseAction(null);
            }
            if (motor != null) motor.enabled = true;
            if (input != null) input.enabled = true;
            if (interactor != null) interactor.enabled = true;
            if (playerCamera != null) playerCamera.SetOverridePose(null);
            outfit = null;
            player = null;
        }

        void HideOtherPlayers()
        {
            foreach (var other in NetworkPlayer.All)
            {
                if (other == null || other.gameObject == player)
                {
                    continue;
                }
                foreach (var r in other.GetComponentsInChildren<Renderer>())
                {
                    if (r.enabled)
                    {
                        r.enabled = false;
                        hiddenOthers.Add(r);
                    }
                }
            }
        }

        void ShowOtherPlayers()
        {
            foreach (var r in hiddenOthers)
            {
                if (r != null)
                {
                    r.enabled = true;
                }
            }
            hiddenOthers.Clear();
        }

        void Step(CharacterOutfit.Slot slot, int direction)
        {
            if (outfit == null)
            {
                return;
            }
            int count = outfit.GetAvailableParts(slot).Length;
            int min = CharacterOutfit.IsRequired(slot) ? 1 : 0;
            int range = count - min + 1;
            if (range <= 0)
            {
                return;
            }
            int index = outfit.GetIndex(slot);
            if (index < min)
            {
                index = min;
            }
            index = min + ((index - min + direction) % range + range) % range;
            outfit.SetIndex(slot, index);
            RefreshAll();
        }

        void RefreshAll()
        {
            if (outfit == null)
            {
                return;
            }
            foreach (var row in rows)
            {
                row.Value.text = CharacterOutfit.GetPartDisplayName(outfit.Get(row.Slot));
            }
        }

        void BuildRows()
        {
            rowTemplate.SetActive(false);
            foreach (var slot in CharacterOutfit.AllSlots)
            {
                GameObject go = Instantiate(rowTemplate, rowContainer);
                go.name = "Row_" + slot;
                go.SetActive(true);
                go.transform.Find("Label").GetComponent<Text>().text = CharacterOutfit.GetSlotDisplayName(slot);
                var value = go.transform.Find("Value").GetComponent<Text>();
                CharacterOutfit.Slot captured = slot;
                go.transform.Find("Prev").GetComponent<Button>().onClick.AddListener(() => Step(captured, -1));
                go.transform.Find("Next").GetComponent<Button>().onClick.AddListener(() => Step(captured, +1));
                rows.Add(new Row { Slot = slot, Value = value });
            }
        }
    }
}
