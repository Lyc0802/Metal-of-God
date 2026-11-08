Shader "Hidden/MetalOfGod/TinSurfaceNightVision"
{
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" }
        Pass
        {
            Name "TinSurfaceRelighting"
            ZWrite Off
            ZTest Always
            Cull Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/GBufferCommon.hlsl"

            TEXTURE2D_X(_TinAlbedo);
            TEXTURE2D_X(_TinSpecular);
            TEXTURE2D_X(_TinNormals);
            TEXTURE2D_X_FLOAT(_TinDepth);
            float4 _TinLightDirection;
            half4 _TinLightColor;
            // intensity, back-face illumination floor, retained shadow contrast, strength
            float4 _TinSettings;

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float2 uv = input.texcoord;
                half4 original = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv);
                float depth = SAMPLE_TEXTURE2D_X(_TinDepth, sampler_PointClamp, uv).r;

                #if UNITY_REVERSED_Z
                    if (depth <= 0.000001) return original;
                #else
                    if (depth >= 0.999999) return original;
                #endif

                half4 base = SAMPLE_TEXTURE2D_X(_TinAlbedo, sampler_PointClamp, uv);
                half4 spec = SAMPLE_TEXTURE2D_X(_TinSpecular, sampler_PointClamp, uv);
                half3 n = UnpackGBufferNormal(SAMPLE_TEXTURE2D_X(_TinNormals, sampler_PointClamp, uv).xyz);
                // Unlit / sky pixels are never lifted.
                if (dot(n, n) < .1) return original;
                n = normalize(n);

                float reflectivity = spec.r;
                uint flags = UnpackGBufferMaterialFlags(base.a);
                if ((flags & kMaterialFlagSpecularSetup) != 0)
                {
                    reflectivity = max(spec.r, max(spec.g, spec.b));
                }

                // Same diffuse convention as URP's Lit BRDF; no specular bloom or additive exposure.
                half3 diffuse = base.rgb * (1 - saturate(reflectivity));
                float shading = max(_TinSettings.y, saturate(dot(n, normalize(_TinLightDirection.xyz))));
                half3 reference = diffuse * _TinLightColor.rgb * shading * _TinSettings.x * lerp(.65, 1, saturate(spec.a));
                float currentLuma = Luminance(original.rgb);
                float referenceLuma = max(Luminance(reference), .00001);
                if (currentLuma >= referenceLuma * .98) return original;

                // The target is bounded by an unshadowed directional light of intensity <= 2.
                // Retain scene shadow variation without needing visible input in total darkness.
                float retained = lerp(1, saturate(currentLuma / referenceLuma), _TinSettings.z);
                half3 target = reference * retained;

                // Preserve already-brighter channels, emissive surfaces, particles and highlights.
                half3 addition = max(original.rgb, target) - original.rgb;
                // Colored lights must not push the combined result above the reference luminance.
                float addedLuma = max(Luminance(addition), .00001);
                float limit = saturate((referenceLuma - currentLuma) / addedLuma);
                return half4(original.rgb + addition * limit * _TinSettings.w, original.a);
            }
            ENDHLSL
        }
    }
}
