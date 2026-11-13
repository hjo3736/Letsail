using Letsail.Ocean;
using UnityEngine;
using UnityEngine.Rendering;

namespace Letsail.World
{
    /// <summary>
    /// 낮과 밤, 그리고 지금 있는 곳의 날씨를 하늘·빛·안개로 보여 준다.
    /// - 시각은 서버 시간으로 계산하므로 모든 선원이 같은 시각을 본다 (dayLengthMinutes 분에 하루).
    /// - 해가 지면 같은 방향광이 달빛(푸르고 어둡게)이 된다. 하늘 밝기·색, 환경광(위·옆·아래 3색), 안개 색이 시각을 따라간다.
    /// - 카메라가 있는 곳이 거칠수록 어두워지고, 안개가 짙어지고, 비가 내리고, 폭풍에서는 번개가 친다 (번개는 각자 화면에서만).
    /// 씬에 하나 둔다. 화면에 보이는 것만 바꾸므로 네트워크 오브젝트가 아니다.
    /// </summary>
    [DefaultExecutionOrder(50)]
    public sealed class SkyController : MonoBehaviour
    {
        [Header("시각")]
        [SerializeField, Tooltip("시간이 흐른다. 끄면 fixedHour 에 멈춘다 (로비)")] bool cycle = true;
        [SerializeField, Tooltip("하루 길이 (분)")] float dayLengthMinutes = 20f;
        [SerializeField, Range(0f, 24f), Tooltip("판이 시작할 때 시각")] float startHour = 8f;
        [SerializeField, Range(0f, 24f), Tooltip("시간이 흐르지 않을 때 시각")] float fixedHour = 10f;
        [SerializeField, Tooltip("해가 뜨고 지는 방위 (도)")] float sunAzimuth = -30f;

        [Header("빛")]
        [SerializeField, Tooltip("해(밤에는 달)로 쓰는 방향광. 비우면 씬의 첫 방향광")] Light sun;
        [SerializeField] Color dayColor = new Color(1f, 0.957f, 0.878f);
        [SerializeField] Color duskColor = new Color(1f, 0.62f, 0.38f);
        [SerializeField] Color moonColor = new Color(0.55f, 0.65f, 0.95f);
        [SerializeField] float dayIntensity = 1.15f;
        [SerializeField] float moonIntensity = 0.28f;

        [Header("환경광 (위, 옆, 아래)")]
        [SerializeField] Color daySky = new Color(0.6f, 0.69f, 0.77f);
        [SerializeField] Color dayEquator = new Color(0.65f, 0.71f, 0.75f);
        [SerializeField] Color dayGround = new Color(0.43f, 0.4f, 0.35f);
        [SerializeField] Color duskEquator = new Color(0.75f, 0.52f, 0.45f);
        [SerializeField] Color nightSky = new Color(0.07f, 0.09f, 0.17f);
        [SerializeField] Color nightEquator = new Color(0.06f, 0.08f, 0.13f);
        [SerializeField] Color nightGround = new Color(0.03f, 0.03f, 0.04f);

        [Header("하늘과 안개")]
        [SerializeField] Color dayFog = new Color(0.81f, 0.89f, 0.93f);
        [SerializeField] Color duskFog = new Color(0.86f, 0.66f, 0.56f);
        [SerializeField] Color nightFog = new Color(0.05f, 0.07f, 0.12f);
        [SerializeField] Color stormFog = new Color(0.42f, 0.47f, 0.52f);
        [SerializeField, Tooltip("맑을 때 안개 (시작, 끝, m)")] Vector2 clearFogRange = new Vector2(300f, 1500f);
        [SerializeField, Tooltip("폭풍일 때 안개 (시작, 끝, m)")] Vector2 stormFogRange = new Vector2(30f, 320f);
        [SerializeField] float daySkyExposure = 1.3f;
        [SerializeField] float nightSkyExposure = 0.12f;

        [Header("날씨")]
        [SerializeField, Tooltip("날씨가 화면에 반영되는 빠르기 (1/초)")] float weatherResponse = 0.5f;
        [SerializeField, Tooltip("비 (파티클) 머티리얼. 비우면 비 없음")] Material rainMaterial;
        [SerializeField, Tooltip("가장 센 비의 초당 빗방울 수")] float maxRain = 2500f;
        [SerializeField, Tooltip("이 거칠기부터 번개가 친다 (0~1)")] float lightningFrom = 0.75f;

        public static SkyController Instance { get; private set; }

        /// <summary>지금 시각 (0~24).</summary>
        public float Hour { get; private set; }

        /// <summary>밝기 (0 = 밤, 1 = 낮, 날씨로 어두워진 것 포함). 하늘이 없는 씬에서는 1.</summary>
        public static float Daylight { get; private set; } = 1f;

        /// <summary>해가 진 뒤.</summary>
        public bool IsNight => sunHeight < 0f;

        /// <summary>카메라가 있는 곳의 거칠기 (0~1, 부드럽게 따라감).</summary>
        public float Storm { get; private set; }

        /// <summary>정해진 서버 시각의 시각(0~24). 여러 PC 가 같은 값을 얻는지 검사할 때 쓴다.</summary>
        public float HourAt(double serverTime) => cycle ? Mathf.Repeat(startHour + (float)(serverTime / (dayLengthMinutes * 60.0)) * 24f, 24f) : fixedHour;

        Material skybox;
        float sunHeight;
        ParticleSystem rain;
        float nextLightning;
        float lightningTime = -1f;

        void Awake()
        {
            Instance = this;
            if (sun == null)
            {
                foreach (var light in FindObjectsByType<Light>(FindObjectsSortMode.None))
                {
                    if (light.type == LightType.Directional)
                    {
                        sun = light;
                        break;
                    }
                }
            }
            if (RenderSettings.skybox != null)
            {
                skybox = new Material(RenderSettings.skybox) { name = "Sky (runtime)" };
                RenderSettings.skybox = skybox;
            }
            RenderSettings.sun = sun;
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
        }

        void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
                Daylight = 1f;
            }
            if (skybox != null) Destroy(skybox);
        }

        void LateUpdate()
        {
            var cam = Camera.main;
            Hour = cycle ? Mathf.Repeat(startHour + (float)(OceanSystem.TimeSource() / (dayLengthMinutes * 60.0)) * 24f, 24f) : fixedHour;

            // 날씨 (카메라 자리)
            float stormTarget = 0f;
            var weather = WeatherSystem.Instance;
            if (weather != null && cam != null)
            {
                stormTarget = weather.StormAt(cam.transform.position);
            }
            Storm = Mathf.MoveTowards(Storm, stormTarget, weatherResponse * Time.deltaTime);
            float s = Storm;

            // 해: 6시에 동쪽 지평선, 12시 꼭대기, 18시 서쪽 지평선
            float sunAngle = (Hour - 6f) / 24f * 360f;
            sunHeight = Mathf.Sin(sunAngle * Mathf.Deg2Rad);
            float day = Smooth(-0.08f, 0.25f, sunHeight);          // 0 밤 → 1 낮
            float golden = 1f - Smooth(0.05f, 0.4f, sunHeight);   // 해가 낮을수록 노을빛

            if (sun != null)
            {
                bool moon = sunHeight < 0f;
                float angle = moon ? sunAngle + 180f : sunAngle;
                sun.transform.rotation = Quaternion.Euler(angle, sunAzimuth, 0f);
                float intensity = moon
                    ? moonIntensity * Smooth(0.02f, 0.2f, -sunHeight)
                    : dayIntensity * Smooth(-0.02f, 0.18f, sunHeight);
                sun.color = moon ? moonColor : Color.Lerp(dayColor, duskColor, golden);
                sun.intensity = intensity * (1f - 0.6f * s) + Lightning();
                sun.shadowStrength = Mathf.Lerp(0.6f, 0.25f, s);
            }

            float dim = 1f - 0.4f * s;
            Daylight = Mathf.Lerp(0.15f, 1f, day) * dim;
            RenderSettings.ambientSkyColor = Color.Lerp(nightSky, daySky, day) * dim;
            RenderSettings.ambientEquatorColor = Color.Lerp(nightEquator, Color.Lerp(dayEquator, duskEquator, golden * day), day) * dim;
            RenderSettings.ambientGroundColor = Color.Lerp(nightGround, dayGround, day) * dim;

            Color fog = Color.Lerp(nightFog, Color.Lerp(dayFog, duskFog, golden), day);
            fog = Color.Lerp(fog, stormFog * Mathf.Lerp(0.25f, 1f, day), s * 0.85f);
            RenderSettings.fogColor = fog;
            RenderSettings.fogStartDistance = Mathf.Lerp(clearFogRange.x, stormFogRange.x, s);
            RenderSettings.fogEndDistance = Mathf.Lerp(clearFogRange.y, stormFogRange.y, s);

            if (skybox != null)
            {
                if (skybox.HasProperty("_Exposure")) skybox.SetFloat("_Exposure", Mathf.Lerp(nightSkyExposure, daySkyExposure, day) * (1f - 0.55f * s));
                if (skybox.HasProperty("_AtmosphereThickness")) skybox.SetFloat("_AtmosphereThickness", Mathf.Lerp(1f, 0.7f, s));
                if (skybox.HasProperty("_SkyTint")) skybox.SetColor("_SkyTint", Color.Lerp(new Color(0.5f, 0.5f, 0.5f), new Color(0.36f, 0.37f, 0.39f), s));
                if (skybox.HasProperty("_GroundColor")) skybox.SetColor("_GroundColor", Color.Lerp(new Color(0.37f, 0.35f, 0.34f), new Color(0.3f, 0.32f, 0.34f), s));
            }

            UpdateRain(cam, s);
        }

        float Lightning()
        {
            if (Storm < lightningFrom)
            {
                return 0f;
            }
            float now = Time.time;
            if (now >= nextLightning)
            {
                lightningTime = now;
                nextLightning = now + Random.Range(5f, 14f) * Mathf.Lerp(1.5f, 0.6f, Storm);
            }
            float t = now - lightningTime;
            // 번쩍 - 잠깐 - 한 번 더
            if (t < 0.08f) return 3f;
            if (t > 0.16f && t < 0.24f) return 2f;
            return 0f;
        }

        void UpdateRain(Camera cam, float s)
        {
            if (rainMaterial == null || cam == null)
            {
                return;
            }
            if (rain == null)
            {
                rain = CreateRain();
            }
            var under = cam.GetComponent<UnderwaterView>();
            bool underwater = under != null && under.IsUnderwater;
            rain.transform.position = cam.transform.position + Vector3.up * 14f;
            var emission = rain.emission;
            emission.rateOverTime = underwater ? 0f : maxRain * Smooth(0.25f, 0.85f, s);
        }

        ParticleSystem CreateRain()
        {
            var go = new GameObject("Rain");
            go.transform.SetParent(transform, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.loop = true;
            main.startLifetime = 1.1f;
            main.startSpeed = 0f;
            main.startSize = 0.05f;
            main.startColor = new Color(0.75f, 0.82f, 0.9f, 0.55f);
            main.maxParticles = 4000;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(45f, 0.5f, 45f);
            var velocity = ps.velocityOverLifetime;
            velocity.enabled = true;
            velocity.space = ParticleSystemSimulationSpace.World;
            velocity.x = new ParticleSystem.MinMaxCurve(-1.5f);
            velocity.y = new ParticleSystem.MinMaxCurve(-22f);
            velocity.z = new ParticleSystem.MinMaxCurve(0.5f);
            var emission = ps.emission;
            emission.rateOverTime = 0f;
            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Stretch;
            renderer.velocityScale = 0.05f;
            renderer.lengthScale = 1.5f;
            renderer.sharedMaterial = rainMaterial;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            ps.Play();
            return ps;
        }

        static float Smooth(float from, float to, float x)
        {
            float t = Mathf.Clamp01((x - from) / (to - from));
            return t * t * (3f - 2f * t);
        }
    }
}
