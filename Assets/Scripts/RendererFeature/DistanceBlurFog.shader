// Used by DistanceBlurFogFeature. Pass 0/1: separable gaussian blur, pass 2: depth-based composite.
Shader "Hidden/DistanceBlurFog"
{
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" }
        ZWrite Off ZTest Always Cull Off Blend Off

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

        TEXTURE2D_X(_DistanceBlurTex);
        float4 _DistanceBlurTexelSize;   // 1/w, 1/h, w, h of _BlitTexture
        float4 _DistanceBlurParams;      // start distance, 1/fade distance, max blur, fog strength
        float4 _DistanceBlurFogColor;
        float _DistanceBlurRadius;
        float _DistanceBlurAffectSky;

        half4 SampleSource(float2 uv)
        {
            return SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv);
        }

        // 9-tap gaussian using 5 bilinear fetches.
        half4 BlurDirection(float2 uv, float2 direction)
        {
            float2 offset = direction * _DistanceBlurTexelSize.xy * _DistanceBlurRadius;
            half4 color = SampleSource(uv) * 0.2270270;
            color += (SampleSource(uv + offset * 1.3846153) + SampleSource(uv - offset * 1.3846153)) * 0.3162162;
            color += (SampleSource(uv + offset * 3.2307692) + SampleSource(uv - offset * 3.2307692)) * 0.0702703;
            return color;
        }

        half4 FragHorizontal(Varyings input) : SV_Target
        {
            UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
            return BlurDirection(input.texcoord, float2(1, 0));
        }

        half4 FragVertical(Varyings input) : SV_Target
        {
            UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
            return BlurDirection(input.texcoord, float2(0, 1));
        }

        half4 FragComposite(Varyings input) : SV_Target
        {
            UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
            float2 uv = input.texcoord;
            half4 source = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_PointClamp, uv);
            half3 blurred = SAMPLE_TEXTURE2D_X(_DistanceBlurTex, sampler_LinearClamp, uv).rgb;

            float rawDepth = SampleSceneDepth(uv);
            #if UNITY_REVERSED_Z
                bool isSky = rawDepth <= 0.00001;
                float orthoDepth = 1.0 - rawDepth;
            #else
                bool isSky = rawDepth >= 0.99999;
                float orthoDepth = rawDepth;
            #endif
            float eyeDepth = unity_OrthoParams.w > 0.5
                ? lerp(_ProjectionParams.y, _ProjectionParams.z, orthoDepth)
                : LinearEyeDepth(rawDepth, _ZBufferParams);

            float t = saturate((eyeDepth - _DistanceBlurParams.x) * _DistanceBlurParams.y);
            t = t * t * (3.0 - 2.0 * t);
            if (isSky) t *= _DistanceBlurAffectSky;

            half3 color = lerp(source.rgb, blurred, t * _DistanceBlurParams.z);
            color = lerp(color, _DistanceBlurFogColor.rgb, t * _DistanceBlurParams.w);
            return half4(color, source.a);
        }
        ENDHLSL

        Pass
        {
            Name "DistanceBlurHorizontal"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FragHorizontal
            ENDHLSL
        }

        Pass
        {
            Name "DistanceBlurVertical"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FragVertical
            ENDHLSL
        }

        Pass
        {
            Name "DistanceBlurComposite"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FragComposite
            ENDHLSL
        }
    }
}
