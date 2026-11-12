using System.Collections.Generic;
using UnityEngine;

namespace Letsail.Core
{
    /// <summary>
    /// 플레이어가 고르는 설정. 이 PC에 저장되고 게임 시작 때 적용된다.
    /// 해상도와 화면 모드는 빌드한 게임에서만 실제로 바뀐다 (에디터는 Game 창 크기를 따른다).
    /// </summary>
    public static class GameSettings
    {
        const string Prefix = "Letsail.Settings.";

        // ---- 게임 ----

        public const float MinMouseSensitivity = 0.02f;
        public const float MaxMouseSensitivity = 0.3f;

        /// <summary>마우스 1픽셀당 카메라 회전 각도.</summary>
        public static float MouseSensitivity
        {
            get => Mathf.Clamp(PlayerPrefs.GetFloat(Prefix + "MouseSensitivity", 0.1f), MinMouseSensitivity, MaxMouseSensitivity);
            set => SetFloat("MouseSensitivity", Mathf.Clamp(value, MinMouseSensitivity, MaxMouseSensitivity));
        }

        public static bool InvertY
        {
            get => GetBool("InvertY", false);
            set => SetBool("InvertY", value);
        }

        /// <summary>true 면 웅크리기 키를 한 번 눌러 켜고 끈다.</summary>
        public static bool ToggleCrouch
        {
            get => GetBool("ToggleCrouch", false);
            set => SetBool("ToggleCrouch", value);
        }

        // ---- 화면 ----

        /// <summary>설정 메뉴에 보여 줄 화면 모드 (순서대로).</summary>
        public static readonly FullScreenMode[] ScreenModes =
        {
            FullScreenMode.ExclusiveFullScreen,
            FullScreenMode.FullScreenWindow,
            FullScreenMode.Windowed,
        };

        /// <summary>최대 프레임 선택지. 0은 제한 없음.</summary>
        public static readonly int[] FrameRateLimits = { 30, 60, 120, 144, 240, 0 };

        public static FullScreenMode ScreenMode
        {
            get => (FullScreenMode)PlayerPrefs.GetInt(Prefix + "ScreenMode", (int)Screen.fullScreenMode);
            set
            {
                PlayerPrefs.SetInt(Prefix + "ScreenMode", (int)value);
                PlayerPrefs.Save();
                ApplyDisplay();
            }
        }

        /// <summary>저장된 해상도. 저장된 게 없으면 지금 화면 크기.</summary>
        public static Vector2Int Resolution
        {
            get => new Vector2Int(PlayerPrefs.GetInt(Prefix + "Width", Screen.width), PlayerPrefs.GetInt(Prefix + "Height", Screen.height));
            set
            {
                PlayerPrefs.SetInt(Prefix + "Width", value.x);
                PlayerPrefs.SetInt(Prefix + "Height", value.y);
                PlayerPrefs.Save();
                ApplyDisplay();
            }
        }

        public static int QualityLevel
        {
            get => Mathf.Clamp(PlayerPrefs.GetInt(Prefix + "Quality", QualitySettings.GetQualityLevel()), 0, QualitySettings.names.Length - 1);
            set
            {
                PlayerPrefs.SetInt(Prefix + "Quality", value);
                PlayerPrefs.Save();
                ApplyRendering();
            }
        }

        public static bool VSync
        {
            get => GetBool("VSync", true);
            set
            {
                SetBool("VSync", value);
                ApplyRendering();
            }
        }

        /// <summary>최대 프레임. 0은 제한 없음. 수직 동기화가 켜져 있으면 모니터 주사율을 따른다.</summary>
        public static int FrameRateLimit
        {
            get => PlayerPrefs.GetInt(Prefix + "FrameRateLimit", 144);
            set
            {
                PlayerPrefs.SetInt(Prefix + "FrameRateLimit", value);
                PlayerPrefs.Save();
                ApplyRendering();
            }
        }

        // ---- 소리 (0~1) ----

        public static float MasterVolume
        {
            get => PlayerPrefs.GetFloat(Prefix + "MasterVolume", 1f);
            set
            {
                SetFloat("MasterVolume", Mathf.Clamp01(value));
                ApplyAudio();
            }
        }

        /// <summary>음악 볼륨. 오디오 믹서가 생기면 음악 그룹에 적용된다.</summary>
        public static float MusicVolume
        {
            get => PlayerPrefs.GetFloat(Prefix + "MusicVolume", 0.8f);
            set => SetFloat("MusicVolume", Mathf.Clamp01(value));
        }

        /// <summary>효과음 볼륨. 오디오 믹서가 생기면 효과음 그룹에 적용된다.</summary>
        public static float SfxVolume
        {
            get => PlayerPrefs.GetFloat(Prefix + "SfxVolume", 1f);
            set => SetFloat("SfxVolume", Mathf.Clamp01(value));
        }

        // ---- 적용 ----

        /// <summary>모니터가 지원하는 해상도 목록 (가로×세로 중복 제거, 작은 것부터).</summary>
        public static List<Vector2Int> GetAvailableResolutions()
        {
            var list = new List<Vector2Int>();
            foreach (var r in Screen.resolutions)
            {
                var size = new Vector2Int(r.width, r.height);
                if (!list.Contains(size))
                {
                    list.Add(size);
                }
            }
            if (list.Count == 0)
            {
                list.Add(new Vector2Int(Screen.width, Screen.height));
            }
            list.Sort((a, b) => a.x != b.x ? a.x.CompareTo(b.x) : a.y.CompareTo(b.y));
            return list;
        }

        public static void ApplyDisplay()
        {
#if !UNITY_EDITOR
            Vector2Int size = Resolution;
            Screen.SetResolution(size.x, size.y, ScreenMode);
#endif
        }

        public static void ApplyRendering()
        {
            if (QualitySettings.GetQualityLevel() != QualityLevel)
            {
                QualitySettings.SetQualityLevel(QualityLevel, true);
            }
            QualitySettings.vSyncCount = VSync ? 1 : 0;
            Application.targetFrameRate = FrameRateLimit > 0 ? FrameRateLimit : -1;
        }

        public static void ApplyAudio()
        {
            AudioListener.volume = MasterVolume;
        }

        public static string GetScreenModeName(FullScreenMode mode)
        {
            switch (mode)
            {
                case FullScreenMode.ExclusiveFullScreen: return "전체 화면";
                case FullScreenMode.FullScreenWindow: return "테두리 없는 창";
                case FullScreenMode.Windowed: return "창 모드";
                default: return mode.ToString();
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void ApplyOnStartup()
        {
            // 화면은 저장된 설정이 있을 때만 바꾼다. 처음 실행이면 Player Settings 기본값을 따른다.
            if (PlayerPrefs.HasKey(Prefix + "Width") || PlayerPrefs.HasKey(Prefix + "ScreenMode"))
            {
                ApplyDisplay();
            }
            ApplyRendering();
            ApplyAudio();
        }

        static bool GetBool(string key, bool fallback) => PlayerPrefs.GetInt(Prefix + key, fallback ? 1 : 0) == 1;

        static void SetBool(string key, bool value)
        {
            PlayerPrefs.SetInt(Prefix + key, value ? 1 : 0);
            PlayerPrefs.Save();
        }

        static void SetFloat(string key, float value)
        {
            PlayerPrefs.SetFloat(Prefix + key, value);
            PlayerPrefs.Save();
        }
    }
}
