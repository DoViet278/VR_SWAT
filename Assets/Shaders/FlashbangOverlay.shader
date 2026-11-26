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

        // Ảnh chụp lại khung hình ngay lúc nổ. FlashbangReceiver render một camera phụ vào
        // RenderTexture rồi gán vào đây, nên không có ReadPixels, không kéo pixel về CPU.
        _AfterImage ("Vệt võng mạc", 2D) = "black" {}

        // Độ đậm của vệt, do script điều khiển theo thời gian.
        _AfterImageAlpha ("Độ đậm vệt", Range(0, 1)) = 0

        // 1 nếu RenderTexture bị lật dọc trên nền đồ họa đang chạy.
        _AfterImageFlip ("Lật dọc vệt", Range(0, 1)) = 0
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
                half _AfterImageAlpha;
                half _AfterImageFlip;
            CBUFFER_END

            TEXTURE2D(_AfterImage);
            SAMPLER(sampler_AfterImage);

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

                half aFlash = _Color.a;
                half aAfter = _AfterImageAlpha;

                if (aAfter <= 0.0001h)
                    return half4(_Color.rgb, aFlash);

                // Lấy UV theo vị trí pixel trên màn chứ không theo UV của quad: quad phủ ~174 độ
                // còn ảnh chụp chỉ bằng đúng FOV của mắt, dán theo UV quad sẽ lệch tỉ lệ.
                // Ở Single Pass Instanced hàm này trả về UV riêng của từng mắt, nên hai mắt cùng
                // nhìn một vệt mono - đúng với võng mạc, vệt đọng không có thị sai.
                float2 uv = GetNormalizedScreenSpaceUV(input.positionCS);
                uv.y = lerp(uv.y, 1.0 - uv.y, _AfterImageFlip);

                half3 ghost = SAMPLE_TEXTURE2D(_AfterImage, sampler_AfterImage, uv).rgb;

                // Trộn có trọng số: màn trắng và vệt cùng góp vào một lớp phủ duy nhất.
                half total = saturate(aFlash + aAfter);
                half3 rgb = (aFlash * _Color.rgb + aAfter * ghost) / max(total, 0.0001h);

                return half4(rgb, total);
            }
            ENDHLSL
        }
    }

    Fallback "Universal Render Pipeline/Unlit"
}
