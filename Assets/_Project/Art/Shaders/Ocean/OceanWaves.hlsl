#ifndef LETSAIL_OCEAN_WAVES_INCLUDED
#define LETSAIL_OCEAN_WAVES_INCLUDED

// Scripts/Runtime/Ocean/GerstnerMath.cs 와 같은 공식이어야 한다.
// 값은 OceanSystem 이 전역 셰이더 변수로 넘긴다.

#define LETSAIL_MAX_WAVES 4

float4 _LetsailWaves[LETSAIL_MAX_WAVES]; // (방향 x, 방향 z, 뾰족함, 파장)
float _LetsailWaveCount;
float _LetsailOceanTime;
float _LetsailSeaLevel;

static const float LETSAIL_GRAVITY = 9.81;
static const float LETSAIL_TWO_PI = 6.28318530718;

float3 LetsailGerstnerDisplacement(float2 origin, float time)
{
    float3 d = 0;
    int count = (int)_LetsailWaveCount;
    [unroll]
    for (int i = 0; i < LETSAIL_MAX_WAVES; i++)
    {
        if (i >= count)
        {
            break;
        }
        float4 w = _LetsailWaves[i];
        float k = LETSAIL_TWO_PI / w.w;
        float c = sqrt(LETSAIL_GRAVITY / k);
        float f = k * (dot(w.xy, origin) - c * time);
        float a = w.z / k;
        float cs = cos(f);
        d.x += w.x * a * cs;
        d.y += a * sin(f);
        d.z += w.y * a * cs;
    }
    return d;
}

#endif
