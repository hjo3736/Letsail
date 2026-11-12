namespace Letsail.Core
{
    /// <summary>
    /// 플레이어가 자리를 잡고 조작하는 상호작용 (키, 돛, 대포, 침대 등).
    /// 자리에 있는 동안 상호작용 키(E)는 다른 대상을 고르지 않고 자리에서 일어나는 데 쓴다.
    /// </summary>
    public interface IInteractionSeat
    {
        /// <summary>자리에서 일어날 때의 안내 (예: 키 놓기).</summary>
        string LeavePrompt { get; }

        /// <summary>자리에 있는 동안의 조작 안내 (예: A·D: 키 돌리기). 없으면 null.</summary>
        string ControlsHint { get; }

        void Leave();
    }
}
