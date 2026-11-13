using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace Letsail.Ocean
{
    /// <summary>
    /// 카메라가 수면 아래로 내려가면 물속처럼 보이게 한다: 푸른 안개가 끼고, 먼 곳(하늘이 보이던 곳)도 푸른색으로 채운다.
    /// 해(방향광)에는 일렁이는 빛무늬(코스틱) 쿠키를 씌워서 배 밑바닥, 캐릭터, 바닥에 물결 빛이 비친다.
    /// 배 안쪽(선창)은 수면보다 낮아도 물속이 아니므로 바꾸지 않는다. 메인 카메라에 단다.
    /// 화면에 보이는 것만 바꾸므로 각자의 PC 에서 따로 동작한다.
    /// </summary>
    // 카메라가 이번 프레임 위치로 옮겨진 뒤에 판단한다.
    [DefaultExecutionOrder(100)]
    [RequireComponent(typeof(Camera))]
    public sealed class UnderwaterView : MonoBehaviour
    {
        [SerializeField, Tooltip("물속 색 (안개와 먼 곳)")] Color waterColor = new Color(0.05f, 0.33f, 0.45f);
        [SerializeField, Tooltip("물속 안개 짙기. 클수록 가까운 것만 보인다")] float fogDensity = 0.09f;
        [SerializeField, Tooltip("수면 바로 아래에서 깜빡이지 않게 두는 여유 (m)")] float surfaceMargin = 0.05f;

        [Header("빛무늬 (코스틱)")]
        [SerializeField, Tooltip("빛무늬 프레임 (Letsail > 물속 빛무늬 굽기). 비우면 빛무늬 없음")] CausticsFrames caustics;
        [SerializeField, Tooltip("빛무늬 한 장이 덮는 크기 (m)")] float causticsSize = 4f;
        [SerializeField, Tooltip("1초에 넘기는 프레임 수")] float causticsFps = 12f;
        [SerializeField, Tooltip("빛무늬가 흘러가는 속도 (m/s)")] Vector2 causticsDrift = new Vector2(0.15f, 0.08f);

        Camera cam;
        bool underwater;

        // 물에 들어가기 전 화면 설정 (나오면 되돌린다)
        bool savedFog;
        FogMode savedFogMode;
        Color savedFogColor;
        float savedFogDensity;
        CameraClearFlags savedClearFlags;
        Color savedBackground;

        Light sun;
        UniversalAdditionalLightData sunData;
        Texture savedCookie;
        Vector2 savedCookieSize;
        Vector2 savedCookieOffset;

        public bool IsUnderwater => underwater;

        void Awake()
        {
            cam = GetComponent<Camera>();
        }

        void OnDisable()
        {
            SetUnderwater(false);
        }

        void LateUpdate()
        {
            var ocean = OceanSystem.Instance;
            bool below = false;
            if (ocean != null)
            {
                Vector3 p = transform.position;
                below = p.y < ocean.GetWaterHeight(p) - surfaceMargin && !HullWaterMask.IsInsideHull(p);
            }
            SetUnderwater(below);
            if (underwater)
            {
                // 하늘(낮밤·날씨)이 매 프레임 안개를 바꾸므로 물속에서는 매 프레임 덮어쓴다.
                ApplyWaterFog();
                AnimateCaustics();
            }
        }

        void AnimateCaustics()
        {
            if (sun == null || caustics == null || caustics.Count == 0)
            {
                return;
            }
            float time = Time.time;
            sun.cookie = caustics[(int)(time * causticsFps) % caustics.Count];
            if (sunData != null)
            {
                sunData.lightCookieOffset = causticsDrift * time;
            }
        }

        void SetUnderwater(bool value)
        {
            if (value == underwater)
            {
                return;
            }
            underwater = value;
            if (value)
            {
                savedFog = RenderSettings.fog;
                savedFogMode = RenderSettings.fogMode;
                savedFogColor = RenderSettings.fogColor;
                savedFogDensity = RenderSettings.fogDensity;
                savedClearFlags = cam.clearFlags;
                savedBackground = cam.backgroundColor;

                ApplyWaterFog();
                BeginCaustics();
            }
            else
            {
                RenderSettings.fog = savedFog;
                RenderSettings.fogMode = savedFogMode;
                RenderSettings.fogColor = savedFogColor;
                RenderSettings.fogDensity = savedFogDensity;
                if (cam != null)
                {
                    cam.clearFlags = savedClearFlags;
                    cam.backgroundColor = savedBackground;
                }
                EndCaustics();
            }
        }

        void ApplyWaterFog()
        {
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Exponential;
            // 밤이나 폭풍이면 물속도 어둡다.
            Color color = waterColor * Mathf.Lerp(0.25f, 1f, Letsail.World.SkyController.Daylight);
            color.a = 1f;
            RenderSettings.fogColor = color;
            RenderSettings.fogDensity = fogDensity;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = color;
        }

        void BeginCaustics()
        {
            sun = caustics != null && caustics.Count > 0 ? FindSun() : null;
            if (sun == null)
            {
                return;
            }
            savedCookie = sun.cookie;
            sunData = sun.GetComponent<UniversalAdditionalLightData>();
            if (sunData != null)
            {
                savedCookieSize = sunData.lightCookieSize;
                savedCookieOffset = sunData.lightCookieOffset;
                sunData.lightCookieSize = Vector2.one * causticsSize;
            }
            AnimateCaustics();
        }

        void EndCaustics()
        {
            if (sun != null)
            {
                sun.cookie = savedCookie;
                if (sunData != null)
                {
                    sunData.lightCookieSize = savedCookieSize;
                    sunData.lightCookieOffset = savedCookieOffset;
                }
            }
            sun = null;
            sunData = null;
        }

        static Light FindSun()
        {
            if (RenderSettings.sun != null)
            {
                return RenderSettings.sun;
            }
            foreach (var light in FindObjectsByType<Light>(FindObjectsSortMode.None))
            {
                if (light.type == LightType.Directional && light.isActiveAndEnabled)
                {
                    return light;
                }
            }
            return null;
        }
    }
}
