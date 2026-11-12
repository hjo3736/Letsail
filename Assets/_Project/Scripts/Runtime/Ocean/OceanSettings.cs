using System.Collections.Generic;
using UnityEngine;

namespace Letsail.Ocean
{
    [CreateAssetMenu(menuName = "Letsail/Ocean Settings", fileName = "OceanSettings")]
    public sealed class OceanSettings : ScriptableObject
    {
        /// <summary>셰이더 배열 크기와 같아야 한다 (OceanWaves.hlsl 의 LETSAIL_MAX_WAVES).</summary>
        public const int MaxWaves = 4;

        [SerializeField]
        GerstnerWave[] waves =
        {
            new GerstnerWave(0f, 0.12f, 30f),
            new GerstnerWave(35f, 0.10f, 18f),
            new GerstnerWave(-25f, 0.08f, 11f),
            new GerstnerWave(70f, 0.06f, 6f),
        };

        [SerializeField, Tooltip("파도가 없을 때의 수면 높이 (m)")]
        float seaLevel;

        public IReadOnlyList<GerstnerWave> Waves => waves;
        public float SeaLevel => seaLevel;

        void OnValidate()
        {
            if (waves != null && waves.Length > MaxWaves)
            {
                Debug.LogWarning($"파도는 최대 {MaxWaves}개까지 쓸 수 있습니다. 나머지는 무시됩니다.", this);
            }
        }
    }
}
