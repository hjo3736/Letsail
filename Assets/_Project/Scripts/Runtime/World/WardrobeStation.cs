using Letsail.Core;
using Letsail.UI;
using UnityEngine;

namespace Letsail.World
{
    /// <summary>
    /// 옷장. 상호작용하면 캐릭터가 거울 앞 자리로 가고, 카메라가 옷장 시점으로 바뀌며 복장 화면이 열린다.
    /// </summary>
    [RequireComponent(typeof(Interactable))]
    public sealed class WardrobeStation : MonoBehaviour
    {
        [SerializeField, Tooltip("옷을 고르는 동안 카메라가 있을 자리")] Transform cameraPose;
        [SerializeField, Tooltip("옷을 고르는 동안 캐릭터가 서 있을 자리")] Transform playerSpot;

        Interactable interactable;

        public Transform CameraPose => cameraPose;
        public Transform PlayerSpot => playerSpot;

        void Awake()
        {
            interactable = GetComponent<Interactable>();
        }

        void OnEnable()
        {
            interactable.Interacted += OnInteracted;
        }

        void OnDisable()
        {
            interactable.Interacted -= OnInteracted;
        }

        void OnInteracted(GameObject player)
        {
            var ui = WardrobeUI.Instance != null ? WardrobeUI.Instance : FindAnyObjectByType<WardrobeUI>(FindObjectsInactive.Include);
            if (ui != null)
            {
                ui.Open(this, player);
            }
        }
    }
}
