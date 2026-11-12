using Letsail.Core;
using Letsail.Interaction;
using Letsail.Ocean;
using Letsail.World;
using Unity.Netcode;
using UnityEngine;

namespace Letsail.Ship
{
    /// <summary>
    /// 배를 물에 띄우고 움직인다.
    /// - 부력: 뱃머리·고물·좌현·우현·가운데의 수면 높이를 재서, 오르내림과 앞뒤·좌우 기울기를 부드럽게 따라간다.
    ///   물리로 계산하지 않고 파도 모양을 따라가므로 뒤집히거나 튀지 않는다.
    /// - 항해: 펼친 돛 × 바람 세기(바람을 등질수록 빠르다)로 속도를 내고, 키(방향타)로 돈다. 닻을 내리면 선다.
    /// 호스트(서버)가 계산하고, 위치는 같은 오브젝트의 NetworkTransform 이 다른 선원에게 보낸다.
    /// 정박 중(docked)이면 제자리에서 흔들리기만 한다 (로비).
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public sealed class ShipMotion : NetworkBehaviour
    {
        [Header("부력")]
        [SerializeField, Tooltip("수면 높이를 재는 곳 (배 기준): 뱃머리, 고물, 좌현, 우현")]
        Vector4 hullExtents = new Vector4(8.6f, -7.2f, -2.5f, 2.5f);
        [SerializeField, Tooltip("파도에 맞춰 기우는 정도 (1 = 수면 기울기 그대로)")] float tiltAmount = 0.8f;
        [SerializeField, Tooltip("최대로 기우는 각도 (도)")] float maxTilt = 18f;
        [SerializeField, Tooltip("오르내림이 따라가는 시간 (초). 클수록 무겁게 움직인다")] float heaveTime = 0.6f;
        [SerializeField, Tooltip("기울기가 따라가는 시간 (초)")] float tiltTime = 0.9f;
        [SerializeField, Tooltip("물에 잠기는 깊이 (m). 배 원점이 흘수선이면 0")] float draft;

        [Header("항해")]
        [SerializeField, Tooltip("정박 중이면 움직이지 않고 흔들리기만 한다")] bool docked;
        [SerializeField, Tooltip("키 (HelmControl 이 있는 상호작용)")] Interactable helm;
        [SerializeField, Tooltip("돛 (SailControl 이 있는 상호작용들)")] Interactable[] sails;
        [SerializeField, Tooltip("닻 (켜지면 내린 것)")] ToggleInteraction anchor;
        [SerializeField, Tooltip("바람 1m/s 당 최고 속도 (m/s), 돛을 다 펴고 바람을 등졌을 때")] float speedPerWind = 0.45f;
        [SerializeField, Tooltip("가장 빠른 속도 (m/s)")] float maxSpeed = 6f;
        [SerializeField, Tooltip("바람을 정면으로 받을 때도 남는 힘 비율")] float upwindEfficiency = 0.3f;
        [SerializeField, Tooltip("빨라지는 정도 (m/s²)")] float acceleration = 0.35f;
        [SerializeField, Tooltip("느려지는 정도 (m/s²). 닻을 내리면 훨씬 빨리 선다")] float deceleration = 0.5f;
        [SerializeField, Tooltip("키를 끝까지 돌렸을 때 도는 속도 (도/초), 충분히 빠를 때")] float maxTurnRate = 10f;
        [SerializeField, Tooltip("이 속도(m/s)가 되어야 키가 제대로 먹는다")] float fullSteerSpeed = 2.5f;
        [SerializeField, Tooltip("멈춰 있을 때도 키로 돌 수 있는 비율")] float minSteer = 0.15f;

        Rigidbody body;
        InteractionHub hub;
        int helmIndex = -1;
        int[] sailIndices;
        float heading;
        float heave;
        float heaveVelocity;
        float pitch;
        float pitchVelocity;
        float roll;
        float rollVelocity;
        bool initialized;

        static readonly System.Collections.Generic.List<ShipMotion> ships = new System.Collections.Generic.List<ShipMotion>();

        [SerializeField, Tooltip("이 상자 안(배 기준)에 있으면 배에 탄 것으로 본다 (최소, 최대)")]
        Bounds aboardBounds = new Bounds(new Vector3(0f, 6.5f, 1f), new Vector3(7.2f, 19f, 21f));

        /// <summary>위치가 어느 배 위(갑판, 선창, 돛대 포함)인지. 없으면 null.</summary>
        public static ShipMotion FindShipUnder(Vector3 worldPosition)
        {
            foreach (var ship in ships)
            {
                if (ship != null && ship.aboardBounds.Contains(ship.transform.InverseTransformPoint(worldPosition)))
                {
                    return ship;
                }
            }
            return null;
        }

        void OnEnable()
        {
            ships.Add(this);
        }

        void OnDisable()
        {
            ships.Remove(this);
        }

        /// <summary>지금 속도 (m/s, 앞쪽).</summary>
        public float Speed { get; private set; }

        bool Simulates => !IsSpawned || IsServer;

        void Awake()
        {
            body = GetComponent<Rigidbody>();
            hub = GetComponent<InteractionHub>();
        }

        void Start()
        {
            if (hub != null)
            {
                helmIndex = helm != null ? hub.IndexOf(helm) : -1;
                sailIndices = new int[sails != null ? sails.Length : 0];
                for (int i = 0; i < sailIndices.Length; i++)
                {
                    sailIndices[i] = sails[i] != null ? hub.IndexOf(sails[i]) : -1;
                }
            }
        }

        void FixedUpdate()
        {
            if (!Simulates || OceanSystem.Instance == null)
            {
                return;
            }
            float dt = Time.fixedDeltaTime;
            if (!initialized)
            {
                initialized = true;
                heading = transform.eulerAngles.y;
                heave = transform.position.y;
            }

            if (!docked)
            {
                UpdateSailing(dt);
            }

            // 지금 방향에서 배의 앞·옆 (수평)
            Quaternion yaw = Quaternion.Euler(0f, heading, 0f);
            Vector3 forward = yaw * Vector3.forward;
            Vector3 right = yaw * Vector3.right;
            Vector3 center = body.position;
            if (!docked)
            {
                center += forward * (Speed * dt);
            }

            var ocean = OceanSystem.Instance;
            float bow = ocean.GetWaterHeight(center + forward * hullExtents.x);
            float stern = ocean.GetWaterHeight(center + forward * hullExtents.y);
            float port = ocean.GetWaterHeight(center + right * hullExtents.z);
            float starboard = ocean.GetWaterHeight(center + right * hullExtents.w);
            float middle = ocean.GetWaterHeight(center);

            float targetHeave = (bow + stern + port + starboard + middle) / 5f - draft;
            // 뱃머리 쪽 수면이 높으면 앞이 들리고 (Unity 에서는 -X 회전), 우현 쪽 수면이 높으면 우현이 들린다 (+Z 회전).
            float targetPitch = Mathf.Clamp(-Mathf.Atan2(bow - stern, hullExtents.x - hullExtents.y) * Mathf.Rad2Deg * tiltAmount, -maxTilt, maxTilt);
            float targetRoll = Mathf.Clamp(Mathf.Atan2(starboard - port, hullExtents.w - hullExtents.z) * Mathf.Rad2Deg * tiltAmount, -maxTilt, maxTilt);

            heave = Mathf.SmoothDamp(heave, targetHeave, ref heaveVelocity, heaveTime, Mathf.Infinity, dt);
            pitch = Mathf.SmoothDamp(pitch, targetPitch, ref pitchVelocity, tiltTime, Mathf.Infinity, dt);
            roll = Mathf.SmoothDamp(roll, targetRoll, ref rollVelocity, tiltTime, Mathf.Infinity, dt);

            body.MovePosition(new Vector3(center.x, heave, center.z));
            body.MoveRotation(yaw * Quaternion.Euler(pitch, 0f, roll));
        }

        void UpdateSailing(float dt)
        {
            float sail = 0f;
            int count = 0;
            if (hub != null && sailIndices != null)
            {
                foreach (int i in sailIndices)
                {
                    if (i >= 0)
                    {
                        sail += hub.GetValue(i);
                        count++;
                    }
                }
            }
            sail = count > 0 ? sail / count : 0f;
            float rudder = hub != null && helmIndex >= 0 ? hub.GetValue(helmIndex) : 0f;
            bool anchored = anchor != null && anchor.IsOn;

            var weather = WeatherSystem.Instance;
            float windSpeed = weather != null ? weather.WindSpeed : 5f;
            Vector3 windDir = weather != null ? weather.WindDirection : Vector3.forward;
            Vector3 forward = Quaternion.Euler(0f, heading, 0f) * Vector3.forward;
            // 바람을 등지면(바람과 같은 쪽으로 가면) 가장 빠르고, 정면으로 받으면 힘이 조금만 남는다.
            float downwind = Mathf.Clamp01(Vector3.Dot(forward, windDir) * 0.5f + 0.5f);
            float efficiency = Mathf.Lerp(upwindEfficiency, 1f, downwind);
            float targetSpeed = anchored ? 0f : Mathf.Min(maxSpeed, sail * windSpeed * speedPerWind * efficiency);

            float rate = targetSpeed > Speed ? acceleration : (anchored ? deceleration * 4f : deceleration);
            Speed = Mathf.MoveTowards(Speed, targetSpeed, rate * dt);

            // 방향타는 물이 흘러야 먹는다. 닻을 내리면 돌지 않는다.
            if (!anchored)
            {
                float steer = Mathf.Lerp(minSteer, 1f, Mathf.Clamp01(Speed / fullSteerSpeed));
                heading = Mathf.Repeat(heading + rudder * maxTurnRate * steer * dt, 360f);
            }
        }
    }
}
