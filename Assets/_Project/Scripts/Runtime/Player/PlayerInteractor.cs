using System;
using Letsail.Core;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Letsail.Player
{
    /// <summary>
    /// 내 캐릭터 근처의 상호작용 대상 중 화면 가운데(카메라가 보는 방향)에 가장 가까운 것을 고르고,
    /// 상호작용 키(E)를 누르면 실행한다. 길게 누르는 대상은 정해진 시간 동안 누르고 있어야 한다.
    /// 벽·갑판 너머에 있거나 발 높이에서 멀리 떨어진 대상(다른 층)은 고르지 않는다.
    /// 대상에 IInteractionAnimation 이 있으면 그 동작을 캐릭터가 재생한다.
    /// 자리(키, 대포, 침대 등)에 있는 동안은 대상을 고르지 않고, E 로 자리에서 일어난다.
    /// 멀티플레이에서는 내 캐릭터에만 켜 둔다.
    /// </summary>
    [RequireComponent(typeof(PlayerMotor))]
    public sealed class PlayerInteractor : MonoBehaviour
    {
        [SerializeField, Tooltip("카메라가 보는 방향에서 이 각도 안에 있는 대상만 고른다 (도)")]
        float maxViewAngle = 60f;
        [SerializeField, Tooltip("고를 때 각도와 거리를 합치는 비율. 1m 멀어지는 것을 몇 도 벗어난 것으로 칠지")]
        float degreesPerMeter = 12f;
        [SerializeField, Tooltip("대상의 서는 자리가 내 발보다 이만큼 위까지만 고른다 (m)")] float maxClimb = 1.5f;
        [SerializeField, Tooltip("대상의 서는 자리가 내 발보다 이만큼 아래까지만 고른다 (m)")] float maxDrop = 1.2f;
        [SerializeField, Tooltip("시야를 재는 눈높이 (m)")] float eyeHeight = 1f;
        [SerializeField, Tooltip("대상 바로 앞 이 거리 안에서 막힌 것은 막힘으로 치지 않는다 (대상의 틀 등, m)")] float sightTolerance = 0.7f;
        [SerializeField, Tooltip("시야를 가리는 것으로 볼 레이어. 플레이어 레이어는 뺀다")] LayerMask sightMask = ~(1 << 6);

        InputAction interactAction;
        PlayerAnimator playerAnimator;
        float holdTime;
        string holdAnimation;
        // 길게 누르기를 끝낸 뒤, 키를 뗄 때까지 다시 시작하지 않는다.
        bool waitForRelease;

        // 켜져 있는 것들. 다른 선원의 캐릭터도 생길 때 잠깐 켜졌다가 꺼지므로, 마지막 하나만 믿지 않고 목록으로 관리한다.
        static readonly System.Collections.Generic.List<PlayerInteractor> enabledInstances = new System.Collections.Generic.List<PlayerInteractor>();

        /// <summary>이 PC에서 조작하는 캐릭터의 것 (켜져 있는 것은 내 캐릭터뿐이다). 없으면 null.</summary>
        public static PlayerInteractor Local => enabledInstances.Count > 0 ? enabledInstances[enabledInstances.Count - 1] : null;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            enabledInstances.Clear();
        }

        /// <summary>지금 안내가 떠 있는 대상. 없으면 null.</summary>
        public Interactable Focused { get; private set; }

        /// <summary>true 면 대상을 고르지 않고 키도 받지 않는다 (사다리를 오르내리는 중 등).</summary>
        public bool Busy { get; set; }

        /// <summary>지금 앉아 있는 자리. 없으면 null.</summary>
        public IInteractionSeat Seat { get; set; }

        /// <summary>길게 누르는 중인 진행 정도 (0~1). 누르고 있지 않으면 0.</summary>
        public float HoldProgress => Focused != null && Focused.Mode == Interactable.InteractMode.Hold
            ? Mathf.Clamp01(holdTime / Mathf.Max(0.01f, Focused.HoldSeconds))
            : 0f;

        /// <summary>안내 대상이 바뀔 때.</summary>
        public static event Action<Interactable> FocusChanged;

        void Awake()
        {
            interactAction = InputSystem.actions.FindAction("Player/Interact", true);
            playerAnimator = GetComponent<PlayerAnimator>();
        }

        void OnEnable()
        {
            enabledInstances.Remove(this);
            enabledInstances.Add(this);
        }

        void OnDisable()
        {
            SetFocus(null);
            enabledInstances.Remove(this);
        }

        void Update()
        {
            if (Busy)
            {
                SetFocus(null);
                waitForRelease = true;
                return;
            }
            if (Seat != null)
            {
                SetFocus(null);
                if (interactAction.WasPressedThisFrame())
                {
                    Seat.Leave();
                    waitForRelease = true;
                }
                return;
            }

            SetFocus(FindBest());
            if (waitForRelease)
            {
                waitForRelease = interactAction.IsPressed();
                return;
            }
            if (Focused == null || Focused.IsBlocked)
            {
                return;
            }

            if (Focused.Mode == Interactable.InteractMode.Press)
            {
                if (interactAction.WasPressedThisFrame())
                {
                    Use(Focused);
                    waitForRelease = true;
                }
                return;
            }

            if (!interactAction.IsPressed())
            {
                holdTime = 0f;
                SetHoldAnimation(null);
                return;
            }
            holdTime += Time.deltaTime;
            SetHoldAnimation(Focused.GetComponent<IInteractionAnimation>()?.HoldAction);
            if (holdTime >= Focused.HoldSeconds)
            {
                holdTime = 0f;
                waitForRelease = true;
                SetHoldAnimation(null);
                Use(Focused);
            }
        }

        void Use(Interactable target)
        {
            // 동작은 실행 전 상태로 고른다 (닫힌 서랍을 열면 여는 동작).
            string action = target.GetComponent<IInteractionAnimation>()?.UseAction;
            target.Interact(gameObject);
            if (action != null && playerAnimator != null)
            {
                // 제자리 동작은 대상을 바라보고 한다 (서랍, 화덕, 줄사다리 등).
                Vector3 toTarget = Vector3.ProjectOnPlane((target.AimTarget != null ? target.AimPosition : target.Position + target.transform.forward) - transform.position, Vector3.up);
                if (toTarget.sqrMagnitude > 0.0001f)
                {
                    transform.rotation = Quaternion.LookRotation(toTarget.normalized, Vector3.up);
                }
                playerAnimator.PlayOnce(action, holdStill: true);
            }
        }

        void SetHoldAnimation(string action)
        {
            if (playerAnimator == null || holdAnimation == action)
            {
                return;
            }
            holdAnimation = action;
            playerAnimator.SetBaseAction(action);
        }

        Interactable FindBest()
        {
            // 화면 가운데(카메라가 보는 방향)에 가장 가까운 대상을 고른다. 서랍과 침대처럼 붙어 있는 대상도 바라보는 쪽으로 고를 수 있다.
            Transform cam = Camera.main != null ? Camera.main.transform : null;
            Vector3 eye = cam != null ? cam.position : transform.position + Vector3.up * 1f;
            Vector3 look = cam != null ? cam.forward : transform.forward;
            Vector3 origin = transform.position;
            Vector3 head = origin + Vector3.up * eyeHeight;

            Interactable best = null;
            float bestScore = float.MaxValue;
            foreach (var candidate in Interactable.All)
            {
                if (!candidate.Available)
                {
                    continue;
                }
                Vector3 offset = candidate.Position - origin;
                float distance = new Vector2(offset.x, offset.z).magnitude;
                // 서는 자리가 내 발 높이 근처여야 한다 (점프해도 위층 대상은 고르지 않는다).
                if (distance > candidate.Radius || offset.y < -maxDrop || offset.y > maxClimb)
                {
                    continue;
                }
                Vector3 aim = candidate.AimPosition;
                float angle = Vector3.Angle(look, aim - eye);
                // 아주 가까우면 방향은 따지지 않는다.
                if (distance > 0.6f && angle > maxViewAngle)
                {
                    continue;
                }
                if (IsBlocked(head, aim, candidate))
                {
                    continue;
                }
                float score = angle + distance * degreesPerMeter;
                if (score < bestScore)
                {
                    best = candidate;
                    bestScore = score;
                }
            }
            return best;
        }

        // 내 머리에서 대상까지 갑판·벽이 막고 있으면 고르지 않는다. 대상 바로 앞(대상 자체의 틀 등)은 막힘으로 치지 않는다.
        bool IsBlocked(Vector3 from, Vector3 to, Interactable candidate)
        {
            Vector3 dir = to - from;
            float length = dir.magnitude;
            if (length < 0.01f)
            {
                return false;
            }
            if (!Physics.Raycast(from, dir / length, out RaycastHit hit, length, sightMask, QueryTriggerInteraction.Ignore))
            {
                return false;
            }
            if (candidate.AimTarget != null && hit.collider.transform.IsChildOf(candidate.AimTarget))
            {
                return false;
            }
            return hit.distance < length - sightTolerance;
        }

        void SetFocus(Interactable target)
        {
            if (Focused == target)
            {
                return;
            }
            Focused = target;
            holdTime = 0f;
            SetHoldAnimation(null);
            FocusChanged?.Invoke(target);
        }
    }
}
