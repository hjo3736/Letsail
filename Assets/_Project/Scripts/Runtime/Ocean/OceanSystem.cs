using System;
using UnityEngine;

namespace Letsail.Ocean
{
    /// <summary>
    /// 바다 상태(파도 설정, 시간)를 셰이더에 넘기고, 게임 코드에 수면 높이를 알려 준다.
    /// 씬에 하나만 둔다.
    /// </summary>
    [ExecuteAlways]
    [DefaultExecutionOrder(-100)]
    public sealed class OceanSystem : MonoBehaviour
    {
        static readonly int WaveCountId = Shader.PropertyToID("_LetsailWaveCount");
        static readonly int WavesId = Shader.PropertyToID("_LetsailWaves");
        static readonly int TimeId = Shader.PropertyToID("_LetsailOceanTime");
        static readonly int SeaLevelId = Shader.PropertyToID("_LetsailSeaLevel");
        static readonly int AmpBaseId = Shader.PropertyToID("_LetsailWaveAmpBase");
        static readonly int AmpCellsId = Shader.PropertyToID("_LetsailWaveAmpCells");
        static readonly int AmpCellCountId = Shader.PropertyToID("_LetsailWaveAmpCellCount");

        /// <summary>파도 세기 구역의 최대 개수 (셰이더 배열 길이와 같아야 한다).</summary>
        public const int MaxWeatherCells = 8;

        /// <summary>
        /// 바다 시간 공급원 (초). 멀티플레이에서는 네트워크 서버 시간으로 바꿔
        /// 모든 플레이어의 파도를 맞춘다.
        /// </summary>
        public static Func<double> TimeSource = () => Time.timeAsDouble;

        public static OceanSystem Instance { get; private set; }

        [SerializeField] OceanSettings settings;

        // 모든 파도의 뾰족함 합이 이보다 커지면 물결이 꼬이므로 날씨가 거칠어도 넘지 않게 한다.
        const float MaxTotalSteepness = 0.85f;

        // 셰이더 배열은 항상 MaxWaves 길이로 넘긴다 (처음 넘긴 길이로 고정되기 때문).
        readonly Vector4[] waves = new Vector4[OceanSettings.MaxWaves];
        // 파도 세기 (0~1, 1 = 모든 파도 뾰족함 합이 MaxTotalSteepness). 위치마다 다르다: 기본값 + 구역들.
        float amplitudeBase = 0.5f;
        bool fieldSet; // 날씨가 아직 값을 안 넣었으면 설정 그대로(높이 1)
        readonly Vector4[] amplitudeCells = new Vector4[MaxWeatherCells]; // (x, z, 반지름, 세기 더하기)
        int amplitudeCellCount;
        // 날씨의 "파도 높이" 1 이 파도 세기 몇에 해당하는지 (설정 파도의 뾰족함 합 / MaxTotalSteepness)
        float levelToAmplitude = 1f;
        float wavelengthScale = 1f;
        int waveCount;
        float seaLevel;

        /// <summary>파도가 없을 때 수면 높이 (월드 y).</summary>
        public float SeaLevel => seaLevel;

        /// <summary>현재 프레임의 바다 시간 (초).</summary>
        public float CurrentTime { get; private set; }

        ReadOnlySpan<Vector4> ActiveWaves => new ReadOnlySpan<Vector4>(waves, 0, waveCount);

        void OnEnable()
        {
            Instance = this;
            ApplySettings();
            UpdateTime();
        }

        void OnDisable()
        {
            if (Instance == this)
            {
                Instance = null;
            }
        }

        void OnValidate()
        {
            if (isActiveAndEnabled)
            {
                ApplySettings();
            }
        }

        void Update()
        {
            UpdateTime();
        }

        /// <summary>
        /// 위치마다 다른 파도 세기. 날씨의 "파도 높이" 단위(1 = 설정 그대로)로 기본값과 구역들을 받는다.
        /// 구역 (x, z, 반지름, 더하는 높이): 가운데서 가장 세고 반지름에서 0 이 된다. 파장은 모든 곳이 같다.
        /// 모든 플레이어가 같은 값을 넣어야 파도가 같아진다 (WeatherSystem 이 맞춰 준다).
        /// </summary>
        public void SetWeatherField(float wavelength, float baseLevel, Vector4[] cells, int cellCount)
        {
            if (!Mathf.Approximately(wavelength, wavelengthScale))
            {
                wavelengthScale = Mathf.Max(0.1f, wavelength);
                ApplySettings();
            }
            fieldSet = true;
            amplitudeBase = baseLevel * levelToAmplitude;
            amplitudeCellCount = Mathf.Min(cellCount, MaxWeatherCells);
            for (int i = 0; i < MaxWeatherCells; i++)
            {
                Vector4 c = i < amplitudeCellCount ? cells[i] : Vector4.zero;
                c.w *= levelToAmplitude;
                amplitudeCells[i] = c;
            }
            PushAmplitude();
        }

        void PushAmplitude()
        {
            Shader.SetGlobalFloat(AmpBaseId, amplitudeBase);
            Shader.SetGlobalVectorArray(AmpCellsId, amplitudeCells);
            Shader.SetGlobalFloat(AmpCellCountId, amplitudeCellCount);
        }

        /// <summary>파도 설정 요약 (여러 PC 의 파도가 같은지 검사용).</summary>
        public string WaveDigest()
        {
            var sb = new System.Text.StringBuilder();
            sb.Append(seaLevel.ToString("F3")).Append(';').Append(wavelengthScale.ToString("F3")).Append(';');
            for (int i = 0; i < waveCount; i++)
            {
                Vector4 w = waves[i];
                sb.Append(w.x.ToString("F4")).Append(',').Append(w.y.ToString("F4")).Append(',').Append(w.z.ToString("F4")).Append(',').Append(w.w.ToString("F3")).Append(';');
            }
            return sb.ToString();
        }

        /// <summary>그 위치의 파도 세기 (0~1). 셰이더 LetsailWaveAmplitude 와 같은 공식.</summary>
        public float AmplitudeAt(Vector2 xz)
        {
            float a = amplitudeBase;
            for (int i = 0; i < amplitudeCellCount; i++)
            {
                Vector4 c = amplitudeCells[i];
                float dx = xz.x - c.x, dz = xz.y - c.y;
                float t = Mathf.Clamp01(1f - Mathf.Sqrt(dx * dx + dz * dz) / Mathf.Max(1f, c.z));
                a += c.w * t * t * (3f - 2f * t);
            }
            return Mathf.Clamp01(a);
        }

        /// <summary>
        /// 모든 곳의 파도 크기를 똑같이 바꾼다 (로비처럼 구역 날씨가 없는 곳). 높이 1 이면 설정 그대로.
        /// </summary>
        public void SetWeatherScale(float steepness, float wavelength)
        {
            SetWeatherField(wavelength, steepness, amplitudeCells, 0);
        }

        /// <summary>설정 에셋의 값을 셰이더에 다시 넘긴다. 런타임에 파도를 바꾼 뒤 호출한다.</summary>
        public void ApplySettings()
        {
            Array.Clear(waves, 0, waves.Length);
            waveCount = 0;
            seaLevel = 0f;
            levelToAmplitude = 1f;

            if (settings != null)
            {
                var source = settings.Waves;
                waveCount = Mathf.Min(source.Count, OceanSettings.MaxWaves);
                float total = 0f;
                for (int i = 0; i < waveCount; i++)
                {
                    total += source[i].steepness;
                }
                // 파도는 가장 셀 때(뾰족함 합 = MaxTotalSteepness)로 넘기고, 위치마다 파도 세기(0~1)를 곱한다.
                float full = total > 0f ? MaxTotalSteepness / total : 1f;
                levelToAmplitude = total > 0f ? total / MaxTotalSteepness : 1f;
                for (int i = 0; i < waveCount; i++)
                {
                    Vector4 w = source[i].ToVector();
                    w.z *= full;
                    w.w *= wavelengthScale;
                    waves[i] = w;
                }
                seaLevel = settings.SeaLevel;
            }
            if (!fieldSet)
            {
                amplitudeBase = levelToAmplitude;
            }

            Shader.SetGlobalVectorArray(WavesId, waves);
            Shader.SetGlobalFloat(WaveCountId, waveCount);
            Shader.SetGlobalFloat(SeaLevelId, seaLevel);
            PushAmplitude();
        }

        void UpdateTime()
        {
            CurrentTime = Application.isPlaying ? (float)TimeSource() : 0f;
            Shader.SetGlobalFloat(TimeId, CurrentTime);
        }

        Vector3 Displacement(Vector2 origin)
        {
            return GerstnerMath.Displacement(ActiveWaves, origin, CurrentTime) * AmplitudeAt(origin);
        }

        /// <summary>월드 위치 바로 위·아래의 수면 높이 (월드 y).</summary>
        public float GetWaterHeight(Vector3 worldPosition)
        {
            // Gerstner 파도는 수면 점을 옆으로도 밀기 때문에, 그 자리에 도착하는 원래 점을 반복해서 찾는다.
            var position = new Vector2(worldPosition.x, worldPosition.z);
            Vector2 origin = position;
            for (int i = 0; i < 4; i++)
            {
                Vector3 d = Displacement(origin);
                origin = position - new Vector2(d.x, d.z);
            }
            return seaLevel + Displacement(origin).y;
        }
    }
}
