// Moodium / Candy Jelly — HLSL reference implementation.
//
// Use this for Editor preview and as the exact math reference. On Vision Pro the project runs
// in RealityKit app mode, where PolySpatial cannot run hand-written shaders: build the
// CandyJelly Shader Graph described in CandyJelly-ShaderGraph-Guide.md (same property names).
//
// Look: a candy-wrapper film at low fill that thickens into a glossy jelly coat.
// Geometry: the room mesh is pushed out along its normals by the jelly thickness, with
// a rolling front, a slow breathing wobble, fingertip dents with a bulging rim, and touch waves.
Shader "Moodium/Candy Jelly"
{
    Properties
    {
        [Header(Driven by CandySpatialMeshTransformationController)]
        _Fill ("Fill (0 film - 1 jelly)", Range(0, 1)) = 0
        _Thickness ("Max Thickness (m)", Range(0, 0.05)) = 0.022
        _LayerDepth ("Layer Depth (outer 1, inner ~0.35)", Range(0, 1)) = 1
        _LayerOpacity ("Layer Opacity", Range(0, 1)) = 1
        _CandyOrigin ("Spread Origin", Vector) = (0, 0, 0, 0)
        _SpreadRadius ("Spread Radius (m)", Float) = 0.4
        _SpreadSoftness ("Spread Softness (m)", Float) = 0.6
        _WobbleImpulse ("Wobble Impulse", Range(0, 1)) = 0
        _PressA ("Press A (xyz, w = depth m)", Vector) = (0, -1000, 0, 0)
        _PressB ("Press B (xyz, w = depth m)", Vector) = (0, -1000, 0, 0)
        _PressRadius ("Press Radius (m)", Float) = 0.12
        _RipplePosition ("Ripple Position", Vector) = (0, -1000, 0, 0)
        _RippleAge ("Ripple Age (s)", Float) = 10
        _RippleStrength ("Ripple Strength", Range(0, 1)) = 0
        _AwakeningFlash ("Awakening Flash", Range(0, 1)) = 0

        [Header(Look)]
        _ColorA ("Candy Pink", Color) = (1, 0.52, 0.74, 1)
        _ColorB ("Candy Violet", Color) = (0.70, 0.52, 1, 1)
        _CoreColor ("Deep Jelly Core", Color) = (0.86, 0.16, 0.48, 1)
        _RimColorA ("Iridescent Rim A", Color) = (0.45, 0.78, 1, 1)
        _RimColorB ("Iridescent Rim B", Color) = (1, 0.55, 0.85, 1)
        _FilmAlpha ("Film Alpha", Range(0, 1)) = 0.12
        _JellyAlpha ("Jelly Alpha", Range(0, 1)) = 0.5
        _RimPower ("Rim Power", Range(0.5, 8)) = 3
        _RimStrength ("Rim Strength", Range(0, 3)) = 1.2
        _InnerGlow ("Inner Glow", Range(0, 1)) = 0.3
        _Smoothness ("Smoothness", Range(0, 1)) = 0.92

        [Header(Motion)]
        _WobbleAmplitude ("Idle Wobble (x thickness)", Range(0, 0.6)) = 0.18
        _WobbleFrequency ("Wobble Frequency (1/m)", Float) = 2.2
        _WobbleSpeed ("Wobble Speed (rad/s)", Float) = 2.4
        _FrontBulge ("Front Bulge (x thickness)", Range(0, 1)) = 0.35
        _RippleSpeed ("Ripple Speed (m/s)", Float) = 0.6
        _RippleWidth ("Ripple Width (m)", Float) = 0.08

        [Header(Sugar sparkle)]
        _SparkleMask ("Sparkle Mask", 2D) = "black" {}
        _SparkleTiling ("Sparkle World Tiling", Float) = 1.35
        _SparkleStrength ("Sparkle Strength", Range(0, 3)) = 0.5
    }

    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" "RenderPipeline"="UniversalPipeline" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Back

        Pass
        {
            Name "CandyJelly"
            Tags { "LightMode"="UniversalForward" }
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D(_SparkleMask);
            SAMPLER(sampler_SparkleMask);

            CBUFFER_START(UnityPerMaterial)
                float _Fill;
                float _Thickness;
                float _LayerDepth;
                float _LayerOpacity;
                float4 _CandyOrigin;
                float _SpreadRadius;
                float _SpreadSoftness;
                float _WobbleImpulse;
                float4 _PressA;
                float4 _PressB;
                float _PressRadius;
                float4 _RipplePosition;
                float _RippleAge;
                float _RippleStrength;
                float _AwakeningFlash;
                float4 _ColorA;
                float4 _ColorB;
                float4 _CoreColor;
                float4 _RimColorA;
                float4 _RimColorB;
                float _FilmAlpha;
                float _JellyAlpha;
                float _RimPower;
                float _RimStrength;
                float _InnerGlow;
                float _Smoothness;
                float _WobbleAmplitude;
                float _WobbleFrequency;
                float _WobbleSpeed;
                float _FrontBulge;
                float _RippleSpeed;
                float _RippleWidth;
                float4 _SparkleMask_ST;
                float _SparkleTiling;
                float _SparkleStrength;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float3 viewWS : TEXCOORD2;
                float3 restWS : TEXCOORD3;
            };

            // 1 inside the jelly, 0 outside, with a soft front of width _SpreadSoftness.
            float SpreadMask(float3 wp)
            {
                float d = distance(wp, _CandyOrigin.xyz);
                return 1.0 - smoothstep(_SpreadRadius - _SpreadSoftness, _SpreadRadius, d);
            }

            // 0..1 how strongly a fingertip presses here.
            float PressFalloff(float3 wp, float4 press)
            {
                float k = saturate(1.0 - distance(wp, press.xyz) / max(_PressRadius, 0.0001));
                return k * k * (3.0 - 2.0 * k);
            }

            // Signed offset along the normal: a dent in the middle and a raised rim around it
            // (the displaced jelly). Negative press.w (spring overshoot) flips it into a bulge.
            float PressOffset(float3 wp, float4 press)
            {
                float d = distance(wp, press.xyz);
                float r = max(_PressRadius, 0.0001);
                float ringX = (d - r * 0.95) / (r * 0.25);
                float ring = exp(-ringX * ringX);
                return -press.w * PressFalloff(wp, press) + press.w * 0.35 * ring;
            }

            float JellyOffset(float3 wp)
            {
                float t = _Time.y;
                float spread = SpreadMask(wp);
                float baseThickness = _Thickness * _Fill * spread;

                // Rolling front: the edge of the advancing jelly is a little fatter.
                float front = saturate(spread * (1.0 - spread) * 4.0);
                float frontBulge = front * _Thickness * _FrontBulge * saturate(_Fill * 3.0);

                // Slow breathing wobble; a bag squeeze adds _WobbleImpulse for a brief quiver.
                float w1 = sin(dot(wp, float3(1.3, 0.7, 1.1)) * _WobbleFrequency + t * _WobbleSpeed);
                float w2 = sin(dot(wp, float3(-0.8, 1.2, 0.6)) * _WobbleFrequency * 0.73 + t * _WobbleSpeed * 1.31);
                float quiver = sin(t * 14.0 + dot(wp, float3(3.1, 2.3, 2.7)));
                float wobble = baseThickness * (_WobbleAmplitude * w1 * w2 + _WobbleImpulse * 0.6 * quiver);

                // Fingertip dents can never push the surface below the real wall/table.
                float press = PressOffset(wp, _PressA) + PressOffset(wp, _PressB);
                press = max(press, -baseThickness * 0.95) * spread;

                // Touch ripple: a travelling ring.
                float rippleRadius = _RippleAge * _RippleSpeed;
                float rd = distance(wp, _RipplePosition.xyz) - rippleRadius;
                float rippleEnvelope = exp(-(rd * rd) / max(_RippleWidth * _RippleWidth, 0.00001));
                float ripple = sin(rd * 40.0) * rippleEnvelope * _RippleStrength * baseThickness * 0.5;

                return (baseThickness + frontBulge + wobble + press + ripple) * _LayerDepth;
            }

            Varyings Vert(Attributes input)
            {
                Varyings output;
                float3 restWS = TransformObjectToWorld(input.positionOS.xyz);
                float3 normalWS = TransformObjectToWorldNormal(input.normalOS);
                float3 displacedWS = restWS + normalWS * JellyOffset(restWS);
                output.positionCS = TransformWorldToHClip(displacedWS);
                output.positionWS = displacedWS;
                output.restWS = restWS;
                output.normalWS = normalWS;
                output.viewWS = GetWorldSpaceViewDir(displacedWS);
                return output;
            }

            float SparkleTriplanar(float3 wp, float3 n)
            {
                float3 w = pow(abs(n), 5.0);
                w /= max(w.x + w.y + w.z, 0.001);
                float s = max(_SparkleTiling, 0.01);
                float x = SAMPLE_TEXTURE2D(_SparkleMask, sampler_SparkleMask, wp.zy * s).r;
                float y = SAMPLE_TEXTURE2D(_SparkleMask, sampler_SparkleMask, wp.xz * s).r;
                float z = SAMPLE_TEXTURE2D(_SparkleMask, sampler_SparkleMask, wp.xy * s).r;
                return x * w.x + y * w.y + z * w.z;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float t = _Time.y;
                float3 n = normalize(input.normalWS);
                float3 v = normalize(input.viewWS);
                float spread = SpreadMask(input.restWS);
                float pressFall = max(PressFalloff(input.restWS, _PressA) * step(0.0, _PressA.w),
                                      PressFalloff(input.restWS, _PressB) * step(0.0, _PressB.w));
                float thick01 = saturate(_Fill * spread * (1.0 - pressFall * 0.8));
                float outer = smoothstep(0.5, 1.0, _LayerDepth);

                float fresnel = pow(1.0 - saturate(dot(n, v)), _RimPower);

                // Body: pink -> violet with height, deepening toward the jelly core as it thickens.
                float height01 = saturate(input.restWS.y * 0.5 + 0.5);
                float3 body = lerp(_ColorA.rgb, _ColorB.rgb, height01);
                body = lerp(body, _CoreColor.rgb, thick01 * 0.5 * (1.0 - fresnel));

                // Candy-wrapper iridescence on the rim, strongest on the outer layer.
                float phase = 0.5 + 0.5 * sin(6.2832 * (fresnel * 1.2 + dot(n, float3(0.3, 0.7, 0.2)) * 0.5 + t * 0.03));
                float3 iridescent = lerp(_RimColorA.rgb, _RimColorB.rgb, phase);

                // Fake subsurface: light seems to glow from inside the thick jelly.
                float3 innerGlow = (1.0 - fresnel) * thick01 * _InnerGlow * lerp(_ColorA.rgb, float3(1, 0.9, 0.95), 0.5);

                float sparkle = SparkleTriplanar(input.restWS, n) *
                    pow(saturate(sin(t * 3.1 + dot(floor(input.restWS * 5.0), float3(12.9898, 78.233, 37.719))) * 0.5 + 0.5), 5.0) *
                    _SparkleStrength * spread * outer;

                // Specular highlight from the main light gives the glossy jelly read in the Editor.
                // On device, RealityKit's own environment lighting provides this from the Lit target.
                Light mainLight = GetMainLight();
                float3 h = normalize(mainLight.direction + v);
                float specular = pow(saturate(dot(n, h)), lerp(32.0, 160.0, _Smoothness)) * _Smoothness * outer;

                float3 color = body
                    + iridescent * fresnel * _RimStrength * (0.4 + 0.6 * thick01) * outer
                    + innerGlow
                    + sparkle * float3(1.0, 0.85, 1.0)
                    + specular * mainLight.color;
                color = lerp(color, float3(1.0, 0.75, 0.95), _AwakeningFlash * 0.35);

                float alpha = spread * lerp(_FilmAlpha, _JellyAlpha, thick01) * (0.65 + 0.35 * fresnel)
                    + fresnel * 0.15 * spread * outer
                    + sparkle * 0.1
                    + specular * 0.3 * spread;
                return half4(color, saturate(alpha * _LayerOpacity));
            }
            ENDHLSL
        }
    }
}

