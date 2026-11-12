using UnityEngine;

namespace Letsail.Interaction
{
    /// <summary>
    /// 켜지면 블렌드셰이프를 0 → 100 으로 올린다 (서랍 열기, 줄사다리 내리기 등).
    /// 모양을 따라 움직여야 하는 충돌체가 있으면 같은 비율로 옮긴다 (열린 서랍에 부딪히게).
    /// </summary>
    public sealed class BlendShapeToggleVisual : ToggleVisual
    {
        [SerializeField] SkinnedMeshRenderer[] renderers;
        [SerializeField, Tooltip("블렌드셰이프 이름 (예: Open, Lowered)")] string shapeName = "Open";
        [SerializeField, Tooltip("모양을 따라 옮길 충돌체 (없어도 된다)")] BoxCollider movingCollider;
        [SerializeField, Tooltip("켜졌을 때 충돌체 중심을 옮기는 거리 (충돌체 기준)")] Vector3 colliderOnOffset;

        int[] shapeIndices;
        Vector3 colliderBaseCenter;

        protected override void Awake()
        {
            shapeIndices = new int[renderers.Length];
            for (int i = 0; i < renderers.Length; i++)
            {
                var mesh = renderers[i] != null ? renderers[i].sharedMesh : null;
                shapeIndices[i] = mesh != null ? mesh.GetBlendShapeIndex(shapeName) : -1;
            }
            if (movingCollider != null)
            {
                colliderBaseCenter = movingCollider.center;
            }
            base.Awake();
        }

        protected override void ApplyAmount(float value)
        {
            float t = Mathf.SmoothStep(0f, 1f, value);
            for (int i = 0; i < renderers.Length; i++)
            {
                if (renderers[i] != null && shapeIndices[i] >= 0)
                {
                    renderers[i].SetBlendShapeWeight(shapeIndices[i], t * 100f);
                }
            }
            if (movingCollider != null)
            {
                movingCollider.center = colliderBaseCenter + colliderOnOffset * t;
            }
        }
    }
}
