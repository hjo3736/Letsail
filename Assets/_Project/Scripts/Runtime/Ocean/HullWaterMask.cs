using UnityEngine;

namespace Letsail.Ocean
{
    /// <summary>
    /// 배 안쪽(선창)에 바다 수면이 보이지 않게 한다. 배의 물 가림 메시(Hull_WaterMask)에 붙인다.
    /// 마스크 텍스처는 위에서 본 칸마다 배 안쪽 공간의 (아래 높이, 위 높이)를 담고 있고,
    /// 바다 셰이더가 그 사이에 들어온 수면을 버린다. 배가 움직여도 따라가도록 매 프레임 위치를 넘긴다.
    /// 텍스처는 에디터 메뉴 Letsail > 배 물 가림 다시 굽기 로 만든다 (모델이 바뀌면 다시 굽는다).
    /// 지금은 한 번에 한 척만 가린다.
    /// </summary>
    [ExecuteAlways]
    public sealed class HullWaterMask : MonoBehaviour
    {
        static readonly int MaskId = Shader.PropertyToID("_LetsailHullMask");
        static readonly int WorldToMaskId = Shader.PropertyToID("_LetsailHullWorldToMask");
        static readonly int EnabledId = Shader.PropertyToID("_LetsailHullMaskEnabled");

        [SerializeField, Tooltip("구운 마스크 (R = 아래 높이, G = 위 높이, 이 오브젝트 기준)")] Texture2D mask;
        [SerializeField, Tooltip("마스크가 덮는 범위의 최소 x, z (이 오브젝트 기준)")] Vector2 boundsMin;
        [SerializeField, Tooltip("마스크가 덮는 범위의 크기 x, z (이 오브젝트 기준)")] Vector2 boundsSize = Vector2.one;

        static HullWaterMask current;

        public Texture2D Mask => mask;

        /// <summary>
        /// 위치가 배 안쪽 공간(선창 등, 바다가 들어오지 않는 곳)인지. 셰이더와 같은 마스크로 판단한다.
        /// 선창은 수면보다 낮으므로, 물속인지 볼 때 이것으로 빼야 한다.
        /// </summary>
        public static bool IsInsideHull(Vector3 worldPosition)
        {
            var m = current;
            if (m == null || m.mask == null || !m.mask.isReadable || m.boundsSize.x <= 0f || m.boundsSize.y <= 0f)
            {
                return false;
            }
            Vector3 local = m.transform.InverseTransformPoint(worldPosition);
            float u = (local.x - m.boundsMin.x) / m.boundsSize.x;
            float v = (local.z - m.boundsMin.y) / m.boundsSize.y;
            if (u < 0f || u > 1f || v < 0f || v > 1f)
            {
                return false;
            }
            Color span = m.mask.GetPixel(Mathf.Min((int)(u * m.mask.width), m.mask.width - 1), Mathf.Min((int)(v * m.mask.height), m.mask.height - 1));
            return local.y > span.r && local.y < span.g;
        }

        /// <summary>구운 결과를 넣는다 (에디터 굽기 도구용).</summary>
        public void SetBakedData(Texture2D texture, Vector2 min, Vector2 size)
        {
            mask = texture;
            boundsMin = min;
            boundsSize = size;
        }

        void OnEnable()
        {
            current = this;
        }

        void OnDisable()
        {
            if (current == this)
            {
                current = null;
                Shader.SetGlobalFloat(EnabledId, 0f);
            }
        }

        void LateUpdate()
        {
            if (current != this)
            {
                return;
            }
            if (mask == null || boundsSize.x <= 0f || boundsSize.y <= 0f)
            {
                Shader.SetGlobalFloat(EnabledId, 0f);
                return;
            }
            // 이 오브젝트 기준 (x, y, z) → (u, y, v)
            var toUv = Matrix4x4.identity;
            toUv.m00 = 1f / boundsSize.x;
            toUv.m03 = -boundsMin.x / boundsSize.x;
            toUv.m22 = 1f / boundsSize.y;
            toUv.m23 = -boundsMin.y / boundsSize.y;
            Shader.SetGlobalTexture(MaskId, mask);
            Shader.SetGlobalMatrix(WorldToMaskId, toUv * transform.worldToLocalMatrix);
            Shader.SetGlobalFloat(EnabledId, 1f);
        }
    }
}
