#ifndef LETSAIL_HULL_WATER_MASK_INCLUDED
#define LETSAIL_HULL_WATER_MASK_INCLUDED

// 배 안쪽(선창)에 바다 수면이 보이지 않게 한다.
// Scripts/Runtime/Ocean/HullWaterMask.cs 가 전역 변수로 넘긴다.
// 마스크 텍스처는 배 바닥을 위에서 본 칸마다 배 안쪽 공간의 (아래 높이, 위 높이)를 담는다 (마스크 메시 기준 y).

TEXTURE2D(_LetsailHullMask);
SamplerState letsail_point_clamp_sampler;
float4x4 _LetsailHullWorldToMask; // 월드 → (u, 마스크 기준 y, v)
float _LetsailHullMaskEnabled;

bool LetsailInsideHull(float3 positionWS)
{
    if (_LetsailHullMaskEnabled < 0.5)
    {
        return false;
    }
    float3 m = mul(_LetsailHullWorldToMask, float4(positionWS, 1.0)).xyz;
    if (m.x < 0.0 || m.x > 1.0 || m.z < 0.0 || m.z > 1.0)
    {
        return false;
    }
    float2 span = SAMPLE_TEXTURE2D_LOD(_LetsailHullMask, letsail_point_clamp_sampler, m.xz, 0).rg;
    return m.y > span.x && m.y < span.y;
}

#endif
