using System;
using Letsail.Ocean;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Letsail.World
{
    /// <summary>
    /// 날씨. 파도의 크기와 바람(방향, 세기)을 정한다. 날씨가 바뀌면 transitionSeconds 동안 서서히 넘어간다.
    /// 날씨는 호스트(서버)가 정하고, 모든 선원이 같은 시간·같은 값으로 계산하므로 파도가 똑같다.
    /// 씬에 하나 둔다 (NetworkObject 가 필요하다). 네트워크 없이 실행하면 혼자 쓰는 상태로 동작한다.
    /// 개발 중에는 호스트가 F5~F8 로 날씨를 바로 바꿀 수 있다.
    /// </summary>
    [DefaultExecutionOrder(-90)]
    public sealed class WeatherSystem : NetworkBehaviour
    {
        [Serializable]
        public struct Preset
        {
            public string name;
            [Tooltip("파도 높이 배율 (뾰족함에 곱한다)")] public float waveHeight;
            [Tooltip("파도 길이 배율 (파장에 곱한다). 거친 날씨일수록 크고 긴 너울")] public float waveLength;
            [Tooltip("바람 세기 (m/s)")] public float windSpeed;
        }

        [SerializeField]
        Preset[] presets =
        {
            new Preset { name = "잔잔함", waveHeight = 0.5f, waveLength = 0.9f, windSpeed = 3f },
            new Preset { name = "보통", waveHeight = 1f, waveLength = 1f, windSpeed = 6f },
            new Preset { name = "거친 바다", waveHeight = 1.5f, waveLength = 1.25f, windSpeed = 10f },
            new Preset { name = "폭풍", waveHeight = 2.2f, waveLength = 1.6f, windSpeed = 16f },
        };

        [SerializeField, Tooltip("처음 날씨 (presets 번호)")] int startPreset = 1;
        [SerializeField, Tooltip("날씨가 바뀌는 데 걸리는 시간 (초)")] float transitionSeconds = 30f;
        [SerializeField, Tooltip("저절로 날씨가 바뀐다")] bool autoChange = true;
        [SerializeField, Tooltip("저절로 바뀌는 간격 (초, 최소~최대)")] Vector2 changeInterval = new Vector2(90f, 180f);
        [SerializeField, Tooltip("저절로 바뀔 때 고를 수 있는 가장 거친 날씨 번호. 폭풍은 사건으로만 온다")] int maxAutoPreset = 2;
        [SerializeField, Tooltip("개발 빌드에서 호스트가 F5~F8 로 날씨를 바꿀 수 있다")] bool devKeys = true;

        // 바뀌기 시작할 때의 값(from)과 목표 날씨(target). 모두 같은 값으로 섞어서 쓴다.
        readonly NetworkVariable<int> target = new NetworkVariable<int>();
        readonly NetworkVariable<Vector4> from = new NetworkVariable<Vector4>(); // (파도 높이, 파도 길이, 바람 세기, 바람 방향)
        readonly NetworkVariable<float> targetWindAngle = new NetworkVariable<float>();
        readonly NetworkVariable<double> changedAt = new NetworkVariable<double>();

        int offlineTarget;
        Vector4 offlineFrom;
        float offlineWindAngle;
        double offlineChangedAt;
        double nextAutoChange;

        public static WeatherSystem Instance { get; private set; }

        bool Online => IsSpawned;
        int Target => Mathf.Clamp(Online ? target.Value : offlineTarget, 0, presets.Length - 1);
        Vector4 From => Online ? from.Value : offlineFrom;
        float TargetWindAngle => Online ? targetWindAngle.Value : offlineWindAngle;
        double ChangedAt => Online ? changedAt.Value : offlineChangedAt;

        static double Now => OceanSystem.TimeSource();

        /// <summary>지금 날씨로 넘어간 정도 (0~1).</summary>
        public float Blend => transitionSeconds <= 0f ? 1f : Mathf.Clamp01((float)((Now - ChangedAt) / transitionSeconds));

        public string CurrentName => presets.Length > 0 ? presets[Target].name : "";
        public float WaveHeight => Mathf.Lerp(From.x, presets[Target].waveHeight, Blend);
        public float WaveLength => Mathf.Lerp(From.y, presets[Target].waveLength, Blend);
        /// <summary>바람 세기 (m/s).</summary>
        public float WindSpeed => Mathf.Lerp(From.z, presets[Target].windSpeed, Blend);
        /// <summary>바람이 불어 가는 방향 (도, 0 = +Z, 90 = +X).</summary>
        public float WindAngle => Mathf.LerpAngle(From.w, TargetWindAngle, Blend);
        /// <summary>바람이 불어 가는 방향 (수평 단위 벡터).</summary>
        public Vector3 WindDirection => Quaternion.Euler(0f, WindAngle, 0f) * Vector3.forward;

        void Awake()
        {
            Instance = this;
            var start = presets[Mathf.Clamp(startPreset, 0, presets.Length - 1)];
            offlineTarget = startPreset;
            offlineWindAngle = UnityEngine.Random.Range(0f, 360f);
            offlineFrom = new Vector4(start.waveHeight, start.waveLength, start.windSpeed, offlineWindAngle);
            offlineChangedAt = -transitionSeconds;
            ScheduleNextChange();
        }

        public override void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }
            base.OnDestroy();
        }

        public override void OnNetworkSpawn()
        {
            if (IsServer)
            {
                target.Value = offlineTarget;
                from.Value = offlineFrom;
                targetWindAngle.Value = offlineWindAngle;
                changedAt.Value = Now - transitionSeconds;
                ScheduleNextChange();
            }
        }

        void Update()
        {
            if (OceanSystem.Instance != null && presets.Length > 0)
            {
                OceanSystem.Instance.SetWeatherScale(WaveHeight, WaveLength);
            }
            if (Online && !IsServer)
            {
                return;
            }
            if (devKeys && Debug.isDebugBuild && Keyboard.current != null)
            {
                Key[] keys = { Key.F5, Key.F6, Key.F7, Key.F8 };
                for (int i = 0; i < keys.Length && i < presets.Length; i++)
                {
                    if (Keyboard.current[keys[i]].wasPressedThisFrame)
                    {
                        SetWeather(i);
                    }
                }
            }
            if (autoChange && Now >= nextAutoChange)
            {
                int next = UnityEngine.Random.Range(0, Mathf.Min(maxAutoPreset, presets.Length - 1) + 1);
                SetWeather(next);
            }
        }

        /// <summary>날씨를 바꾼다 (서버에서만). 지금 상태에서 서서히 넘어간다. 바람 방향도 조금 돈다.</summary>
        public void SetWeather(int index)
        {
            if (Online && !IsServer)
            {
                return;
            }
            index = Mathf.Clamp(index, 0, presets.Length - 1);
            var start = new Vector4(WaveHeight, WaveLength, WindSpeed, WindAngle);
            float angle = Mathf.Repeat(WindAngle + UnityEngine.Random.Range(-60f, 60f), 360f);
            if (Online)
            {
                from.Value = start;
                target.Value = index;
                targetWindAngle.Value = angle;
                changedAt.Value = Now;
            }
            else
            {
                offlineFrom = start;
                offlineTarget = index;
                offlineWindAngle = angle;
                offlineChangedAt = Now;
            }
            ScheduleNextChange();
            Debug.Log($"날씨: {presets[index].name} (바람 {presets[index].windSpeed} m/s, {angle:0}°)");
        }

        void ScheduleNextChange()
        {
            nextAutoChange = Now + UnityEngine.Random.Range(changeInterval.x, changeInterval.y);
        }
    }
}
