// 로우폴리 카툰풍 바다. 꼭짓점을 Gerstner 파도로 움직이고, 면마다 각진 음영을 준다.
Shader "Letsail/Ocean"
{
    Properties
    {
        _DeepColor ("Deep Color", Color) = (0.05, 0.32, 0.55, 1)
        _ShallowColor ("Shallow Color", Color) = (0.22, 0.72, 0.82, 1)
        _CrestColor ("Crest Color", Color) = (0.92, 0.98, 1, 1)
        _ColorHeightRange ("Color Height Range (m)", Float) = 1.2
        _CrestHeight ("Crest Start Height (m)", Float) = 0.55
        _CrestSoftness ("Crest Softness (m)", Range(0.01, 1)) = 0.15
        _FresnelColor ("Fresnel Color", Color) = (0.6, 0.85, 0.95, 1)
        _FresnelPower ("Fresnel Power", Range(0.5, 8)) = 4
        _ShadeSteps ("Light Steps (0 = smooth)", Range(0, 6)) = 3
        _UnderSurfaceColor ("Under Surface Color (seen from below)", Color) = (0.35, 0.75, 0.85, 1)
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            // 물속에서 올려다볼 때도 수면이 보이게 양면으로 그린다.
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "OceanWaves.hlsl"
            #include "HullWaterMask.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _DeepColor;
                half4 _ShallowColor;
                half4 _CrestColor;
                float _ColorHeightRange;
                float _CrestHeight;
                float _CrestSoftness;
                half4 _FresnelColor;
                float _FresnelPower;
                float _ShadeSteps;
                half4 _UnderSurfaceColor;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                // x: 파도 세기 (1 = 그대로, 0 = 평평). 수평선까지 이어 붙인 바깥 고리는 칸이 커서 파도가 깨지므로 평평하게 둔다.
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float waveHeight : TEXCOORD1;
                float fogFactor : TEXCOORD2;
            };

            Varyings vert(Attributes input)
            {
                Varyings output;
                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                float3 d = LetsailGerstnerDisplacement(positionWS.xz, _LetsailOceanTime) * saturate(input.uv.x);
                positionWS.xz += d.xz;
                positionWS.y = _LetsailSeaLevel + d.y;

                output.positionWS = positionWS;
                output.waveHeight = d.y;
                output.positionCS = TransformWorldToHClip(positionWS);
                output.fogFactor = ComputeFogFactor(output.positionCS.z);
                return output;
            }

            half4 frag(Varyings input, bool isFrontFace : SV_IsFrontFace) : SV_Target
            {
                // 배 안쪽 공간에 들어온 수면은 그리지 않는다 (선창이 물에 잠겨 보이지 않게).
                if (LetsailInsideHull(input.positionWS))
                {
                    discard;
                }

                // 물속에서 올려다본 수면: 밝은 청록색으로 하늘빛이 비치는 느낌만 낸다.
                if (!isFrontFace)
                {
                    float shimmer = 0.85 + 0.15 * saturate(input.waveHeight / _ColorHeightRange * 0.5 + 0.5);
                    return half4(MixFog(_UnderSurfaceColor.rgb * shimmer, input.fogFactor), 1);
                }

                // 면 단위 법선: 삼각형마다 하나의 방향이라 각진 로우폴리 음영이 된다.
                float3 normalWS = normalize(cross(ddy(input.positionWS), ddx(input.positionWS)));
                normalWS = normalWS.y < 0 ? -normalWS : normalWS;

                float h = saturate(input.waveHeight / _ColorHeightRange * 0.5 + 0.5);
                half3 color = lerp(_DeepColor.rgb, _ShallowColor.rgb, h);
                float crest = smoothstep(_CrestHeight - _CrestSoftness, _CrestHeight + _CrestSoftness, input.waveHeight);
                color = lerp(color, _CrestColor.rgb, crest);

                Light mainLight = GetMainLight(TransformWorldToShadowCoord(input.positionWS));
                float ndl = saturate(dot(normalWS, mainLight.direction));
                if (_ShadeSteps >= 1)
                {
                    ndl = ceil(ndl * _ShadeSteps) / _ShadeSteps;
                }
                half3 lighting = mainLight.color * (ndl * mainLight.shadowAttenuation) + SampleSH(normalWS);

                float3 viewDirWS = normalize(GetWorldSpaceViewDir(input.positionWS));
                float fresnel = pow(1.0 - saturate(dot(normalWS, viewDirWS)), _FresnelPower);

                half3 finalColor = color * lighting;
                finalColor = lerp(finalColor, _FresnelColor.rgb, fresnel * 0.6);
                finalColor = MixFog(finalColor, input.fogFactor);
                return half4(finalColor, 1);
            }
            ENDHLSL
        }
    }

    FallBack Off
}
