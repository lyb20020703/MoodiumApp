Shader "Moodium/Magic Pearl Lit"
{
    Properties
    {
        _BaseColor ("Base Color", Color) = (0.725, 0.565, 0.937, 1)
        _DarkColor ("Dark Color", Color) = (0.463, 0.314, 0.690, 1)
        _WideEdgeColor ("Wide Edge Color", Color) = (1, 0.616, 0.886, 1)
        _NarrowEdgeColor ("Narrow Edge Color", Color) = (0.651, 0.875, 1, 1)
        _Metallic ("Metallic", Range(0,1)) = 0
        _Smoothness ("Smoothness", Range(0,1)) = 0.65
        _WideEdgeStrength ("Wide Edge Strength", Range(0,5)) = 0.28
        _NarrowEdgeStrength ("Narrow Edge Strength", Range(0,8)) = 0.55
        _FresnelWidePower ("Wide Fresnel Power", Range(0.5,8)) = 2
        _FresnelNarrowPower ("Narrow Fresnel Power", Range(1,12)) = 6
        _CenterEmission ("Center Emission", Range(0,1)) = 0.035
        _BreathAmplitude ("Breath Amplitude", Range(0,0.2)) = 0.05
        _BreathSpeed ("Breath Speed", Range(0.05,2)) = 0.24
        _SparkleDensity ("Sparkle Density", Range(0,1)) = 0.045
        _SparkleIntensity ("Sparkle Intensity", Range(0,4)) = 0.7
        _SparkleScale ("Sparkle Scale", Range(1,40)) = 17
        _SparkleSpeed ("Sparkle Speed", Range(0,2)) = 0.18
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #pragma multi_compile_fog
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile_fragment _ _SHADOWS_SOFT

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor, _DarkColor, _WideEdgeColor, _NarrowEdgeColor;
                half _Metallic, _Smoothness, _WideEdgeStrength, _NarrowEdgeStrength;
                half _FresnelWidePower, _FresnelNarrowPower, _CenterEmission;
                half _BreathAmplitude, _BreathSpeed, _SparkleDensity, _SparkleIntensity;
                half _SparkleScale, _SparkleSpeed;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 positionOS : TEXCOORD1;
                half3 normalWS : TEXCOORD2;
                float2 uv : TEXCOORD3;
                half fogFactor : TEXCOORD4;
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                VertexPositionInputs pos = GetVertexPositionInputs(IN.positionOS.xyz);
                VertexNormalInputs normal = GetVertexNormalInputs(IN.normalOS);
                OUT.positionHCS = pos.positionCS;
                OUT.positionWS = pos.positionWS;
                OUT.positionOS = IN.positionOS.xyz;
                OUT.normalWS = NormalizeNormalPerVertex(normal.normalWS);
                OUT.uv = IN.uv;
                OUT.fogFactor = ComputeFogFactor(pos.positionCS.z);
                return OUT;
            }

            half Hash21(float2 p)
            {
                p = frac(p * float2(123.34, 456.21));
                p += dot(p, p + 45.32);
                return frac(p.x * p.y);
            }

            half Hash31(float3 p)
            {
                p = frac(p * 0.1031);
                p += dot(p, p.yzx + 33.33);
                return frac((p.x + p.y) * p.z);
            }

            half3 SparkleColor(float3 positionOS, float2 uv, half3 wideColor, half3 narrowColor)
            {
                float2 stableUV = (dot(uv, uv) > 0.00001) ? uv : positionOS.xy * 0.37 + positionOS.zx * 0.19;
                float2 cell = floor(stableUV * _SparkleScale);
                half cellRandom = Hash21(cell);
                half phase = Hash31(float3(cell, 3.7));
                half twinkle = saturate(sin((_Time.y * _SparkleSpeed + phase * 6.28318)) * 0.5 + 0.5);
                half sparkle = step(1.0 - _SparkleDensity, cellRandom) * smoothstep(0.55, 1.0, twinkle);
                return lerp(wideColor, narrowColor, Hash21(cell + 7.1)) * sparkle * _SparkleIntensity;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                half3 N = NormalizeNormalPerPixel(IN.normalWS);
                half3 V = GetWorldSpaceNormalizeViewDir(IN.positionWS);
                half fresnel = saturate(1.0 - dot(N, V));
                half wide = pow(fresnel, _FresnelWidePower);
                half narrow = pow(fresnel, _FresnelNarrowPower);
                half lowFrequency = sin(dot(IN.positionOS, float3(0.71, 0.43, 0.29)) * 1.7) * 0.5 + 0.5;
                half3 pearlBase = lerp(_DarkColor.rgb, _BaseColor.rgb, saturate(0.52 + lowFrequency * 0.42));
                half breath = 1.0 + sin(_Time.y * _BreathSpeed * 6.28318) * _BreathAmplitude;
                half3 edgeColor = lerp(_WideEdgeColor.rgb, _NarrowEdgeColor.rgb, saturate(lowFrequency * 0.85 + narrow * 0.35));
                half3 emission = pearlBase * _CenterEmission + edgeColor * (wide * _WideEdgeStrength + narrow * _NarrowEdgeStrength) * breath;
                emission += SparkleColor(IN.positionOS, IN.uv, _WideEdgeColor.rgb, _NarrowEdgeColor.rgb);

                InputData inputData = (InputData)0;
                inputData.positionWS = IN.positionWS;
                inputData.normalWS = N;
                inputData.viewDirectionWS = V;
                inputData.shadowCoord = TransformWorldToShadowCoord(IN.positionWS);
                inputData.fogCoord = IN.fogFactor;
                SurfaceData surfaceData = (SurfaceData)0;
                surfaceData.albedo = pearlBase;
                surfaceData.metallic = _Metallic;
                surfaceData.specular = half3(0.5, 0.5, 0.5);
                surfaceData.smoothness = _Smoothness;
                surfaceData.alpha = 1;
                surfaceData.emission = emission;
                half4 color = UniversalFragmentPBR(inputData, surfaceData);
                color.rgb = MixFog(color.rgb, IN.fogFactor);
                return color;
            }
            ENDHLSL
        }
    }
    FallBack "Universal Render Pipeline/Lit"
}
