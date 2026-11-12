using UnityEngine;

namespace Letsail.Interaction
{
    /// <summary>
    /// 닻. 켜지면(내리기) 닻이 줄을 따라 물속 목표 지점까지 내려가고, 꺼지면 다시 감겨 올라온다.
    /// 줄은 길이 1m 메시를 세로로 늘려서 표현하고, 움직이는 동안 감는 도르래가 돈다.
    /// </summary>
    public sealed class AnchorToggleVisual : ToggleVisual
    {
        [SerializeField] Transform anchorMesh;
        [SerializeField, Tooltip("세로 길이 1m 짜리 줄 메시. y 크기로 길이를 바꾼다")] Transform rope;
        [SerializeField, Tooltip("닻이 내려갈 지점")] Transform dropTarget;
        [SerializeField, Tooltip("움직이는 동안 도는 도르래")] Transform drum;
        [SerializeField, Tooltip("도르래가 도는 축 (도르래 기준)")] Vector3 drumAxis = Vector3.right;
        [SerializeField, Tooltip("닻이 1m 움직일 때 도르래가 도는 각도")] float drumDegreesPerMeter = 120f;

        Vector3 raisedMeshPosition;
        float raisedRopeLength;
        float dropDistance;

        protected override void Awake()
        {
            raisedMeshPosition = anchorMesh.localPosition;
            raisedRopeLength = rope.localScale.y;
            dropDistance = dropTarget != null ? -dropTarget.localPosition.y : 5f;
            base.Awake();
        }

        protected override void ApplyAmount(float value)
        {
            float drop = dropDistance * value;
            anchorMesh.localPosition = raisedMeshPosition + Vector3.down * drop;
            Vector3 scale = rope.localScale;
            scale.y = raisedRopeLength + drop;
            rope.localScale = scale;
        }

        protected override void OnMoved(float delta)
        {
            if (drum != null)
            {
                drum.Rotate(drumAxis, delta * dropDistance * drumDegreesPerMeter, Space.Self);
            }
        }
    }
}
