using Letsail.Core;
using Letsail.Networking;
using Letsail.Player;
using UnityEngine;

namespace Letsail.Interaction
{
    /// <summary>
    /// 자리를 잡고 쓰는 상호작용 (키, 돛, 대포, 침대 등). 한 번에 한 사람만 쓸 수 있다.
    /// 앉으면 이동이 멈추고 캐릭터가 자리에 붙어 있으며(배가 움직여도 따라간다), E 로 일어난다.
    /// 같은 오브젝트에 SeatControl 이 있으면 앉은 동안 그 조작을 한다.
    /// 잡을 때·자리에 있는 동안·놓을 때의 캐릭터 동작을 정할 수 있다 (예: Helm_Grab → Helm_Idle → Helm_Release).
    /// 누가 앉았는지는 InteractionHub 가 모든 선원에게 맞춰 준다.
    /// </summary>
    [RequireComponent(typeof(Interactable))]
    public sealed class SeatInteraction : MonoBehaviour, IInteractionSeat
    {
        [SerializeField, Tooltip("자리를 잡을 때의 안내")] string usePrompt = "자리 잡기";
        [SerializeField, Tooltip("자리에서 일어날 때의 안내")] string leavePrompt = "일어나기";
        [SerializeField, Tooltip("앉아 있는 동안 캐릭터가 있을 자리. 비우면 이 오브젝트 위치와 방향")] Transform pose;
        [SerializeField, Tooltip("앉아 있는 동안 이것을 바라본다 (예: 돛대). 비우면 자리의 방향")] Transform faceTarget;
        [Header("캐릭터 동작 (애니메이터 상태 이름)")]
        [SerializeField, Tooltip("자리를 잡을 때 한 번")] string enterAction;
        [SerializeField, Tooltip("자리에 있는 동안 반복")] string loopAction;
        [SerializeField, Tooltip("자리에서 일어날 때 한 번")] string exitAction;
        [Header("눕는 자리")]
        [SerializeField, Tooltip("눕는 자리 (침대). 머리가 자리(pose)의 앞쪽(베개 쪽)을 향하게 놓는다. 눕는 모습은 동작(Bunk_Sleep 등)이 만든다")] bool lieDown;
        [SerializeField, Tooltip("누웠을 때 발이 자리 가운데에서 떨어진 거리 (m)")] float lieFeetOffset = 0.57f;
        [SerializeField, Tooltip("누웠을 때 자리보다 올리는 높이 (m)")] float lieHeight;
        [SerializeField, Tooltip("누웠을 때 카메라가 도는 중심의 높이 (매트리스 가운데 기준, m)")] float lyingPivotHeight = 0.2f;
        [SerializeField, Tooltip("누웠을 때 카메라 거리 (m). 침대 틀이나 벽에 막히면 더 가까워진다")] float lyingCameraDistance = 1.4f;
        [SerializeField, Tooltip("조작 값을 보내는 간격 (초)")] float sendInterval = 0.1f;

        const string BusyReason = "다른 선원이 쓰는 중";

        Interactable interactable;
        InteractionHub hub;
        SeatControl control;
        int index = -1;

        // 내가 앉아 있을 때
        bool mine;
        GameObject seatedPlayer;
        float localValue;
        float sentValue;
        float sendTimer;
        PlayerCamera playerCamera;
        Transform lyingPivot;

        public string LeavePrompt => leavePrompt;
        public string ControlsHint => control != null ? control.ControlsHint : null;

        void Awake()
        {
            interactable = GetComponent<Interactable>();
            control = GetComponent<SeatControl>();
            hub = GetComponentInParent<InteractionHub>();
            if (hub != null)
            {
                index = hub.IndexOf(interactable);
            }
            interactable.Prompt = usePrompt;
        }

        void OnEnable()
        {
            interactable.Interacted += OnUse;
            if (hub != null)
            {
                hub.Changed += OnHubChanged;
            }
            Refresh();
            if (control != null)
            {
                control.Apply(CurrentValue);
            }
        }

        void OnDisable()
        {
            interactable.Interacted -= OnUse;
            if (hub != null)
            {
                hub.Changed -= OnHubChanged;
            }
            if (mine)
            {
                Exit();
            }
        }

        float CurrentValue => hub != null && index >= 0 ? hub.GetValue(index) : (control != null ? control.InitialValue : 0f);

        void OnUse(GameObject who)
        {
            if (hub == null || index < 0)
            {
                return;
            }
            hub.RequestOccupy(index);
        }

        public void Leave()
        {
            if (!mine)
            {
                return;
            }
            if (control != null && hub != null)
            {
                hub.SubmitValue(index, localValue);
            }
            hub.RequestRelease(index);
        }

        void OnHubChanged(int changed)
        {
            if (changed < 0 || changed == index)
            {
                Refresh();
            }
        }

        void Refresh()
        {
            if (hub == null || index < 0)
            {
                return;
            }
            ulong occupant = hub.GetOccupant(index);
            ulong me = hub.LocalClientId;
            bool nowMine = occupant == me && PlayerInteractor.Local != null;

            interactable.BlockedReason = occupant != InteractionHub.Nobody && !nowMine ? BusyReason : null;
            interactable.Available = !nowMine;

            if (nowMine && !mine)
            {
                Enter();
            }
            else if (!nowMine && mine)
            {
                Exit();
            }

            if (control != null && !mine)
            {
                control.Apply(CurrentValue);
            }
        }

        void Enter()
        {
            var interactor = PlayerInteractor.Local;
            seatedPlayer = interactor.gameObject;
            mine = true;
            interactor.Seat = this;
            localValue = sentValue = CurrentValue;

            var motor = seatedPlayer.GetComponent<PlayerMotor>();
            if (motor != null)
            {
                motor.Stop();
                motor.enabled = false;
            }
            SetEnabled<PlayerInputHandler>(false);
            SetEnabled<CharacterController>(false);
            SnapPlayer();

            var animator = Animator;
            if (animator != null)
            {
                animator.SetBaseAction(loopAction);
                animator.PlayOnce(enterAction);
            }

            // 누우면 몸 가운데를 중심으로 카메라를 돌린다. 침대 틀·벽에 막히면 앞으로 당겨져서 배 밖으로 나가지 않는다.
            if (lieDown)
            {
                playerCamera = FindAnyObjectByType<PlayerCamera>();
                if (playerCamera != null)
                {
                    playerCamera.PivotOverride = GetLyingPivot();
                    playerCamera.DistanceOverride = lyingCameraDistance;
                }
            }
        }

        Transform GetLyingPivot()
        {
            if (lyingPivot == null)
            {
                Transform p = pose != null ? pose : transform;
                lyingPivot = new GameObject("LyingCameraPivot").transform;
                lyingPivot.SetParent(p, false);
                lyingPivot.localPosition = Vector3.up * lyingPivotHeight;
            }
            return lyingPivot;
        }

        void Exit()
        {
            mine = false;
            if (seatedPlayer == null)
            {
                return;
            }
            var interactor = seatedPlayer.GetComponent<PlayerInteractor>();
            if (interactor != null && ReferenceEquals(interactor.Seat, this))
            {
                interactor.Seat = null;
            }
            SetEnabled<CharacterController>(true);
            var motor = seatedPlayer.GetComponent<PlayerMotor>();
            if (motor != null)
            {
                motor.enabled = true;
            }
            SetEnabled<PlayerInputHandler>(true);
            if (playerCamera != null)
            {
                playerCamera.PivotOverride = null;
                playerCamera.DistanceOverride = 0f;
                playerCamera = null;
            }
            var animator = Animator;
            if (animator != null)
            {
                animator.SetBaseAction(null);
                animator.PlayOnce(exitAction, holdStill: true);
            }

            // 상호작용 지점(자리 앞)에 내려 준다.
            Vector3 exitPosition = interactable.Position + Vector3.up * 0.05f;
            // 앉아 있던 방향 그대로 일어난다. 누워 있었으면 상호작용 지점의 방향으로.
            float exitYaw = lieDown ? transform.eulerAngles.y : seatedPlayer.transform.eulerAngles.y;
            Quaternion exitRotation = Quaternion.Euler(0f, exitYaw, 0f);
            var networkPlayer = seatedPlayer.GetComponent<NetworkPlayer>();
            if (networkPlayer != null && networkPlayer.IsSpawned)
            {
                networkPlayer.TeleportTo(exitPosition, exitRotation);
            }
            else if (motor != null)
            {
                motor.Teleport(exitPosition);
                seatedPlayer.transform.rotation = exitRotation;
            }
            seatedPlayer = null;
        }

        void LateUpdate()
        {
            if (!mine)
            {
                return;
            }
            if (seatedPlayer == null)
            {
                mine = false;
                return;
            }
            if (control != null)
            {
                localValue = control.Tick(localValue, Time.deltaTime);
                control.Apply(localValue);
                sendTimer -= Time.deltaTime;
                if (sendTimer <= 0f && !Mathf.Approximately(localValue, sentValue))
                {
                    hub.SubmitValue(index, localValue);
                    sentValue = localValue;
                    sendTimer = sendInterval;
                }
            }
            var seatAnimator = Animator;
            if (seatAnimator != null)
            {
                string loop = control != null ? control.LoopAction : null;
                seatAnimator.SetBaseAction(loop ?? loopAction);
            }
            SnapPlayer();
        }

        void SnapPlayer()
        {
            GetSeatPose(out Vector3 position, out Quaternion rotation);
            seatedPlayer.transform.SetPositionAndRotation(position, rotation);
        }

        void GetSeatPose(out Vector3 position, out Quaternion rotation)
        {
            Transform p = pose != null ? pose : transform;
            if (!lieDown)
            {
                position = p.position;
                Vector3 toTarget = faceTarget != null ? Vector3.ProjectOnPlane(faceTarget.position - position, Vector3.up) : Vector3.zero;
                rotation = toTarget.sqrMagnitude > 0.0001f
                    ? Quaternion.LookRotation(toTarget.normalized, Vector3.up)
                    : Quaternion.Euler(0f, p.eulerAngles.y, 0f);
                return;
            }
            // 머리는 자리의 앞쪽(베개 쪽). 누운 동작에서 머리는 캐릭터 뒤쪽을 향하므로, 캐릭터는 반대쪽을 본다.
            Vector3 head = Vector3.ProjectOnPlane(p.forward, Vector3.up).normalized;
            rotation = Quaternion.LookRotation(-head, Vector3.up);
            position = p.position + rotation * Vector3.forward * lieFeetOffset + Vector3.up * lieHeight;
        }

        void SetEnabled<T>(bool value) where T : Component
        {
            var component = seatedPlayer != null ? seatedPlayer.GetComponent<T>() : null;
            if (component is Behaviour behaviour)
            {
                behaviour.enabled = value;
            }
            else if (component is Collider collider)
            {
                collider.enabled = value;
            }
        }

        PlayerAnimator Animator => seatedPlayer != null ? seatedPlayer.GetComponent<PlayerAnimator>() : null;
    }
}
