using System;
using Letsail.Ocean;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Letsail.World
{
    /// <summary>
    /// 날씨. 위치마다 파도 크기와 바람 세기가 다르고, 시간이 지나며 계속 바뀐다.
    /// - 구역 날씨(regional): 맵 위를 날씨 구역(폭풍, 거친 바다, 잔잔한 곳)이 떠다닌다. 구역은 생겨나서 커졌다가 사라지고,
    ///   사라지면 다른 곳에서 새로 생긴다. 모든 값은 시드와 서버 시간으로 계산하므로 호스트는 시드만 보내고
    ///   모든 선원이 같은 날씨·같은 파도를 본다 (나중에 들어온 사람도 같다).
    /// - 구역 날씨를 끄면(로비) 모든 곳이 startPreset 날씨로 고정된다.
    /// 날씨 세기는 "파도 높이" 단위(presets 의 waveHeight: 잔잔함 0.5 ~ 폭풍 2.2)로 다룬다.
    /// 개발 중에는 호스트가 F5~F8 로 모든 곳의 날씨를 고정할 수 있다 (같은 키를 다시 누르면 풀린다).
    /// 씬에 하나 둔다 (NetworkObject 가 필요하다). 네트워크 없이 실행하면 혼자 쓰는 상태로 동작한다.
    /// </summary>
    [DefaultExecutionOrder(-90)]
    public sealed class WeatherSystem : NetworkBehaviour
    {
        [Serializable]
        public struct Preset
        {
            public string name;
            [Tooltip("파도 높이 배율 (뾰족함에 곱한다)")] public float waveHeight;
            [Tooltip("파도 길이 배율 (구역 날씨를 끈 곳에서만 쓴다)")] public float waveLength;
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

        [SerializeField, Tooltip("구역 날씨를 쓴다 (항해). 끄면 모든 곳이 startPreset (로비)")] bool regional = true;
        [SerializeField, Tooltip("구역 날씨를 끈 곳의 날씨 (presets 번호)")] int startPreset = 1;
        [SerializeField, Tooltip("개발 빌드에서 호스트가 F5~F8 로 날씨를 고정할 수 있다")] bool devKeys = true;

        [Header("구역 날씨")]
        [SerializeField, Tooltip("모든 곳의 파도 길이 배율 (구역마다 다르게 하면 물결이 어긋난다)")] float regionalWaveLength = 1.2f;
        [SerializeField, Tooltip("기본 날씨 높이 (구역 밖). 시간에 따라 이 둘레에서 오르내린다")] float baseLevel = 0.95f;
        [SerializeField, Tooltip("기본 날씨가 오르내리는 폭과 주기(초)")] Vector2 baseSwing = new Vector2(0.25f, 900f);
        [SerializeField, Tooltip("날씨 구역이 생기는 범위 (출발점에서 반지름, m)")] float mapRadius = 1400f;
        [SerializeField, Range(1, OceanSystem.MaxWeatherCells), Tooltip("동시에 있는 날씨 구역 수")] int cellCount = 8;
        [SerializeField, Tooltip("구역 반지름 (m)")] Vector2 cellRadius = new Vector2(250f, 520f);
        [SerializeField, Tooltip("구역 하나가 생겨서 사라지기까지 (초)")] Vector2 cellLifetime = new Vector2(240f, 480f);
        [SerializeField, Tooltip("구역이 흘러가는 속도 (m/s)")] Vector2 cellDrift = new Vector2(1.5f, 5f);
        [SerializeField, Tooltip("폭풍 구역이 더하는 높이와 나올 확률")] Vector2 storm = new Vector2(1.35f, 0.3f);
        [SerializeField, Tooltip("거친 구역이 더하는 높이와 나올 확률 (나머지는 잔잔한 구역)")] Vector2 rough = new Vector2(0.6f, 0.4f);
        [SerializeField, Tooltip("잔잔한 구역이 빼는 높이")] float calm = 0.55f;
        [SerializeField, Tooltip("날씨 높이 범위 (최소, 최대)")] Vector2 levelRange = new Vector2(0.4f, 2.3f);

        readonly NetworkVariable<int> seed = new NetworkVariable<int>();
        // 개발용 고정 날씨 (파도 높이). 0 이하면 고정 안 함.
        readonly NetworkVariable<float> forcedLevel = new NetworkVariable<float>(-1f);

        int offlineSeed;
        float offlineForced = -1f;
        readonly Vector4[] cells = new Vector4[OceanSystem.MaxWeatherCells]; // (x, z, 반지름, 더하는 높이)
        int activeCells;
        float currentBase;

        public static WeatherSystem Instance { get; private set; }

        bool Online => IsSpawned;
        int Seed => Online ? seed.Value : offlineSeed;
        float Forced => Online ? forcedLevel.Value : offlineForced;
        static double Now => OceanSystem.TimeSource();

        public bool Regional => regional;

        /// <summary>바람이 불어 가는 방향 (도, 0 = +Z, 90 = +X). 모든 곳이 같고 천천히 돈다.</summary>
        public float WindAngle { get; private set; }

        /// <summary>바람이 불어 가는 방향 (수평 단위 벡터).</summary>
        public Vector3 WindDirection => Quaternion.Euler(0f, WindAngle, 0f) * Vector3.forward;

        /// <summary>지금 계산된 날씨 구역들 (x, z, 반지름, 더하는 높이). 디버그·지도용.</summary>
        public ReadOnlySpan<Vector4> Cells => new ReadOnlySpan<Vector4>(cells, 0, activeCells);

        void Awake()
        {
            Instance = this;
            offlineSeed = UnityEngine.Random.Range(1, int.MaxValue);
            Recalculate();
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
                seed.Value = offlineSeed;
                forcedLevel.Value = offlineForced;
            }
            Recalculate();
        }

        void Update()
        {
            Recalculate();
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
                        float level = presets[i].waveHeight;
                        SetForcedLevel(Mathf.Approximately(Forced, level) ? -1f : level);
                    }
                }
            }
        }

        /// <summary>모든 곳의 날씨를 이 높이로 고정한다 (서버에서만, 개발·사건용). 0 이하면 풀린다.</summary>
        public void SetForcedLevel(float level)
        {
            if (Online && !IsServer)
            {
                return;
            }
            if (Online) forcedLevel.Value = level;
            else offlineForced = level;
            Debug.Log(level > 0f ? $"날씨 고정: {NameOfLevel(level)}" : "날씨 고정 풀림");
        }

        /// <summary>프리셋 번호로 고정 (예전 방식 호환).</summary>
        public void SetWeather(int index) => SetForcedLevel(presets[Mathf.Clamp(index, 0, presets.Length - 1)].waveHeight);

        // 지금 시간의 날씨 구역과 바람을 계산해 바다에 넘긴다. 모든 PC 가 같은 시드·시간으로 같은 값을 얻는다.
        void Recalculate()
        {
            double now = Application.isPlaying ? Now : 0.0;
            var random = new System.Random(Seed);
            float windBase = (float)random.NextDouble() * 360f;
            WindAngle = Mathf.Repeat(windBase + 50f * Mathf.Sin((float)(now * 2.0 * Math.PI / 1300.0)) + 25f * Mathf.Sin((float)(now * 2.0 * Math.PI / 410.0)), 360f);

            float wavelength;
            activeCells = 0;
            if (Forced > 0f)
            {
                currentBase = Forced;
                wavelength = regional ? regionalWaveLength : WaveLengthOfLevel(Forced);
            }
            else if (!regional)
            {
                var p = presets[Mathf.Clamp(startPreset, 0, presets.Length - 1)];
                currentBase = p.waveHeight;
                wavelength = p.waveLength;
            }
            else
            {
                float phase = (float)random.NextDouble() * Mathf.PI * 2f;
                currentBase = baseLevel + baseSwing.x * Mathf.Sin((float)(now * 2.0 * Math.PI / Math.Max(1.0, baseSwing.y)) + phase);
                wavelength = regionalWaveLength;
                for (int i = 0; i < cellCount; i++)
                {
                    cells[activeCells++] = CellAt(i, now);
                }
            }
            if (OceanSystem.Instance != null)
            {
                OceanSystem.Instance.SetWeatherField(wavelength, currentBase, cells, activeCells);
            }
        }

        // 구역 i 의 지금 모습. 구역은 수명마다 새 자리에서 다시 생긴다 (수명 번호로 시드를 바꿔서).
        Vector4 CellAt(int index, double now)
        {
            var slot = new System.Random(unchecked(Seed * 31 + index * 7919));
            float lifetime = Mathf.Lerp(cellLifetime.x, cellLifetime.y, (float)slot.NextDouble());
            double offset = slot.NextDouble() * lifetime;
            double age = now + offset;
            long cycle = (long)Math.Floor(age / lifetime);
            float u = (float)(age / lifetime - cycle); // 0 → 1 (생겨나서 사라질 때까지)

            var life = new System.Random(unchecked(Seed * 131 + index * 7919 + (int)cycle * 104729));
            float angle = (float)life.NextDouble() * Mathf.PI * 2f;
            float distance = Mathf.Sqrt((float)life.NextDouble()) * mapRadius;
            Vector2 start = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * distance;
            float driftAngle = (float)life.NextDouble() * Mathf.PI * 2f;
            float driftSpeed = Mathf.Lerp(cellDrift.x, cellDrift.y, (float)life.NextDouble());
            Vector2 position = start + new Vector2(Mathf.Cos(driftAngle), Mathf.Sin(driftAngle)) * driftSpeed * u * lifetime;
            float radius = Mathf.Lerp(cellRadius.x, cellRadius.y, (float)life.NextDouble());
            double kind = life.NextDouble();
            float strength = kind < storm.y ? storm.x : kind < storm.y + rough.y ? rough.x : -calm;
            // 서서히 생겨나 가장 세졌다가 서서히 사라진다.
            float envelope = Mathf.Sin(u * Mathf.PI);
            return new Vector4(position.x, position.y, radius, strength * envelope);
        }

        /// <summary>그 위치의 날씨 높이 (파도 높이 단위: 잔잔함 0.5 ~ 폭풍 2.2).</summary>
        public float LevelAt(Vector3 position)
        {
            float level = currentBase;
            for (int i = 0; i < activeCells; i++)
            {
                Vector4 c = cells[i];
                float dx = position.x - c.x, dz = position.z - c.y;
                float t = Mathf.Clamp01(1f - Mathf.Sqrt(dx * dx + dz * dz) / Mathf.Max(1f, c.z));
                level += c.w * t * t * (3f - 2f * t);
            }
            return Forced > 0f ? level : Mathf.Clamp(level, levelRange.x, levelRange.y);
        }

        /// <summary>
        /// 정해진 시각의 그 위치 날씨 높이. 지금 프레임 값을 바꾸지 않고 다시 계산한다 (여러 PC 가 같은 시각의 값을 맞춰 보는 검사용).
        /// </summary>
        public float LevelAtTime(Vector3 position, double time)
        {
            if (Forced > 0f) return Forced;
            if (!regional) return presets[Mathf.Clamp(startPreset, 0, presets.Length - 1)].waveHeight;
            var random = new System.Random(Seed);
            random.NextDouble(); // 바람 방향 (Recalculate 와 같은 순서)
            float phase = (float)random.NextDouble() * Mathf.PI * 2f;
            float level = baseLevel + baseSwing.x * Mathf.Sin((float)(time * 2.0 * Math.PI / Math.Max(1.0, baseSwing.y)) + phase);
            for (int i = 0; i < cellCount; i++)
            {
                Vector4 c = CellAt(i, time);
                float dx = position.x - c.x, dz = position.z - c.y;
                float t = Mathf.Clamp01(1f - Mathf.Sqrt(dx * dx + dz * dz) / Mathf.Max(1f, c.z));
                level += c.w * t * t * (3f - 2f * t);
            }
            return Mathf.Clamp(level, levelRange.x, levelRange.y);
        }

        /// <summary>개발용 고정 날씨 (0 이하면 고정 안 함).</summary>
        public float ForcedLevel => Forced;

        /// <summary>그 위치의 바람 세기 (m/s).</summary>
        public float WindSpeedAt(Vector3 position) => Interpolate(LevelAt(position), p => p.windSpeed);

        /// <summary>그 위치의 날씨 이름 (가장 가까운 프리셋).</summary>
        public string NameAt(Vector3 position) => NameOfLevel(LevelAt(position));

        /// <summary>그 위치가 얼마나 거친지 (0 = 보통 이하, 1 = 폭풍). 비·어두움 등 화면 효과용.</summary>
        public float StormAt(Vector3 position)
        {
            if (presets.Length < 2) return 0f;
            return Mathf.Clamp01(Mathf.InverseLerp(presets[1].waveHeight, presets[presets.Length - 1].waveHeight, LevelAt(position)));
        }

        string NameOfLevel(float level)
        {
            int best = 0;
            for (int i = 1; i < presets.Length; i++)
            {
                if (Mathf.Abs(presets[i].waveHeight - level) < Mathf.Abs(presets[best].waveHeight - level)) best = i;
            }
            return presets[best].name;
        }

        float WaveLengthOfLevel(float level) => Interpolate(level, p => p.waveLength);

        // 날씨 높이 사이의 프리셋 값을 섞는다.
        float Interpolate(float level, Func<Preset, float> value)
        {
            if (presets.Length == 0) return 0f;
            if (level <= presets[0].waveHeight) return value(presets[0]);
            for (int i = 0; i < presets.Length - 1; i++)
            {
                if (level <= presets[i + 1].waveHeight)
                {
                    float t = Mathf.InverseLerp(presets[i].waveHeight, presets[i + 1].waveHeight, level);
                    return Mathf.Lerp(value(presets[i]), value(presets[i + 1]), t);
                }
            }
            return value(presets[presets.Length - 1]);
        }
    }
}
