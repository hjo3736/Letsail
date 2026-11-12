using UnityEngine;

namespace Letsail.Interaction
{
    /// <summary>
    /// 화덕 불. 켜지면 블렌드셰이프 Lit 을 올려 불이 붙고, 켜져 있는 동안 Flicker 로 불꽃 끝이 일렁인다.
    /// 불빛 세기도 불 크기와 일렁임을 따라간다. Flicker 는 Lit 보다 크게 주면 모양이 깨지므로 항상 Lit 이하로 둔다.
    /// </summary>
    public sealed class FireToggleVisual : ToggleVisual
    {
        [SerializeField] SkinnedMeshRenderer fire;
        [SerializeField] string litShape = "Lit";
        [SerializeField] string flickerShape = "Flicker";
        [SerializeField, Tooltip("불이 다 붙었을 때 불빛 세기")] float lightIntensity = 2f;
        [SerializeField] Light fireLight;
        [SerializeField, Tooltip("일렁이는 빠르기")] float flickerSpeed = 6f;

        int litIndex = -1;
        int flickerIndex = -1;
        float lit;

        protected override void Awake()
        {
            var mesh = fire != null ? fire.sharedMesh : null;
            if (mesh != null)
            {
                litIndex = mesh.GetBlendShapeIndex(litShape);
                flickerIndex = mesh.GetBlendShapeIndex(flickerShape);
            }
            base.Awake();
        }

        protected override void ApplyAmount(float value)
        {
            lit = value;
            Refresh();
        }

        protected override void Update()
        {
            base.Update();
            if (lit > 0f)
            {
                Refresh();
            }
        }

        void Refresh()
        {
            float flicker = lit > 0f ? Mathf.PerlinNoise(Time.time * flickerSpeed, 0.37f) : 0f;
            if (fire != null)
            {
                if (litIndex >= 0) fire.SetBlendShapeWeight(litIndex, lit * 100f);
                if (flickerIndex >= 0) fire.SetBlendShapeWeight(flickerIndex, flicker * lit * 100f);
            }
            if (fireLight != null)
            {
                fireLight.enabled = lit > 0f;
                fireLight.intensity = lightIntensity * lit * (0.85f + 0.3f * flicker);
            }
        }
    }
}
