using UnityEngine;
using UnityEngine.InputSystem;

namespace Letsail.Interaction
{
    /// <summary>
    /// 돛대 하나의 돛들. W·S 로 펴고 접는다. 값은 0(다 접음) ~ 1(다 폄)이고, 나중에 배의 속도에 쓴다.
    /// 모습은 돛 메시의 블렌드셰이프 Furled(0 = 폄, 100 = 활대 위로 감아 올림)로 바꾼다.
    /// 성한 돛과 찢어진 돛(_Intact, _Damaged)에 같은 값을 넣는다.
    /// </summary>
    public sealed class SailControl : SeatControl
    {
        [SerializeField, Tooltip("이 돛대의 돛 메시들 (_Intact, _Damaged 모두)")] SkinnedMeshRenderer[] sails;
        [SerializeField] string furledShape = "Furled";
        [SerializeField, Tooltip("W·S 를 누르고 있을 때 값이 바뀌는 속도 (초당)")] float rate = 0.5f;

        InputAction moveAction;
        float lastInput;
        int[] shapeIndices;

        public override string ControlsHint => "W · S  돛 펴기 · 접기";

        // S(접기)는 줄을 당기고, W(펴기)는 줄을 풀어 준다.
        public override string LoopAction => lastInput < -0.1f ? "Sail_Pull" : lastInput > 0.1f ? "Sail_Release" : null;

        protected override void Initialize()
        {
            moveAction = InputSystem.actions.FindAction("Player/Move", true);
            shapeIndices = new int[sails != null ? sails.Length : 0];
            for (int i = 0; i < shapeIndices.Length; i++)
            {
                var mesh = sails[i] != null ? sails[i].sharedMesh : null;
                shapeIndices[i] = mesh != null ? mesh.GetBlendShapeIndex(furledShape) : -1;
            }
        }

        protected override float OnTick(float value, float deltaTime)
        {
            float input = moveAction.ReadValue<Vector2>().y;
            lastInput = input;
            return Mathf.Clamp01(value + input * rate * deltaTime);
        }

        protected override void OnApply(float value)
        {
            float furled = (1f - value) * 100f;
            for (int i = 0; i < shapeIndices.Length; i++)
            {
                if (sails[i] != null && shapeIndices[i] >= 0)
                {
                    sails[i].SetBlendShapeWeight(shapeIndices[i], furled);
                }
            }
        }
    }
}
