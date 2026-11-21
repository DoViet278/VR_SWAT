// Tấm màn trắng che kín tầm nhìn khi bị flashbang.
//
// Vì sao phải có shader riêng thay vì dùng Canvas Screen Space Overlay: trong VR, Canvas kiểu
// Screen Space không hiện trên kính. Cách chạy được là dán một tấm quad ngay trước mắt, nhưng
// quad đó lại bị tường/súng/tay che mất. Nên shader này ZTest Always + Queue Overlay: vẽ sau
// cùng và bỏ qua chiều sâu, không gì che được nó.
//
// Các macro UNITY_*_STEREO bên dưới là bắt buộc cho VR. Thiếu chúng thì ở chế độ
// Single Pass Instanced tấm màn sẽ lệch hoặc chỉ hiện một mắt.
Shader "VR SWAT/Flashbang Overlay"
{
    Properties
    {
        _Color ("Màu màn", Color) = (1, 1, 1, 1)
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "Queue" = "Overlay"
            "RenderPipeline" = "UniversalPipeline"
            "IgnoreProjector" = "True"
        }

        Pass
        {
            Name "FlashbangOverlay"
            Tags { "LightMode" = "SRPDefaultUnlit" }

            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest Always
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings vert(Attributes input)
            {
                Varyings output = (Varyings)0;

                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                return _Color;
            }
            ENDHLSL
        }
    }

    Fallback "Universal Render Pipeline/Unlit"
}
