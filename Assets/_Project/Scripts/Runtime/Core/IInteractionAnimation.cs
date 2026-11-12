namespace Letsail.Core
{
    /// <summary>
    /// 상호작용할 때 캐릭터가 할 동작. Interactable 과 같은 오브젝트에 단다.
    /// 값은 캐릭터 애니메이터의 동작 상태 이름이고, 없으면 null.
    /// </summary>
    public interface IInteractionAnimation
    {
        /// <summary>길게 누르는 동안 반복할 동작 (예: 닻 감기).</summary>
        string HoldAction { get; }

        /// <summary>실행할 때 한 번 재생할 동작 (예: 서랍 열기). 지금 상태에 따라 달라질 수 있다.</summary>
        string UseAction { get; }
    }
}
