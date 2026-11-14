using Letsail.Core;
using UnityEngine;

namespace Letsail.Player
{
    /// <summary>쓰러진 동료를 일으키는 동안 하는 동작. 일으키기 Interactable 과 같은 오브젝트에 둔다.</summary>
    public sealed class ReviveAnimation : MonoBehaviour, IInteractionAnimation
    {
        public string HoldAction => "Revive";
        public string UseAction => null;
    }
}
