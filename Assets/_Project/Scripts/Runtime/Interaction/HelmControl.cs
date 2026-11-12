using UnityEngine;
using UnityEngine.InputSystem;

namespace Letsail.Interaction
{
    /// <summary>
    /// 키(조타륜). A·D 로 돌린다. 값은 -1(왼쪽 끝) ~ 1(오른쪽 끝)이고, 나중에 배의 방향 전환에 쓴다.
    /// 바퀴와 함께 배 뒤쪽 방향타도 돌아간다.
    /// </summary>
    public sealed class HelmControl : SeatControl
    {
        [SerializeField] Transform wheel;
        [SerializeField, Tooltip("바퀴가 도는 축 (바퀴 기준)")] Vector3 wheelAxis = Vector3.forward;
        [SerializeField, Tooltip("끝에서 끝까지 돌렸을 때 바퀴 회전 (도, 한쪽)")] float maxWheelDegrees = 540f;
        [SerializeField] Transform rudder;
        [SerializeField, Tooltip("방향타가 도는 축 (방향타 기준)")] Vector3 rudderAxis = Vector3.up;
        [SerializeField, Tooltip("끝까지 돌렸을 때 방향타 각도 (도, 한쪽)")] float maxRudderDegrees = 30f;
        [SerializeField, Tooltip("A·D 를 누르고 있을 때 값이 바뀌는 속도 (초당)")] float turnRate = 0.6f;

        InputAction moveAction;
        float lastInput;
        Quaternion wheelBase;
        Quaternion rudderBase;

        public override string ControlsHint => "A · D  키 돌리기";

        public override string LoopAction => lastInput < -0.1f ? "Helm_TurnLeft" : lastInput > 0.1f ? "Helm_TurnRight" : null;

        protected override void Initialize()
        {
            moveAction = InputSystem.actions.FindAction("Player/Move", true);
            wheelBase = wheel != null ? wheel.localRotation : Quaternion.identity;
            rudderBase = rudder != null ? rudder.localRotation : Quaternion.identity;
        }

        protected override float OnTick(float value, float deltaTime)
        {
            float input = moveAction.ReadValue<Vector2>().x;
            lastInput = input;
            return Mathf.Clamp(value + input * turnRate * deltaTime, -1f, 1f);
        }

        protected override void OnApply(float value)
        {
            if (wheel != null)
            {
                // 오른쪽(D)으로 돌리면 뒤에서 볼 때 바퀴가 시계 방향으로 돈다.
                wheel.localRotation = wheelBase * Quaternion.AngleAxis(-value * maxWheelDegrees, wheelAxis);
            }
            if (rudder != null)
            {
                // 오른쪽으로 돌면 방향타 뒤끝이 오른쪽(+X)으로 간다.
                rudder.localRotation = rudderBase * Quaternion.AngleAxis(-value * maxRudderDegrees, rudderAxis);
            }
        }
    }
}
