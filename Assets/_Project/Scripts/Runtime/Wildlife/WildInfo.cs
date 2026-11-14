using UnityEngine;

namespace Letsail.Wildlife
{
    /// <summary>
    /// 동물의 성질. Blender 에서 FBX 루트에 넣은 wild_* 속성을 임포트할 때 옮겨 담는다 (WildlifeAssetPostprocessor).
    /// 속도는 m/s, 거리는 m, 깊이는 해수면 기준 (음수 = 수면 아래).
    /// </summary>
    public sealed class WildInfo : MonoBehaviour
    {
        [Tooltip("beach, jungle, volcanic, sea")] public string biome;
        [Tooltip("ground, ground_water, ground_tree, air, air_perch, air_cave, water_reef, water_open, water_surface, water_deep")] public string habitat;
        [Tooltip("walk, walk_sideways, crawl, hop, slither, scurry, fly, swim, swim_school, swim_circle, drift")] public string move;
        [Tooltip("small, medium, large, huge")] public string size;
        [Tooltip("들어 있는 클립 (쉼표 구분)")] public string clips;
        [Tooltip("물고기 무리 크기 (예: 5-12)")] public string schoolSize;

        public float speed = 1f;
        public float runSpeed;
        public float swimSpeed;
        [Tooltip("선원이 이 거리 안에 오면 도망 (0 = 안 감)")] public float fleeRadius;
        [Tooltip("이 거리 안의 선원을 공격 (0 = 안 함). 아직 공격은 없다")] public float aggroRadius;
        public float depthMin;
        public float depthMax;
        public float shipAvoidRadius;
        public bool followShip;
        public bool damageTouch;
        public bool rideableHint;
        public bool aggressive = true;
        [Tooltip("실존하지 않는 판타지 생물")] public bool fantasy;

        /// <summary>클립 목록에 이 동작이 있는지 (예: "Run").</summary>
        public bool HasClip(string action)
        {
            if (string.IsNullOrEmpty(clips)) return false;
            foreach (var c in clips.Split(','))
            {
                if (c.Trim() == action) return true;
            }
            return false;
        }

        public bool IsWater => habitat != null && habitat.StartsWith("water");
        public bool IsAir => habitat == "air" || habitat == "air_perch" || habitat == "air_cave" || habitat == "ground_tree";
        public bool IsSchool => move == "swim_school";

        public int SizeRank => size switch { "small" => 0, "medium" => 1, "large" => 2, "huge" => 3, _ => 0 };

        public void CopyFrom(WildInfo o)
        {
            biome = o.biome; habitat = o.habitat; move = o.move; size = o.size; clips = o.clips; schoolSize = o.schoolSize;
            speed = o.speed; runSpeed = o.runSpeed; swimSpeed = o.swimSpeed; fleeRadius = o.fleeRadius; aggroRadius = o.aggroRadius;
            depthMin = o.depthMin; depthMax = o.depthMax; shipAvoidRadius = o.shipAvoidRadius;
            fantasy = o.fantasy; followShip = o.followShip; damageTouch = o.damageTouch; rideableHint = o.rideableHint; aggressive = o.aggressive;
        }
    }
}
