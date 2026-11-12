using UnityEngine;

namespace Letsail.Interaction
{
    /// <summary>
    /// 자리(SeatInteraction)에 앉은 사람이 조작하는 값 하나 (키 각도, 돛 펼침, 대포 각도 등).
    /// 앉은 사람의 PC 에서는 Tick 으로 입력을 읽어 값을 바꾸고, 모든 PC 에서 Apply 로 모습에 반영한다.
    /// 값은 InteractionHub 가 다른 선원에게 보내 준다.
    /// </summary>
    public abstract class SeatControl : MonoBehaviour
    {
        [SerializeField, Tooltip("처음 값")] float initialValue;

        bool initialized;

        public float InitialValue => initialValue;

        /// <summary>자리에 있는 동안 보여 줄 조작 안내.</summary>
        public virtual string ControlsHint => null;

        /// <summary>앉은 사람의 지금 동작 (예: 키를 돌리는 중이면 Helm_TurnLeft). null 이면 자리의 기본 동작.</summary>
        public virtual string LoopAction => null;

        /// <summary>앉은 사람의 PC 에서 매 프레임. 입력을 읽어 새 값을 돌려준다.</summary>
        public float Tick(float value, float deltaTime)
        {
            EnsureInitialized();
            return OnTick(value, deltaTime);
        }

        /// <summary>값을 모습에 반영한다 (모든 PC).</summary>
        public void Apply(float value)
        {
            EnsureInitialized();
            OnApply(value);
        }

        // 같은 오브젝트의 SeatInteraction 이 이 컴포넌트의 Awake 보다 먼저 Apply 를 부를 수 있어서, 처음 쓰일 때 준비한다.
        void EnsureInitialized()
        {
            if (!initialized)
            {
                initialized = true;
                Initialize();
            }
        }

        /// <summary>처음 쓰이기 전에 한 번. 원래 모습(회전, 크기)을 기억해 둔다.</summary>
        protected abstract void Initialize();

        protected abstract float OnTick(float value, float deltaTime);

        protected abstract void OnApply(float value);
    }
}
