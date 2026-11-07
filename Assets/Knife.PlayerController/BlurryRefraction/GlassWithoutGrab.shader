Shader "FX/Glass/Stained BumpDistort URP"
{
    Properties
    {
        _BumpAmt ("Distortion", Range(0, 640)) = 10
        _TintAmt ("Tint Amount", Range(0, 1)) = 0.1

        _MainTex ("Tint Color (RGB)", 2D) = "white" {}
        _Mask ("Blur Mask", 2D) = "white" {}
        _NoiseMask ("Noise Mask", 2D) = "white" {}

        _WorldScale ("World Scale", Float) = 1.0
        _MaskPower ("Blur Mask Power", Float) = 1.0
        _Tint ("Tint", Color) = (1, 1, 1, 1)

        _BumpMap ("Normal Map", 2D) = "bump" {}

        // These are explicit texture inputs, not automatic GrabPass textures.
        _GrabBlurTexture ("Blurred Background", 2D) = "white" {}
        _GrabNotBlurTexture ("Original Background", 2D) = "white" {}
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "Transparent"
            "RenderType" = "Transparent"
        }

        Pass
        {
            Name "StainedGlass"
            Tags { "LightMode" = "UniversalForward" }

            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Back

            HLSLPROGRAM

            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_BumpMap);
            SAMPLER(sampler_BumpMap);

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            TEXTURE2D(_Mask);
            SAMPLER(sampler_Mask);

            TEXTURE2D(_NoiseMask);
            SAMPLER(sampler_NoiseMask);

            TEXTURE2D(_GrabBlurTexture);
            SAMPLER(sampler_GrabBlurTexture);

            TEXTURE2D(_GrabNotBlurTexture);
            SAMPLER(sampler_GrabNotBlurTexture);

            float4 _BumpMap_ST;
            float4 _MainTex_ST;
            float4 _GrabBlurTexture_TexelSize;

            CBUFFER_START(UnityPerMaterial)
                float _BumpAmt;
                half _TintAmt;
                float _WorldScale;
                float _MaskPower;
                float4 _Tint;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float4 screenPos : TEXCOORD0;
                float2 uvBump : TEXCOORD1;
                float2 uvMain : TEXCOORD2;
                float2 uvNoise : TEXCOORD3;
                float3 positionWS : TEXCOORD4;
                half fogFactor : TEXCOORD5;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output = (Varyings)0;

                VertexPositionInputs positionInputs =
                    GetVertexPositionInputs(input.positionOS.xyz);

                output.positionCS = positionInputs.positionCS;
                output.positionWS = positionInputs.positionWS;

                // Screen-space UVs for the two background textures.
                output.screenPos = ComputeScreenPos(output.positionCS);

                output.uvBump = TRANSFORM_TEX(input.uv, _BumpMap);
                output.uvMain = TRANSFORM_TEX(input.uv, _MainTex);

                // Preserve the original world-XZ noise mapping.
                output.uvNoise =
                    input.uv * _MainTex_ST.xy
                    + _MainTex_ST.zw
                    + positionInputs.positionWS.xz * _WorldScale;

                output.fogFactor = ComputeFogFactor(output.positionCS.z);

                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                // Convert clip-derived screen coordinates to UVs.
                float2 screenUV =
                    input.screenPos.xy / input.screenPos.w;

                // Keep UVs inside the valid texture range.
                screenUV = UnityStereoTransformScreenSpaceTex(screenUV);

                // Preserve the original normal-map distortion.
                half2 bump = UnpackNormal(
                    SAMPLE_TEXTURE2D(
                        _BumpMap,
                        sampler_BumpMap,
                        input.uvBump
                    )
                ).rg;

                float2 offset =
                    (float2)bump
                    * _BumpAmt
                    * _GrabBlurTexture_TexelSize.xy;

                float2 distortedUV = screenUV + offset;

                // Original and blurred background samples.
                half4 blurredColor = SAMPLE_TEXTURE2D(
                    _GrabBlurTexture,
                    sampler_GrabBlurTexture,
                    distortedUV
                );

                half4 originalColor = SAMPLE_TEXTURE2D(
                    _GrabNotBlurTexture,
                    sampler_GrabNotBlurTexture,
                    distortedUV
                );

                // Tint and masks.
                half4 tintTexture = SAMPLE_TEXTURE2D(
                    _MainTex,
                    sampler_MainTex,
                    input.uvMain
                );

                half noise = SAMPLE_TEXTURE2D(
                    _NoiseMask,
                    sampler_NoiseMask,
                    input.uvNoise
                ).r;

                half mask = SAMPLE_TEXTURE2D(
                    _Mask,
                    sampler_Mask,
                    input.uvMain
                ).r;

                blurredColor *= lerp(
                    half4(1, 1, 1, 1),
                    tintTexture,
                    _TintAmt
                );

                blurredColor *= _Tint;

                half blendMask = pow(
                    saturate(mask * noise),
                    max(_MaskPower, 0.0001)
                );

                half4 color = lerp(
                    originalColor,
                    blurredColor,
                    blendMask
                );

                color.rgb = MixFog(color.rgb, input.fogFactor);

                return color;
            }

            ENDHLSL
        }
    }

    FallBack Off
}