using UnityEngine;

namespace Letsail.Interaction
{
    /// <summary>
    /// ToggleInteraction 의 켜짐/꺼짐을 모습으로 보여 주는 컴포넌트의 기반.
    /// 켜짐 정도(0~1)를 speed 로 서서히 바꾸고, 그 값으로 ApplyAmount 를 부른다.
    /// </summary>
    [RequireComponent(typeof(ToggleInteraction))]
    public abstract class ToggleVisual : MonoBehaviour
    {
        [SerializeField, Tooltip("완전히 바뀌는 데 걸리는 시간 (초). 0 이면 바로")] float duration = 0.4f;

        ToggleInteraction toggle;
        float amount;
        float goal;

        protected bool IsMoving => !Mathf.Approximately(amount, goal);

        protected virtual void Awake()
        {
            toggle = GetComponent<ToggleInteraction>();
            amount = goal = toggle.IsOn ? 1f : 0f;
        }

        protected virtual void OnEnable()
        {
            toggle.StateChanged += OnStateChanged;
            ApplyAmount(amount);
        }

        protected virtual void OnDisable()
        {
            toggle.StateChanged -= OnStateChanged;
        }

        void OnStateChanged(bool on, bool instant)
        {
            goal = on ? 1f : 0f;
            if (instant || duration <= 0f)
            {
                amount = goal;
                ApplyAmount(amount);
            }
        }

        protected virtual void Update()
        {
            if (!IsMoving)
            {
                return;
            }
            float step = Time.deltaTime / Mathf.Max(0.01f, duration);
            float before = amount;
            amount = Mathf.MoveTowards(amount, goal, step);
            ApplyAmount(amount);
            OnMoved(amount - before);
        }

        /// <summary>켜짐 정도(0 = 꺼짐, 1 = 켜짐)를 모습에 반영한다.</summary>
        protected abstract void ApplyAmount(float value);

        /// <summary>움직이는 동안 매 프레임 (바뀐 양). 도르래 돌리기 등에 쓴다.</summary>
        protected virtual void OnMoved(float delta)
        {
        }
    }
}
