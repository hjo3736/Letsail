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
        float steepnessScale = 1f;
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
        /// 날씨에 따라 파도 크기를 바꾼다. 뾰족함(높이)과 파장(길이)에 곱한다. 1 이면 설정 그대로.
        /// 모든 플레이어가 같은 값을 넣어야 파도가 같아진다 (WeatherSystem 이 맞춰 준다).
        /// </summary>
        public void SetWeatherScale(float steepness, float wavelength)
        {
            if (Mathf.Approximately(steepness, steepnessScale) && Mathf.Approximately(wavelength, wavelengthScale))
            {
                return;
            }
            steepnessScale = Mathf.Max(0f, steepness);
            wavelengthScale = Mathf.Max(0.1f, wavelength);
            ApplySettings();
        }

        /// <summary>설정 에셋의 값을 셰이더에 다시 넘긴다. 런타임에 파도를 바꾼 뒤 호출한다.</summary>
        public void ApplySettings()
        {
            Array.Clear(waves, 0, waves.Length);
            waveCount = 0;
            seaLevel = 0f;

            if (settings != null)
            {
                var source = settings.Waves;
                waveCount = Mathf.Min(source.Count, OceanSettings.MaxWaves);
                float total = 0f;
                for (int i = 0; i < waveCount; i++)
                {
                    total += source[i].steepness * steepnessScale;
                }
                float clamp = total > MaxTotalSteepness ? MaxTotalSteepness / total : 1f;
                for (int i = 0; i < waveCount; i++)
                {
                    Vector4 w = source[i].ToVector();
                    w.z *= steepnessScale * clamp;
                    w.w *= wavelengthScale;
                    waves[i] = w;
                }
                seaLevel = settings.SeaLevel;
            }

            Shader.SetGlobalVectorArray(WavesId, waves);
            Shader.SetGlobalFloat(WaveCountId, waveCount);
            Shader.SetGlobalFloat(SeaLevelId, seaLevel);
        }

        void UpdateTime()
        {
            CurrentTime = Application.isPlaying ? (float)TimeSource() : 0f;
            Shader.SetGlobalFloat(TimeId, CurrentTime);
        }

        /// <summary>월드 위치 바로 위·아래의 수면 높이 (월드 y).</summary>
        public float GetWaterHeight(Vector3 worldPosition)
        {
            return seaLevel + GerstnerMath.HeightAt(ActiveWaves, new Vector2(worldPosition.x, worldPosition.z), CurrentTime);
        }
    }
}
