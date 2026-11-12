using UnityEngine;

namespace Letsail.Interaction
{
    /// <summary>켜지면 대상을 경첩 축으로 돌린다 (문 열기, 상자 뚜껑 열기 등). 대상의 원점이 경첩이어야 한다.</summary>
    public sealed class RotateToggleVisual : ToggleVisual
    {
        [SerializeField, Tooltip("돌릴 대상 (원점이 경첩)")] Transform target;
        [SerializeField, Tooltip("도는 축 (대상 기준)")] Vector3 axis = Vector3.up;
        [SerializeField, Tooltip("켜졌을 때 돌아가는 각도 (도)")] float openAngle = 90f;

        Quaternion closedRotation;

        protected override void Awake()
        {
            closedRotation = target != null ? target.localRotation : Quaternion.identity;
            base.Awake();
        }

        protected override void ApplyAmount(float value)
        {
            if (target != null)
            {
                target.localRotation = closedRotation * Quaternion.AngleAxis(openAngle * Mathf.SmoothStep(0f, 1f, value), axis);
            }
        }
    }
}
