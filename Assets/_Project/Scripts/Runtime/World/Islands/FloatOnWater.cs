using Letsail.Ocean;
using UnityEngine;

namespace Letsail.World.Islands
{
    /// <summary>
    /// 파도에 맞춰 위아래로 뜬다 (떠다니는 통·상자·부표, 거품 고리).
    /// 처음 놓인 높이를 해수면 기준으로 보고, 그 위치의 수면 높이만큼 올리고 내린다. 기울기도 물결을 살짝 따른다.
    /// 모든 PC 의 파도가 같으므로 각자 계산해도 같은 자리에 보인다.
    /// </summary>
    public sealed class FloatOnWater : MonoBehaviour
    {
        [SerializeField, Tooltip("수면에서 띄우는 높이 (m)")] float offset;
        [SerializeField, Range(0f, 1f), Tooltip("물결 기울기를 따르는 정도")] float tilt = 0.6f;
        [SerializeField, Tooltip("기울기를 잴 때 앞뒤로 떨어진 거리 (m)")] float sampleDistance = 0.8f;

        Vector3 basePosition;
        Quaternion baseRotation;
        bool initialized;

        public float Offset
        {
            get => offset;
            set => offset = value;
        }

        void OnEnable()
        {
            basePosition = transform.position;
            baseRotation = transform.rotation;
            initialized = true;
        }

        void LateUpdate()
        {
            var ocean = OceanSystem.Instance;
            if (ocean == null || !initialized)
            {
                return;
            }
            Vector3 p = basePosition;
            float h = ocean.GetWaterHeight(p);
            Quaternion rotation = baseRotation;
            if (tilt > 0f)
            {
                float hx = ocean.GetWaterHeight(p + Vector3.right * sampleDistance) - ocean.GetWaterHeight(p - Vector3.right * sampleDistance);
                float hz = ocean.GetWaterHeight(p + Vector3.forward * sampleDistance) - ocean.GetWaterHeight(p - Vector3.forward * sampleDistance);
                Vector3 normal = new Vector3(-hx, 2f * sampleDistance, -hz).normalized;
                rotation = Quaternion.Slerp(Quaternion.identity, Quaternion.FromToRotation(Vector3.up, normal), tilt) * baseRotation;
            }
            transform.SetPositionAndRotation(new Vector3(p.x, h + offset, p.z), rotation);
        }
    }
}
