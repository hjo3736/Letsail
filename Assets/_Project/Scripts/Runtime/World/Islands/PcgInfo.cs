using UnityEngine;

namespace Letsail.World.Islands
{
    /// <summary>
    /// 환경 소품의 흩뿌리기 규칙. Blender 에서 FBX 루트에 넣은 pcg_* 속성을 임포트할 때 옮겨 담는다.
    /// 높이는 해수면 기준(m), 경사는 도.
    /// </summary>
    public sealed class PcgInfo : MonoBehaviour
    {
        [Tooltip("beach, jungle, volcanic, sea, common")] public string biome;
        [Tooltip("tree, bush, plant, detail, rock, log, cliff, hazard, sea_rock, reef, floating, landmark")] public string category;
        [Tooltip("none, box, capsule, mesh"), UnityEngine.Serialization.FormerlySerializedAs("collider")] public string colliderType;
        [Tooltip("차지하는 반경 (m). 같은 종류끼리 이 거리 안에 겹치지 않는다")] public float radius = 1f;
        public float slopeMax = 30f;
        public float heightMin;
        public float heightMax = 10f;
        public float scaleMin = 1f;
        public float scaleMax = 1f;
        [Tooltip("지형 법선에 맞춰 기울이기")] public bool alignNormal;

        public void CopyFrom(PcgInfo other)
        {
            biome = other.biome;
            category = other.category;
            colliderType = other.colliderType;
            radius = other.radius;
            slopeMax = other.slopeMax;
            heightMin = other.heightMin;
            heightMax = other.heightMax;
            scaleMin = other.scaleMin;
            scaleMax = other.scaleMax;
            alignNormal = other.alignNormal;
        }

        /// <summary>원점이 해수면에 있는 소품 (바다 바위, 떠다니는 것, 난파선).</summary>
        public bool SitsOnWater => biome == "sea" && category != "reef";

        /// <summary>해저 바닥에 놓는 소품 (산호, 켈프).</summary>
        public bool SitsOnSeabed => category == "reef";
    }
}
