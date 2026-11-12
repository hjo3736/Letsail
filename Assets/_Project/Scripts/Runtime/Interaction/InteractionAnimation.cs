using Letsail.Core;
using UnityEngine;

namespace Letsail.Interaction
{
    /// <summary>
    /// 상호작용할 때 캐릭터가 할 동작 (캐릭터 애니메이터의 상태 이름).
    /// 켜고 끄는 대상이면 켜져 있을 때 다른 동작을 줄 수 있다 (서랍 열기 / 닫기).
    /// </summary>
    [RequireComponent(typeof(Interactable))]
    public sealed class InteractionAnimation : MonoBehaviour, IInteractionAnimation
    {
        [SerializeField, Tooltip("길게 누르는 동안 반복할 동작 (예: Anchor_Crank)")] string holdAction;
        [SerializeField, Tooltip("실행할 때 한 번 재생할 동작 (예: Drawer_Open)")] string useAction;
        [SerializeField, Tooltip("켜져 있을 때 실행하면 대신 재생할 동작 (예: Drawer_Close). 비우면 useAction")] string useActionWhenOn;

        ToggleInteraction toggle;

        public string HoldAction => string.IsNullOrEmpty(holdAction) ? null : holdAction;

        public string UseAction
        {
            get
            {
                if (toggle != null && toggle.IsOn && !string.IsNullOrEmpty(useActionWhenOn))
                {
                    return useActionWhenOn;
                }
                return string.IsNullOrEmpty(useAction) ? null : useAction;
            }
        }

        void Awake()
        {
            toggle = GetComponent<ToggleInteraction>();
        }
    }
}
