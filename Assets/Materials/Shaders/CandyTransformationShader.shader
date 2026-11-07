Shader "Moodium/Candy Transformation Preview"
{
    Properties
    {
        _CandyProgress ("Candy Progress", Range(0, 1)) = 0
        _BaseColor ("Reality Preview Color", Color) = (0.36, 0.39, 0.45, 1)
        _CandyColorA ("Candy Pink", Color) = (1, 0.18, 0.55, 1)
        _CandyColorB ("Candy Violet", Color) = (0.42, 0.18, 1, 1)
        _CandyColorC ("Candy Orange", Color) = (1, 0.45, 0.08, 1)
        _CandyOrigin ("Candy Origin", Vector) = (0, 0, 0, 0)
        _CandyRadius ("Candy Radius", Float) = 2
        _CrystalScale ("Sugar Crystal Scale", Float) = 22
        _Transparency ("Candy Transparency", Range(0.05, 0.9)) = 0.42
        _RimPower ("Fresnel Power", Range(0.5, 8)) = 3
        _RimStrength ("Fresnel Strength", Range(0, 4)) = 1.5
        _SparkleMask ("Candy Sparkle Mask", 2D) = "black" {}
        _SparkleTiling ("Sparkle World Tiling", Float) = 1.35
        _SparkleStrength ("Sparkle Strength", Range(0, 3)) = 0
        _IridescenceStrength ("Holographic Strength", Range(0, 3)) = 0
        _AwakeningFlash ("Candy World Awakening", Range(0, 1)) = 0
        _RipplePosition ("Ripple Position", Vector) = (0, -1000, 0, 0)
        _RippleAge ("Ripple Age", Float) = 10
        _RippleSpeed ("Ripple Speed", Float) = 0.42
        _RippleWidth ("Ripple Width", Float) = 0.045
        _RippleStrength ("Ripple Strength", Range(0, 3)) = 0
        _ShaderTime ("Runtime Time", Float) = 0
        _ShellThickness ("Soft Shell Thickness", Range(0, 0.02)) = 0.006
        _PressRadius ("Press Radius", Range(0.01, 0.3)) = 0.14
        _PressPointA ("Press Point A", Vector) = (0, -1000, 0, 0)
        _PressPointB ("Press Point B", Vector) = (0, -1000, 0, 0)
    }

    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" "RenderPipeline"="UniversalPipeline" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Back

        Pass
        {
            Name "CandyPreview"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_SparkleMask);
            SAMPLER(sampler_SparkleMask);

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float3 viewWS : TEXCOORD2;
            };

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor;
                float4 _CandyColorA;
                float4 _CandyColorB;
                float4 _CandyColorC;
                float4 _CandyOrigin;
                float _CandyProgress;
                float _CandyRadius;
                float _CrystalScale;
                float _Transparency;
                float _RimPower;
                float _RimStrength;
                float _SparkleTiling;
                float _SparkleStrength;
                float _IridescenceStrength;
                float _AwakeningFlash;
                float4 _RipplePosition;
                float _RippleAge;
                float _RippleSpeed;
                float _RippleWidth;
                float _RippleStrength;
                float _ShaderTime;
                float _ShellThickness;
                float _PressRadius;
                float4 _PressPointA;
                float4 _PressPointB;
            CBUFFER_END

            float SoftPress(float3 worldPosition, float4 pressPoint)
            {
                if (pressPoint.w <= 0.00001)
                    return 0.0;
                float t = 1.0 - saturate(distance(worldPosition, pressPoint.xyz) / max(_PressRadius, 0.0001));
                return pressPoint.w * t * t * (3.0 - 2.0 * t);
            }

            float Hash31(float3 p)
            {
                p = frac(p * 0.1031);
                p += dot(p, p.yzx + 33.33);
                return frac((p.x + p.y) * p.z);
            }

            float SampleSparkleTriplanar(float3 worldPosition, float3 normalWS)
            {
                float3 weights = pow(abs(normalWS), 5.0);
                weights /= max(weights.x + weights.y + weights.z, 0.001);
                float scale = max(_SparkleTiling, 0.01);
                float x = SAMPLE_TEXTURE2D(_SparkleMask, sampler_SparkleMask, worldPosition.zy * scale).r;
                float y = SAMPLE_TEXTURE2D(_SparkleMask, sampler_SparkleMask, worldPosition.xz * scale).r;
                float z = SAMPLE_TEXTURE2D(_SparkleMask, sampler_SparkleMask, worldPosition.xy * scale).r;
                return x * weights.x + y * weights.y + z * weights.z;
            }

            Varyings Vert(Attributes input)
            {
                Varyings output;
                float3 worldPositionBeforeDeform = TransformObjectToWorld(input.positionOS.xyz);
                float press = SoftPress(worldPositionBeforeDeform, _PressPointA) +
                    SoftPress(worldPositionBeforeDeform, _PressPointB);
                float3 deformedPositionOS = input.positionOS.xyz + input.normalOS * (_ShellThickness - press);
                VertexPositionInputs position = GetVertexPositionInputs(deformedPositionOS);
                output.positionCS = position.positionCS;
                output.positionWS = position.positionWS;
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.viewWS = GetWorldSpaceViewDir(position.positionWS);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float3 n = normalize(input.normalWS);
                float3 v = normalize(input.viewWS);
                float distanceMask = 1.0 - saturate(distance(input.positionWS, _CandyOrigin.xyz) / max(_CandyRadius, 0.001));
                // Energy is an absolute master gate. At zero this shader contributes no overlay,
                // even at the interaction origin where distanceMask is one.
                float energyGate = smoothstep(0.2, 1.0, _CandyProgress);
                float candyMask = energyGate * saturate((energyGate * 1.35) + distanceMask - 0.72);

                float verticalGradient = saturate(input.positionWS.y * 0.55 + 0.5);
                float3 candyColor = lerp(_CandyColorA.rgb, _CandyColorB.rgb, verticalGradient);
                candyColor = lerp(candyColor, _CandyColorC.rgb, saturate(sin(input.positionWS.x * 2.3) * 0.18 + 0.12));

                float fresnel = pow(1.0 - saturate(dot(n, v)), _RimPower) * _RimStrength;
                float crystal = step(0.92, Hash31(floor(input.positionWS * _CrystalScale)));
                float holoPhase = frac(fresnel * 0.22 + dot(n, normalize(float3(0.34, 0.71, 0.27))) * 0.28 + _ShaderTime * 0.025);
                float3 holoA = lerp(float3(0.32, 0.55, 1.0), float3(0.94, 0.25, 1.0), saturate(holoPhase * 2.0));
                float3 holoB = lerp(float3(0.94, 0.25, 1.0), float3(1.0, 0.35, 0.68), saturate((holoPhase - 0.5) * 2.0));
                float3 holographic = holoPhase < 0.5 ? holoA : holoB;
                float sparkleMask = SampleSparkleTriplanar(input.positionWS, n);
                float twinkle = pow(saturate(sin(_ShaderTime * 3.1 + Hash31(floor(input.positionWS * 5.0)) * 6.283) * 0.5 + 0.5), 5.0);
                float sparkle = sparkleMask * twinkle * _SparkleStrength;
                float rippleRadius = max(0.0, _RippleAge) * _RippleSpeed;
                float rippleDistance = distance(input.positionWS, _RipplePosition.xyz);
                float ripple = saturate(1.0 - abs(rippleDistance - rippleRadius) / max(_RippleWidth, 0.001)) * _RippleStrength;
                float3 surface = lerp(_BaseColor.rgb, candyColor, candyMask);
                surface += candyMask * (fresnel * lerp(float3(0.35, 0.55, 1), float3(1, 0.25, 0.75), verticalGradient));
                surface += candyMask * crystal * 0.55;
                surface += candyMask * holographic * fresnel * _IridescenceStrength * 0.55;
                surface += candyMask * sparkle * float3(1.0, 0.82, 1.0) * 1.5;
                surface += ripple * float3(0.8, 0.58, 1.0);
                surface = lerp(surface, float3(1.0, 0.72, 0.95), _AwakeningFlash * 0.38);

                // This is an overlay on the reconstructed reality mesh. Zero energy must leave reality untouched.
                float alpha = candyMask * (_Transparency + fresnel * 0.2 + crystal * 0.12 + sparkle * 0.12) + ripple * 0.12;
                return half4(surface, saturate(alpha));
            }
            ENDHLSL
        }
    }
}
