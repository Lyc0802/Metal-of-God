Shader "Hidden/SimpleOutline"
{
    Properties
    {
        _Outlined ("Outlined", Float) = 0
        _OutlineColor ("Outline color", Color) = (1, 1, 1, 1)
        _OutlinePixels ("Outline pixels", Float) = 4
    }
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" }
        Pass
        {
            Name "Full silhouette mask"
            Cull Back ZWrite Off ZTest Always
            ColorMask R
            HLSLPROGRAM
            #pragma vertex MaskVertex
            #pragma fragment MaskFragment
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
                float4 _OutlineColor;
                float _OutlinePixels;
                float _Outlined;
            CBUFFER_END
            struct Attributes { float3 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; };
            Varyings MaskVertex(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS);
                return output;
            }
            half4 MaskFragment(Varyings input) : SV_Target
            {
                clip(_Outlined - 0.5);
                return 1;
            }
            ENDHLSL
        }
        Pass
        {
            Name "Screen space outline"
            Cull Off ZWrite Off ZTest Always
            Blend SrcAlpha OneMinusSrcAlpha
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment OutlineFragment
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
            CBUFFER_START(UnityPerMaterial)
                float4 _OutlineColor;
                float _OutlinePixels;
                float _Outlined;
            CBUFFER_END
            // Round dilation, with an inner ring to keep narrow silhouettes continuous.
            static const float2 Directions[16] = {
                float2(1,0), float2(.92388,.38268), float2(.70711,.70711), float2(.38268,.92388),
                float2(0,1), float2(-.38268,.92388), float2(-.70711,.70711), float2(-.92388,.38268),
                float2(-1,0), float2(-.92388,-.38268), float2(-.70711,-.70711), float2(-.38268,-.92388),
                float2(0,-1), float2(.38268,-.92388), float2(.70711,-.70711), float2(.92388,-.38268)
            };
            half2 Coverage(float2 uv)
            {
                return SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_LinearClamp, uv, 0).rg;
            }
            half4 OutlineFragment(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                half2 center = Coverage(input.texcoord);
                // Full interior pixels cannot contain an exterior outline.
                [branch] if (center.r >= 1.0h) return 0;
                half mask = center.r;
                half expanded = mask;
                half visible = center.g;
                float2 radius = _OutlinePixels * _BlitTexture_TexelSize.xy;
                [unroll] for (int i = 0; i < 16; i++)
                {
                    float2 sampleUV=input.texcoord + Directions[i] * radius;
                    half2 sampleMask = Coverage(sampleUV);
                    expanded = max(expanded, sampleMask.r);
                    visible = max(visible, sampleMask.g);
                    if (i % 2 == 0)
                        expanded = max(expanded, Coverage(input.texcoord + Directions[i] * radius * .5).r);
                }
                return half4(_OutlineColor.rgb, _OutlineColor.a * saturate(expanded - mask) * visible);
            }
            ENDHLSL
        }
        Pass
        {
            Name "Visible mask"
            Cull Back ZWrite Off ZTest LEqual
            ColorMask G
            HLSLPROGRAM
            #pragma vertex MaskVertex
            #pragma fragment MaskFragment
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
                float4 _OutlineColor;
                float _OutlinePixels;
                float _Outlined;
            CBUFFER_END
            struct Attributes { float3 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; };
            Varyings MaskVertex(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS);
                return output;
            }
            half4 MaskFragment(Varyings input) : SV_Target
            {
                clip(_Outlined - 0.5);
                return 1;
            }
            ENDHLSL
        }
        // Bronze uses its own two passes. Existing metal-object passes 0/1/2 above
        // retain the visible-mask test and the _Outlined material property contract.
        Pass
        {
            Name "Bronze X-Ray silhouette"
            Cull Back ZWrite Off ZTest Always
            ColorMask R
            HLSLPROGRAM
            #pragma vertex BronzeMaskVertex
            #pragma fragment BronzeMaskFragment
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
                float4 _OutlineColor;
                float _OutlinePixels;
                float _Outlined;
            CBUFFER_END
            struct Attributes
            {
                float3 positionOS : POSITION;
            };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
            };
            Varyings BronzeMaskVertex(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS);
                return output;
            }
            half4 BronzeMaskFragment(Varyings input) : SV_Target
            {
                return 1;
            }
            ENDHLSL
        }
        Pass
        {
            Name "Bronze X-Ray composite"
            Cull Off ZWrite Off ZTest Always
            Blend SrcAlpha OneMinusSrcAlpha
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment BronzeOutlineFragment
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
            CBUFFER_START(UnityPerMaterial)
                float4 _OutlineColor;
                float _OutlinePixels;
                float _Outlined;
            CBUFFER_END
            static const float2 Directions[16] = {
                float2(1,0), float2(.92388,.38268), float2(.70711,.70711), float2(.38268,.92388),
                float2(0,1), float2(-.38268,.92388), float2(-.70711,.70711), float2(-.92388,.38268),
                float2(-1,0), float2(-.92388,-.38268), float2(-.70711,-.70711), float2(-.38268,-.92388),
                float2(0,-1), float2(.38268,-.92388), float2(.70711,-.70711), float2(.92388,-.38268)
            };
            half Coverage(float2 uv)
            {
                return SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_LinearClamp, uv, 0).r;
            }
            half4 BronzeOutlineFragment(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                half center = Coverage(input.texcoord);
                if (center >= 1.0h) return 0;
                half expanded = center;
                float2 radius = _OutlinePixels * _BlitTexture_TexelSize.xy;
                [unroll] for (int i = 0; i < 16; i++)
                {
                    expanded = max(expanded, Coverage(input.texcoord + Directions[i] * radius));
                    if (i % 2 == 0)
                        expanded = max(expanded, Coverage(input.texcoord + Directions[i] * radius * .5));
                }
                // No visible-mask/depth gate: only the exterior silhouette is drawn,
                // including when the entire character is behind an opaque wall.
                return half4(_OutlineColor.rgb, _OutlineColor.a * saturate(expanded - center));
            }
            ENDHLSL
        }
    }
}
