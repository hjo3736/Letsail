using System.Collections;
using Letsail.Core;
using Letsail.Networking;
using Letsail.Player;
using UnityEngine;

namespace Letsail.Interaction
{
    /// <summary>
    /// 사다리 오르내리기 (해치 사다리, 바다에서 줄사다리로 오르기). 반대쪽 끝으로 바로 옮겨 준다.
    /// 해치가 있으면 옮기는 순간 해치가 빠르게 열렸다 닫힌다. 도착하면 사다리를 등지고 선다.
    /// </summary>
    [RequireComponent(typeof(Interactable))]
    public sealed class TeleportInteraction : MonoBehaviour
    {
        [SerializeField, Tooltip("도착할 지점. 그 지점이 바라보는 반대쪽을 보고 선다")] Transform destination;
        [SerializeField, Tooltip("도착 지점에서 비켜 설 거리 (도착 지점 기준). 지점이 계단 등에 묻혀 있을 때")] Vector3 landingOffset;
        [SerializeField, Tooltip("이것이 켜져 있을 때만 쓸 수 있다 (예: 줄사다리를 내렸을 때만 바다에서 올라오기). 비우면 항상")]
        ToggleInteraction requiredToggle;
        [SerializeField, Tooltip("도착해서 한 번 재생할 캐릭터 동작 (예: Climb_Over_Rail)")] string arriveAction;
        [SerializeField, Tooltip("지나가는 동안 열었다 닫을 해치 (없어도 된다)")] ToggleInteraction hatch;
        [SerializeField, Tooltip("해치를 열고 옮기기까지 기다리는 시간 (초)")] float hatchOpenDelay = 0.15f;
        [SerializeField, Tooltip("옮긴 뒤 해치를 닫기까지 기다리는 시간 (초)")] float hatchCloseDelay = 0.35f;

        Interactable interactable;

        public Transform Destination
        {
            get => destination;
            set => destination = value;
        }

        void Awake()
        {
            interactable = GetComponent<Interactable>();
        }

        void OnEnable()
        {
            interactable.Interacted += OnUse;
            if (requiredToggle != null)
            {
                requiredToggle.StateChanged += OnRequiredChanged;
                interactable.Available = requiredToggle.IsOn;
            }
        }

        void OnDisable()
        {
            interactable.Interacted -= OnUse;
            if (requiredToggle != null)
            {
                requiredToggle.StateChanged -= OnRequiredChanged;
            }
        }

        void OnRequiredChanged(bool on, bool instant)
        {
            interactable.Available = on;
        }

        void OnUse(GameObject who)
        {
            if (destination == null || who == null)
            {
                return;
            }
            if (hatch != null)
            {
                StartCoroutine(ThroughHatch(who));
                return;
            }
            MoveToDestination(who);
        }

        IEnumerator ThroughHatch(GameObject who)
        {
            hatch.RequestSet(true);
            yield return new WaitForSeconds(hatchOpenDelay);
            if (who != null)
            {
                MoveToDestination(who);
            }
            yield return new WaitForSeconds(hatchCloseDelay);
            hatch.RequestSet(false);
        }

        void MoveToDestination(GameObject who)
        {
            Vector3 position = destination.TransformPoint(landingOffset) + Vector3.up * 0.05f;
            Quaternion rotation = Quaternion.Euler(0f, destination.eulerAngles.y + 180f, 0f);
            var networkPlayer = who.GetComponent<NetworkPlayer>();
            if (networkPlayer != null && networkPlayer.IsSpawned)
            {
                networkPlayer.TeleportTo(position, rotation);
            }
            else
            {
                var motor = who.GetComponent<PlayerMotor>();
                if (motor != null)
                {
                    motor.Teleport(position);
                    who.transform.rotation = rotation;
                }
            }
            var animator = who.GetComponent<PlayerAnimator>();
            if (animator != null)
            {
                animator.PlayOnce(arriveAction, holdStill: true);
            }
        }
    }
}
