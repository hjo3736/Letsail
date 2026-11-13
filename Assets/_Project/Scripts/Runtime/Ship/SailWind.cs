using System.Collections.Generic;
using Letsail.Ocean;
using Letsail.World;
using UnityEngine;

namespace Letsail.Ship
{
    /// <summary>
    /// 돛이 바람을 받는 모습. 돛 메시의 블렌드셰이프로 바꾼다.
    /// - 가로돛(Billow): 뒤에서 바람을 받으면 앞으로 부푼다. 옆바람이면 조금, 앞바람이면 바람이 빠져 펄럭인다.
    /// - 삼각돛(Billow_L / Billow_R): 바람이 오는 반대쪽으로 부푼다. 바람이 반대편으로 넘어가면 천천히 넘어간다. 정면 바람이면 펄럭인다.
    /// - 가로돛은 활대(Yard_)와 함께 바람 쪽으로 돌아간다 (최대 maxBrace 도, 돛대 줄에 걸리지 않는 만큼).
    /// - 바람이 셀수록 많이 부풀고, 폭풍이면 돌풍에 출렁이고 떤다.
    /// 접힌 만큼(Furled) 덜 부푼다. 펴고 접기는 SailControl 이 맡는다.
    /// 바람과 시각은 모든 PC 가 같게 계산하므로 네트워크로 따로 보내지 않는다.
    /// 배 루트에 둔다. 돛은 자식 중 블렌드셰이프가 있는 메시를 알아서 찾는다.
    /// </summary>
    [DefaultExecutionOrder(100)]
    public sealed class SailWind : MonoBehaviour
    {
        [Header("바람 세기")]
        [SerializeField, Tooltip("이 풍속(m/s) 이하면 가장 적게 부푼다")] float calmWind = 2f;
        [SerializeField, Tooltip("이 풍속(m/s) 이상이면 끝까지 부푼다")] float fullWind = 10f;
        [SerializeField, Range(0f, 1f), Tooltip("가장 약한 바람에서도 부푸는 정도")] float minFill = 0.2f;

        [Header("펄럭임 (앞바람)")]
        [SerializeField, Tooltip("펄럭일 때 부풂이 오가는 크기 (0~1)")] float luffAmount = 0.35f;
        [SerializeField, Tooltip("펄럭이는 빠르기 (초당 번)")] float luffFrequency = 3.5f;

        [Header("폭풍")]
        [SerializeField, Tooltip("폭풍 때 돌풍으로 부풂이 출렁이는 정도 (비율)")] float gustAmount = 0.3f;
        [SerializeField, Tooltip("돌풍 빠르기 (초당 번)")] float gustFrequency = 0.8f;
        [SerializeField, Tooltip("폭풍 때 돛이 떠는 크기 (0~1)")] float flutterAmount = 0.08f;
        [SerializeField, Tooltip("떠는 빠르기 (초당 번)")] float flutterFrequency = 7f;

        [Header("활대 돌리기")]
        [SerializeField, Range(0f, 45f), Tooltip("가로돛을 바람 쪽으로 돌리는 최대 각도 (도). 더 돌리면 돛대 줄에 걸린다")] float maxBrace = 25f;
        [SerializeField, Tooltip("활대가 도는 빠르기 (초당 도)")] float braceSpeed = 12f;

        [Header("반응")]
        [SerializeField, Tooltip("바람이 바뀌면 돛 모양이 따라가는 빠르기")] float response = 2.5f;
        [SerializeField, Tooltip("삼각돛이 반대편으로 넘어가는 빠르기 (초당, 0~2)")] float jibTackSpeed = 1.2f;

        struct Sail
        {
            public SkinnedMeshRenderer renderer;
            public int furled;
            public int billow;
            public int billowLeft;
            public int billowRight;
            public float phase;
            public bool IsJib => billowLeft >= 0 || billowRight >= 0;
        }

        readonly List<Sail> sails = new List<Sail>();
        // 활대와 돛을 함께 돌리는 축 (Sail_Main_Lower 처럼 Yard_ 를 자식으로 가진 돛 부모)
        readonly List<(Transform pivot, Quaternion baseRotation)> yards = new List<(Transform, Quaternion)>();
        float brace;
        float squareFill;
        float squareLuff;
        float jibFill;
        float jibLuff;
        float jibSide = 1f;
        bool hasState;

        /// <summary>가로돛이 바람을 받는 정도 (0~1). 앞바람으로 펄럭이면 작다.</summary>
        public float SquareFill => squareFill;

        /// <summary>앞바람으로 돛이 펄럭이는 정도 (0~1).</summary>
        public float Luff => squareLuff;

        /// <summary>가로돛을 돌린 각도 (도, + 는 오른쪽 끝이 뒤로).</summary>
        public float Brace => brace;

        // 돛 앞면과 바람이 이루는 cos 값 → 돛이 바람을 받는 정도 (0~1). 옆바람(0)이면 0.35.
        static float FillFor(float facing) => Mathf.Clamp01(facing + 0.35f);

        /// <summary>
        /// 지금 활대 각도에서 돛이 배를 앞으로 미는 힘 비율 (0~1).
        /// windAngleLocal 은 배 기준으로 바람이 불어 가는 각도 (0 = 뒷바람, 90 = 왼쪽에서 오는 옆바람, 180 = 앞바람).
        /// 돛이 바람을 받는 정도 × 돛을 돌린 만큼 줄어드는 앞쪽 성분.
        /// </summary>
        public float DriveFor(float windAngleLocal)
        {
            float facing = Mathf.Cos((windAngleLocal - brace) * Mathf.Deg2Rad);
            return FillFor(facing) * Mathf.Cos(brace * Mathf.Deg2Rad);
        }

        void Awake()
        {
            foreach (var r in GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                var mesh = r.sharedMesh;
                if (mesh == null) continue;
                var sail = new Sail
                {
                    renderer = r,
                    furled = mesh.GetBlendShapeIndex("Furled"),
                    billow = mesh.GetBlendShapeIndex("Billow"),
                    billowLeft = mesh.GetBlendShapeIndex("Billow_L"),
                    billowRight = mesh.GetBlendShapeIndex("Billow_R"),
                    // 돛마다 조금씩 다르게 흔들리게. 이름으로 정하므로 모든 PC 가 같다.
                    phase = (Mathf.Abs(r.name.GetHashCode()) % 1000) * 0.0137f,
                };
                if (sail.billow >= 0 || sail.IsJib) sails.Add(sail);

                // 가로돛의 부모에 활대가 함께 있으면 그 부모를 돌린다.
                Transform pivot = r.transform.parent;
                if (sail.billow >= 0 && pivot != null && pivot != transform && !yards.Exists(y => y.pivot == pivot))
                {
                    foreach (Transform child in pivot)
                    {
                        if (child.name.StartsWith("Yard_"))
                        {
                            yards.Add((pivot, pivot.localRotation));
                            break;
                        }
                    }
                }
            }
        }

        void LateUpdate()
        {
            var weather = WeatherSystem.Instance;
            Vector3 windDirection = weather != null ? weather.WindDirection : Vector3.forward;
            float windSpeed = weather != null ? weather.WindSpeedAt(transform.position) : 6f;
            float storm = weather != null ? weather.StormAt(transform.position) : 0f;
            float time = OceanSystem.Instance != null && Application.isPlaying ? OceanSystem.Instance.CurrentTime : Time.time;
            Apply(windDirection, windSpeed, storm, time, Time.deltaTime);
        }

        /// <summary>
        /// 돛 모양을 정한다. windDirection 은 바람이 불어 가는 쪽(월드). deltaTime 이 크면 바로 그 모양이 된다.
        /// </summary>
        public void Apply(Vector3 windDirection, float windSpeed, float storm, float time, float deltaTime)
        {
            // 배 기준 바람: z > 0 이면 뒤에서 앞으로, x > 0 이면 왼쪽에서 오른쪽으로 분다.
            Vector3 local = transform.InverseTransformDirection(windDirection);
            local.y = 0f;
            local = local.sqrMagnitude > 1e-6f ? local.normalized : Vector3.forward;

            float strength = Mathf.Lerp(minFill, 1f, Mathf.InverseLerp(calmWind, fullWind, windSpeed));

            // 활대: 돛 앞면이 바람이 불어 가는 쪽을 보게 돌린다 (최대 maxBrace).
            // 거의 정면 바람이면 지금 돌린 쪽 끝에 그대로 둔다 (왼쪽·오른쪽을 오가며 떨지 않게).
            float windAngle = Mathf.Atan2(local.x, local.z) * Mathf.Rad2Deg;
            float braceTarget = Mathf.Abs(windAngle) > 150f && Mathf.Abs(brace) > 1f
                ? Mathf.Sign(brace) * maxBrace
                : Mathf.Clamp(windAngle, -maxBrace, maxBrace);
            bool snap = !hasState || deltaTime > 1f;
            brace = snap ? braceTarget : Mathf.MoveTowards(brace, braceTarget, braceSpeed * deltaTime);
            foreach (var (pivot, baseRotation) in yards)
            {
                if (pivot != null) pivot.localRotation = Quaternion.AngleAxis(brace, Vector3.up) * baseRotation;
            }

            // 가로돛: 돌린 돛 앞면 기준으로 뒷바람 가득, 옆바람 조금, 앞바람이면 바람이 빠지고 펄럭인다.
            float facing = Mathf.Cos((windAngle - brace) * Mathf.Deg2Rad);
            float squareFillTarget = FillFor(facing);
            float squareLuffTarget = Mathf.Clamp01((-facing - 0.1f) / 0.4f);

            // 삼각돛: 옆바람에 가장 잘 받고, 뒷바람은 앞 돛에 가려 조금, 정면 바람(약 ±27도)이면 펄럭인다.
            float jibFillTarget = Mathf.Clamp01(Mathf.Abs(local.x) * 1.4f + Mathf.Max(0f, local.z) * 0.4f);
            float jibLuffTarget = local.z < 0f ? Mathf.Clamp01(1f - Mathf.Abs(local.x) / 0.45f) : 0f;
            // 바람이 왼쪽에서 오면(x > 0) 오른쪽으로 부푼다. 거의 정면·정후방이면 지금 쪽을 그대로 둔다.
            float sideTarget = Mathf.Abs(local.x) > 0.08f ? Mathf.Sign(local.x) : jibSide;

            if (snap)
            {
                squareFill = squareFillTarget;
                squareLuff = squareLuffTarget;
                jibFill = jibFillTarget;
                jibLuff = jibLuffTarget;
                jibSide = sideTarget;
                hasState = true;
            }
            else
            {
                float t = 1f - Mathf.Exp(-response * deltaTime);
                squareFill = Mathf.Lerp(squareFill, squareFillTarget, t);
                squareLuff = Mathf.Lerp(squareLuff, squareLuffTarget, t);
                jibFill = Mathf.Lerp(jibFill, jibFillTarget, t);
                jibLuff = Mathf.Lerp(jibLuff, jibLuffTarget, t);
                jibSide = Mathf.MoveTowards(jibSide, sideTarget, jibTackSpeed * deltaTime);
            }

            foreach (var sail in sails)
            {
                float flap = 0.5f + 0.5f * Mathf.Sin((time * luffFrequency + sail.phase) * Mathf.PI * 2f);
                flap *= Mathf.PerlinNoise(time * 1.7f + sail.phase, sail.phase);
                float gust = 1f + storm * gustAmount * (Mathf.PerlinNoise(time * gustFrequency, sail.phase * 3f) - 0.5f) * 2f;
                float flutter = storm * flutterAmount * Mathf.Sin((time * flutterFrequency + sail.phase * 2f) * Mathf.PI * 2f);

                float fill = sail.IsJib ? jibFill : squareFill;
                float luff = sail.IsJib ? jibLuff : squareLuff;
                float amount = strength * fill * (1f - luff) * gust + luff * flap * luffAmount + flutter;
                amount = Mathf.Clamp01(amount);

                float open = sail.furled >= 0 ? 1f - sail.renderer.GetBlendShapeWeight(sail.furled) / 100f : 1f;
                float weight = amount * open * 100f;
                if (sail.billow >= 0)
                {
                    sail.renderer.SetBlendShapeWeight(sail.billow, weight);
                }
                if (sail.billowLeft >= 0)
                {
                    sail.renderer.SetBlendShapeWeight(sail.billowLeft, weight * Mathf.Clamp01(-jibSide));
                }
                if (sail.billowRight >= 0)
                {
                    sail.renderer.SetBlendShapeWeight(sail.billowRight, weight * Mathf.Clamp01(jibSide));
                }
            }
        }
    }
}
