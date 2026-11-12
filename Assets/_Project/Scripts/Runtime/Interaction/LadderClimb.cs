using System.Collections;
using Letsail.Core;
using Letsail.Networking;
using Letsail.Player;
using UnityEngine;

namespace Letsail.Interaction
{
    /// <summary>
    /// 사다리·돛대 그물을 따라 실제로 오르내린다. 사다리 아래쪽 끝과 위쪽 끝 상호작용에 하나씩 단다.
    /// 오르기: 아래에서 타기 → 사다리를 따라 오르기 → 위로 나가기 / 내려가기: 위에서 타기 → 내려가기 → 아래로 내리기.
    /// 캐릭터 동작은 모두 제자리 동작이라 위치는 여기서 옮긴다. 사다리 면은 캐릭터 앞 약 0.25m 에 있다고 보고 만든 동작이다.
    /// 위로 나가는 동작(Ladder_Exit_Top 등)은 끝날 때 몸이 앞·위로 옮겨져 있으므로, 그만큼 루트를 덜 옮겼다가 끝에 맞춘다.
    /// 오르내리는 동안은 다른 상호작용을 할 수 없다. 위치는 NetworkTransform 이, 동작은 NetworkAnimator 가 다른 선원에게 보낸다.
    /// </summary>
    [RequireComponent(typeof(Interactable))]
    public sealed class LadderClimb : MonoBehaviour
    {
        [SerializeField, Tooltip("이 상호작용이 사다리 위쪽 끝이면 체크 (내려가기)")] bool fromTop;
        [SerializeField, Tooltip("사다리 아래에 서는 자리")] Transform bottomStand;
        [SerializeField, Tooltip("사다리 위에 서는 자리")] Transform topStand;
        [SerializeField, Tooltip("맨 아래 가로대를 딛고 매달린 자리. 앞쪽(+Z)이 사다리를 바라보는 방향")] Transform bottomHang;
        [SerializeField, Tooltip("맨 위에서 매달린 자리 (여기서 위로 나가는 동작을 한다)")] Transform topHang;

        [Header("캐릭터 동작")]
        [SerializeField] string enterBottom = "Ladder_Enter_Bottom";
        [SerializeField] string climbUp = "Ladder_Climb_Up";
        [SerializeField] string exitTop = "Ladder_Exit_Top";
        [SerializeField] string enterTop = "Ladder_Enter_Top";
        [SerializeField] string climbDown = "Ladder_Climb_Down";
        [SerializeField] string exitBottom = "Ladder_Exit_Bottom";
        [SerializeField, Tooltip("위로 나가는 동작이 끝났을 때 몸이 옮겨져 있는 거리 (앞, 위, m)")] Vector2 topClipOffset = new Vector2(0.42f, 0.62f);

        [Header("속도")]
        [SerializeField, Tooltip("오르내리는 속도 (m/s)")] float climbSpeed = 0.55f;
        [SerializeField, Tooltip("오르기 동작이 손발이 맞는 속도 (m/s). 이보다 빠르면 동작도 빨리 재생한다")] float clipMetersPerSecond = 0.55f;

        [Header("해치")]
        [SerializeField, Tooltip("사다리 위를 막고 있는 해치 (있으면 탈 때 열고, 다 지나가면 닫는다)")] ToggleInteraction hatch;
        [SerializeField, Tooltip("위에서 내려갈 때 해치가 열리기를 기다리는 시간 (초)")] float hatchOpenWait = 1.4f;
        [SerializeField, Tooltip("다 지나간 뒤 해치를 닫기까지 기다리는 시간 (초)")] float hatchCloseDelay = 0.4f;

        Interactable interactable;
        Coroutine climbing;
        GameObject climber;

        void Awake()
        {
            interactable = GetComponent<Interactable>();
        }

        void OnEnable()
        {
            interactable.Interacted += OnUse;
        }

        void OnDisable()
        {
            interactable.Interacted -= OnUse;
            if (climbing != null)
            {
                StopCoroutine(climbing);
                Finish(climber, climber != null ? climber.transform.position : Vector3.zero, climber != null ? climber.transform.rotation : Quaternion.identity);
            }
        }

        void OnUse(GameObject who)
        {
            if (climbing != null || who == null || bottomStand == null || topStand == null || bottomHang == null || topHang == null)
            {
                return;
            }
            // 다른 사다리(같은 사다리의 반대쪽 끝 포함)를 타는 중이면 무시한다.
            var interactor = who.GetComponent<PlayerInteractor>();
            if (interactor != null && interactor.Busy)
            {
                return;
            }
            climbing =StartCoroutine(fromTop ? ClimbDown(who) : ClimbUp(who));
        }

        Quaternion Facing => Quaternion.LookRotation(Vector3.ProjectOnPlane(bottomHang.forward, Vector3.up).normalized, Vector3.up);

        // 위로 나가는 동작이 끝났을 때 몸이 옮겨져 있는 양 (월드)
        Vector3 TopOffset => Facing * Vector3.forward * topClipOffset.x + Vector3.up * topClipOffset.y;

        IEnumerator ClimbUp(GameObject who)
        {
            Begin(who);
            bool openedHatch = OpenHatch();
            var animator = who.GetComponent<PlayerAnimator>();
            yield return Move(who, animator, enterBottom, () => bottomStand.position, () => bottomHang.position, Duration(animator, enterBottom), 1f);
            float length = Vector3.Distance(bottomHang.position, topHang.position);
            yield return Move(who, animator, climbUp, () => bottomHang.position, () => topHang.position, length / climbSpeed, climbSpeed / clipMetersPerSecond);
            yield return Move(who, animator, exitTop, () => topHang.position, () => topStand.position - TopOffset, Duration(animator, exitTop), 1f);
            Finish(who, topStand.position, Facing);
            CloseHatchLater(openedHatch);
        }

        IEnumerator ClimbDown(GameObject who)
        {
            Begin(who);
            var animator = who.GetComponent<PlayerAnimator>();
            // 닫힌 해치를 몸으로 뚫지 않게, 다 열린 뒤에 내려간다.
            bool openedHatch = OpenHatch();
            if (openedHatch)
            {
                yield return new WaitForSeconds(hatchOpenWait);
            }
            // 위에서 타는 동작은 몸이 앞·위로 옮겨진 자세(갑판에 선 모습)로 시작한다.
            yield return Move(who, animator, enterTop, () => topStand.position - TopOffset, () => topHang.position, Duration(animator, enterTop), 1f);
            float length = Vector3.Distance(bottomHang.position, topHang.position);
            yield return Move(who, animator, climbDown, () => topHang.position, () => bottomHang.position, length / climbSpeed, climbSpeed / clipMetersPerSecond);
            yield return Move(who, animator, exitBottom, () => bottomHang.position, () => bottomStand.position, Duration(animator, exitBottom), 1f);
            Finish(who, bottomStand.position, Facing);
            CloseHatchLater(openedHatch);
        }

        // 해치가 닫혀 있으면 연다. 이번에 열었으면 true (다 지나간 뒤 닫는다).
        bool OpenHatch()
        {
            if (hatch == null || hatch.IsOn)
            {
                return false;
            }
            hatch.RequestSet(true);
            return true;
        }

        void CloseHatchLater(bool openedHatch)
        {
            if (openedHatch && isActiveAndEnabled)
            {
                StartCoroutine(CloseHatch());
            }
        }

        IEnumerator CloseHatch()
        {
            yield return new WaitForSeconds(hatchCloseDelay);
            if (hatch != null)
            {
                hatch.RequestSet(false);
            }
        }

        static float Duration(PlayerAnimator animator, string clip) => animator != null ? animator.GetClipLength(clip) : 0.5f;

        // 배가 움직여도 따라가도록 매 프레임 지점 위치를 다시 읽는다.
        IEnumerator Move(GameObject who, PlayerAnimator animator, string action, System.Func<Vector3> from, System.Func<Vector3> to, float duration, float animSpeed)
        {
            if (animator != null)
            {
                animator.SetBaseAction(action);
                animator.ActionSpeed = animSpeed;
            }
            float time = 0f;
            while (who != null)
            {
                float t = duration > 0f ? Mathf.Clamp01(time / duration) : 1f;
                who.transform.SetPositionAndRotation(Vector3.Lerp(from(), to(), t), Facing);
                if (t >= 1f)
                {
                    yield break;
                }
                yield return null;
                time += Time.deltaTime;
            }
        }

        void Begin(GameObject who)
        {
            climber = who;
            var interactor = who.GetComponent<PlayerInteractor>();
            if (interactor != null) interactor.Busy = true;
            var motor = who.GetComponent<PlayerMotor>();
            if (motor != null)
            {
                motor.Stop();
                motor.enabled = false;
            }
            var input = who.GetComponent<PlayerInputHandler>();
            if (input != null) input.enabled = false;
            var controller = who.GetComponent<CharacterController>();
            if (controller != null) controller.enabled = false;
        }

        void Finish(GameObject who, Vector3 position, Quaternion rotation)
        {
            climbing = null;
            climber = null;
            if (who == null)
            {
                return;
            }
            var animator = who.GetComponent<PlayerAnimator>();
            if (animator != null)
            {
                animator.SetBaseAction(null);
                animator.ActionSpeed = 1f;
            }
            var controller = who.GetComponent<CharacterController>();
            if (controller != null) controller.enabled = true;
            var motor = who.GetComponent<PlayerMotor>();
            if (motor != null) motor.enabled = true;
            var input = who.GetComponent<PlayerInputHandler>();
            if (input != null) input.enabled = true;
            var interactor = who.GetComponent<PlayerInteractor>();
            if (interactor != null) interactor.Busy = false;

            Vector3 standPosition = position + Vector3.up * 0.02f;
            var networkPlayer = who.GetComponent<NetworkPlayer>();
            if (networkPlayer != null && networkPlayer.IsSpawned)
            {
                networkPlayer.TeleportTo(standPosition, rotation);
            }
            else if (motor != null)
            {
                motor.Teleport(standPosition);
                who.transform.rotation = rotation;
            }
        }
    }
}
