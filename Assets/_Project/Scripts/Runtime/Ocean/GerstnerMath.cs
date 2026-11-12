using System;
using UnityEngine;

namespace Letsail.Ocean
{
    /// <summary>
    /// Gerstner 파도 계산. Art/Shaders/OceanWaves.hlsl 과 같은 공식을 써야
    /// 화면의 파도와 부력 계산이 일치한다. 한쪽을 고치면 다른 쪽도 고칠 것.
    /// </summary>
    public static class GerstnerMath
    {
        public const float Gravity = 9.81f;
        const float TwoPi = 2f * Mathf.PI;

        /// <summary>
        /// 원래 위치 <paramref name="origin"/>(xz)에 있던 수면 점이 시간 <paramref name="time"/>에 얼마나 움직이는지.
        /// </summary>
        /// <param name="waves">(방향 x, 방향 z, 뾰족함, 파장) 목록</param>
        public static Vector3 Displacement(ReadOnlySpan<Vector4> waves, Vector2 origin, float time)
        {
            Vector3 d = default;
            foreach (var w in waves)
            {
                float k = TwoPi / w.w;
                float c = Mathf.Sqrt(Gravity / k);
                float f = k * (w.x * origin.x + w.y * origin.y - c * time);
                float a = w.z / k;
                float cos = Mathf.Cos(f);
                d.x += w.x * a * cos;
                d.y += a * Mathf.Sin(f);
                d.z += w.y * a * cos;
            }
            return d;
        }

        /// <summary>
        /// 월드 위치 <paramref name="position"/>(xz) 바로 위·아래의 수면 높이 (해수면 기준).
        /// Gerstner 파도는 수면 점을 옆으로도 밀기 때문에, 그 자리에 도착하는 원래 점을 반복해서 찾는다.
        /// </summary>
        public static float HeightAt(ReadOnlySpan<Vector4> waves, Vector2 position, float time, int iterations = 4)
        {
            Vector2 origin = position;
            for (int i = 0; i < iterations; i++)
            {
                Vector3 d = Displacement(waves, origin, time);
                origin = position - new Vector2(d.x, d.z);
            }
            return Displacement(waves, origin, time).y;
        }
    }
}
