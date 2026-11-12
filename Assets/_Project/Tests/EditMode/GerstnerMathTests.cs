using Letsail.Ocean;
using NUnit.Framework;
using UnityEngine;

namespace Letsail.Tests
{
    public class GerstnerMathTests
    {
        static readonly Vector4[] Waves =
        {
            new GerstnerWave(0f, 0.12f, 30f).ToVector(),
            new GerstnerWave(35f, 0.10f, 18f).ToVector(),
            new GerstnerWave(-25f, 0.08f, 11f).ToVector(),
            new GerstnerWave(70f, 0.06f, 6f).ToVector(),
        };

        [Test]
        public void HeightAt_MatchesDisplacedSurfacePoint()
        {
            var random = new System.Random(1234);
            for (int i = 0; i < 200; i++)
            {
                var origin = new Vector2((float)random.NextDouble() * 400f - 200f, (float)random.NextDouble() * 400f - 200f);
                float time = (float)random.NextDouble() * 1800f;

                Vector3 d = GerstnerMath.Displacement(Waves, origin, time);
                var surfacePoint = new Vector2(origin.x + d.x, origin.y + d.z);

                float height = GerstnerMath.HeightAt(Waves, surfacePoint, time);
                Assert.That(height, Is.EqualTo(d.y).Within(0.02f), $"origin {origin}, time {time}");
            }
        }

        [Test]
        public void Displacement_IsZeroWithoutWaves()
        {
            Vector3 d = GerstnerMath.Displacement(System.ReadOnlySpan<Vector4>.Empty, new Vector2(3f, 5f), 10f);
            Assert.That(d, Is.EqualTo(Vector3.zero));
        }
    }
}
