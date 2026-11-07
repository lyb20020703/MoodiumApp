Shader "Moodium/Nature Panorama Feather"
{
    Properties
    {
        _BaseMap ("Panorama Texture", 2D) = "white" {}
        _BaseColor ("Tint", Color) = (1, 1, 1, 1)
        _FeatherBaseY ("Cut Edge World Y", Float) = 0
        _FeatherHeight ("Edge Feather Height", Float) = 0.8
    }

    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Background" "RenderPipeline"="UniversalPipeline" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Front

        Pass
        {
            Name "NaturePanoramaFeather"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor;
                float _FeatherBaseY;
                float _FeatherHeight;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float worldY : TEXCOORD1;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                VertexPositionInputs position = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionHCS = position.positionCS;
                output.uv = input.uv;
                output.worldY = position.positionWS.y;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                half4 color = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv) * _BaseColor;
                float edgeAlpha = smoothstep(_FeatherBaseY, _FeatherBaseY + max(_FeatherHeight, 0.0001), input.worldY);
                color.a *= edgeAlpha;
                return color;
            }
            ENDHLSL
        }
    }
}
