using System;
using UnityEngine;

namespace Letsail.Ocean
{
    /// <summary>
    /// 하나의 Gerstner 파도 설정. 여러 개를 겹쳐 바다 표면을 만든다.
    /// </summary>
    [Serializable]
    public struct GerstnerWave
    {
        [Tooltip("파도가 진행하는 방향 (도). 0 = +Z, 90 = +X")]
        public float directionDegrees;

        [Tooltip("파도의 뾰족함. 모든 파도의 합이 1을 넘으면 물결이 꼬인다.")]
        [Range(0f, 1f)]
        public float steepness;

        [Tooltip("파장 (m). 길수록 크고 느린 파도가 된다.")]
        [Min(0.1f)]
        public float wavelength;

        public GerstnerWave(float directionDegrees, float steepness, float wavelength)
        {
            this.directionDegrees = directionDegrees;
            this.steepness = steepness;
            this.wavelength = wavelength;
        }

        /// <summary>셰이더와 <see cref="GerstnerMath"/>가 쓰는 형식: (방향 x, 방향 z, 뾰족함, 파장).</summary>
        public Vector4 ToVector()
        {
            float rad = directionDegrees * Mathf.Deg2Rad;
            return new Vector4(Mathf.Sin(rad), Mathf.Cos(rad), steepness, Mathf.Max(0.1f, wavelength));
        }
    }
}
