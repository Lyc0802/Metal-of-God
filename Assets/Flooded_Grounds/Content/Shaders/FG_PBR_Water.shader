// URP version of the Flooded Grounds PBR water (originally a built-in Standard surface shader,
// which URP cannot render). Keeps the same properties, vertex waves, scrolling and parallax.
Shader "Flooded_Grounds/PBR_Water" {
Properties {
	_Color ("Main Color", Color) = (1,1,1,1)
	_Emis("Self-Ilumination", Range(0,1)) = 0.1
	_Smth("Smoothness", Range(0,1)) = 0.9
	_Parallax ("Height", Range (0.005, 0.08)) = 0.02
	_MainTex ("Base (RGB) Gloss (A)", 2D) = "white" {}
	_BumpMap ("Normalmap", 2D) = "bump" {}
	_BumpMap2 ("Normalmap2", 2D) = "bump" {}
	_BumpLerp("Normalmap2 Blend", Range(0,1)) = 0.5
	_ParallaxMap ("Heightmap", 2D) = "black" {}
	_ScrollSpeed("Scroll Speed", float) = 0.2
	_WaveFreq("Wave Frequency", float) = 20
	_WaveHeight("Wave Height", float) = 0.1
}

SubShader {
	Tags { "RenderType"="Opaque" "Queue"="Geometry" "RenderPipeline"="UniversalPipeline" }
	LOD 200

	HLSLINCLUDE
	#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

	TEXTURE2D(_MainTex);     SAMPLER(sampler_MainTex);
	TEXTURE2D(_BumpMap);     SAMPLER(sampler_BumpMap);
	TEXTURE2D(_BumpMap2);    SAMPLER(sampler_BumpMap2);
	TEXTURE2D(_ParallaxMap); SAMPLER(sampler_ParallaxMap);

	CBUFFER_START(UnityPerMaterial)
		float4 _MainTex_ST;
		float4 _BumpMap_ST;
		float4 _BumpMap2_ST;
		float4 _ParallaxMap_ST;
		half4 _Color;
		half _Emis;
		half _Smth;
		float _Parallax;
		half _BumpLerp;
		float _ScrollSpeed;
		float _WaveFreq;
		float _WaveHeight;
	CBUFFER_END

	struct WaterAttributes
	{
		float4 positionOS : POSITION;
		float3 normalOS   : NORMAL;
		float4 tangentOS  : TANGENT;
		float2 uv         : TEXCOORD0;
		UNITY_VERTEX_INPUT_INSTANCE_ID
	};

	// Built-in code assigned the float4 _Time to a float, i.e. it used _Time.x (time / 20).
	float3 WaterWave(float3 positionOS)
	{
		float phase = _Time.x * _WaveFreq;
		float offset = (positionOS.x + positionOS.z * 2) * 8;
		positionOS.y = sin(phase + offset) * _WaveHeight;
		return positionOS;
	}
	ENDHLSL

	Pass {
		Name "ForwardLit"
		Tags { "LightMode"="UniversalForward" }

		HLSLPROGRAM
		#pragma target 3.0
		#pragma vertex WaterVertex
		#pragma fragment WaterFragment
		#pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
		#pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
		#pragma multi_compile _ _LIGHT_LAYERS
		#pragma multi_compile _ _CLUSTER_LIGHT_LOOP
		#pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
		#pragma multi_compile_fragment _ _SHADOWS_SOFT
		#pragma multi_compile_fragment _ _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
		#pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
		#pragma multi_compile_fragment _ _REFLECTION_PROBE_BLENDING
		#pragma multi_compile_fragment _ _REFLECTION_PROBE_BOX_PROJECTION
		#pragma multi_compile_fragment _ _LIGHT_COOKIES
		#include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Fog.hlsl"
		#pragma multi_compile_instancing

		#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

		struct WaterVaryings
		{
			float4 positionCS             : SV_POSITION;
			float2 uv                     : TEXCOORD0;
			float3 positionWS             : TEXCOORD1;
			half3 normalWS                : TEXCOORD2;
			half4 tangentWS               : TEXCOORD3;
			half4 fogFactorAndVertexLight : TEXCOORD4;
		#if defined(REQUIRES_VERTEX_SHADOW_COORD_INTERPOLATOR)
			float4 shadowCoord            : TEXCOORD5;
		#endif
			UNITY_VERTEX_INPUT_INSTANCE_ID
			UNITY_VERTEX_OUTPUT_STEREO
		};

		WaterVaryings WaterVertex(WaterAttributes input)
		{
			WaterVaryings output = (WaterVaryings)0;
			UNITY_SETUP_INSTANCE_ID(input);
			UNITY_TRANSFER_INSTANCE_ID(input, output);
			UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

			VertexPositionInputs vertexInput = GetVertexPositionInputs(WaterWave(input.positionOS.xyz));
			VertexNormalInputs normalInput = GetVertexNormalInputs(input.normalOS, input.tangentOS);
			output.positionCS = vertexInput.positionCS;
			output.positionWS = vertexInput.positionWS;
			output.uv = input.uv;
			output.normalWS = normalInput.normalWS;
			output.tangentWS = half4(normalInput.tangentWS, input.tangentOS.w * GetOddNegativeScale());
			output.fogFactorAndVertexLight = half4(ComputeFogFactor(vertexInput.positionCS.z),
				VertexLighting(vertexInput.positionWS, normalInput.normalWS));
		#if defined(REQUIRES_VERTEX_SHADOW_COORD_INTERPOLATOR)
			output.shadowCoord = GetShadowCoord(vertexInput);
		#endif
			return output;
		}

		// Same as the built-in ParallaxOffset (UnityCG.cginc).
		float2 WaterParallaxOffset(half h, half height, half3 viewDirTS)
		{
			h = h * height - height / 2.0;
			float3 v = normalize(viewDirTS);
			v.z += 0.42;
			return h * (v.xy / v.z);
		}

		half4 WaterFragment(WaterVaryings input) : SV_Target
		{
			UNITY_SETUP_INSTANCE_ID(input);
			UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

			half3 bitangentWS = input.tangentWS.w * cross(input.normalWS, input.tangentWS.xyz);
			half3x3 tangentToWorld = half3x3(input.tangentWS.xyz, bitangentWS, input.normalWS);
			half3 viewDirWS = GetWorldSpaceNormalizeViewDir(input.positionWS);
			half3 viewDirTS = mul(tangentToWorld, viewDirWS);

			float time = _Time.x;
			float scrollX = _ScrollSpeed * time;
			float scrollY = _ScrollSpeed * time * 0.5;
			float scrollX2 = (1 - _ScrollSpeed) * time;
			float scrollY2 = (1 - _ScrollSpeed * time) * 0.5;

			float2 uvParallax = TRANSFORM_TEX(input.uv, _ParallaxMap) + float2(scrollX, scrollY) * 0.2;
			half h = SAMPLE_TEXTURE2D(_ParallaxMap, sampler_ParallaxMap, uvParallax).r;
			float2 offset = WaterParallaxOffset(h, _Parallax, viewDirTS);

			float2 uvMain = TRANSFORM_TEX(input.uv, _MainTex) + offset + float2(scrollX, scrollY);
			float2 uvBump = TRANSFORM_TEX(input.uv, _BumpMap);
			float2 uv1 = uvBump + offset + float2(scrollX, scrollY);
			float2 uv2 = uvBump + offset + float2(scrollX2, scrollY2);

			half3 normal1 = UnpackNormal(SAMPLE_TEXTURE2D(_BumpMap, sampler_BumpMap, uv1));
			half3 normal2 = UnpackNormal(SAMPLE_TEXTURE2D(_BumpMap, sampler_BumpMap, uv2));
			half3 normal3 = UnpackNormal(SAMPLE_TEXTURE2D(_BumpMap2, sampler_BumpMap2, TRANSFORM_TEX(input.uv, _BumpMap2)));
			half3 normalTS = normalize(lerp(normal1 + normal2 * half3(1, 1, 0), normal3, _BumpLerp));

			half3 color = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uvMain).rgb * _Color.rgb;

			SurfaceData surfaceData = (SurfaceData)0;
			surfaceData.albedo = color;
			surfaceData.metallic = 0;
			surfaceData.specular = 0;
			surfaceData.smoothness = _Smth;
			surfaceData.normalTS = normalTS;
			surfaceData.emission = color * _Emis;
			surfaceData.occlusion = 1;
			surfaceData.alpha = 1;

			InputData inputData = (InputData)0;
			inputData.positionWS = input.positionWS;
			inputData.normalWS = NormalizeNormalPerPixel(TransformTangentToWorld(normalTS, tangentToWorld));
			inputData.tangentToWorld = tangentToWorld;
			inputData.viewDirectionWS = viewDirWS;
		#if defined(REQUIRES_VERTEX_SHADOW_COORD_INTERPOLATOR)
			inputData.shadowCoord = input.shadowCoord;
		#elif defined(MAIN_LIGHT_CALCULATE_SHADOWS)
			inputData.shadowCoord = TransformWorldToShadowCoord(input.positionWS);
		#else
			inputData.shadowCoord = float4(0, 0, 0, 0);
		#endif
			inputData.fogCoord = InitializeInputDataFog(float4(input.positionWS, 1.0), input.fogFactorAndVertexLight.x);
			inputData.vertexLighting = input.fogFactorAndVertexLight.yzw;
			inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(input.positionCS);
			inputData.bakedGI = SampleSH(inputData.normalWS);
			inputData.shadowMask = half4(1, 1, 1, 1);

			half4 result = UniversalFragmentPBR(inputData, surfaceData);
			result.rgb = MixFog(result.rgb, inputData.fogCoord);
			result.a = 1;
			return result;
		}
		ENDHLSL
	}

	Pass {
		Name "ShadowCaster"
		Tags { "LightMode"="ShadowCaster" }
		ZWrite On
		ZTest LEqual
		ColorMask 0

		HLSLPROGRAM
		#pragma target 3.0
		#pragma vertex WaterShadowVertex
		#pragma fragment WaterShadowFragment
		#pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
		#pragma multi_compile_instancing

		#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

		float3 _LightDirection;
		float3 _LightPosition;

		float4 WaterShadowVertex(WaterAttributes input) : SV_POSITION
		{
			UNITY_SETUP_INSTANCE_ID(input);
			float3 positionWS = TransformObjectToWorld(WaterWave(input.positionOS.xyz));
			float3 normalWS = TransformObjectToWorldNormal(input.normalOS);
		#if defined(_CASTING_PUNCTUAL_LIGHT_SHADOW)
			float3 lightDirectionWS = normalize(_LightPosition - positionWS);
		#else
			float3 lightDirectionWS = _LightDirection;
		#endif
			return ApplyShadowClamping(TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, lightDirectionWS)));
		}

		half4 WaterShadowFragment() : SV_Target
		{
			return 0;
		}
		ENDHLSL
	}

	Pass {
		Name "DepthOnly"
		Tags { "LightMode"="DepthOnly" }
		ZWrite On
		ColorMask R

		HLSLPROGRAM
		#pragma target 3.0
		#pragma vertex WaterDepthVertex
		#pragma fragment WaterDepthFragment
		#pragma multi_compile_instancing

		float4 WaterDepthVertex(WaterAttributes input) : SV_POSITION
		{
			UNITY_SETUP_INSTANCE_ID(input);
			return TransformObjectToHClip(WaterWave(input.positionOS.xyz));
		}

		half WaterDepthFragment(float4 positionCS : SV_POSITION) : SV_Target
		{
			return positionCS.z;
		}
		ENDHLSL
	}

	Pass {
		Name "DepthNormals"
		Tags { "LightMode"="DepthNormals" }
		ZWrite On

		HLSLPROGRAM
		#pragma target 3.0
		#pragma vertex WaterDepthNormalsVertex
		#pragma fragment WaterDepthNormalsFragment
		#pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT
		#pragma multi_compile_instancing

		struct WaterDepthNormalsVaryings
		{
			float4 positionCS : SV_POSITION;
			half3 normalWS    : TEXCOORD0;
		};

		WaterDepthNormalsVaryings WaterDepthNormalsVertex(WaterAttributes input)
		{
			UNITY_SETUP_INSTANCE_ID(input);
			WaterDepthNormalsVaryings output;
			output.positionCS = TransformObjectToHClip(WaterWave(input.positionOS.xyz));
			output.normalWS = TransformObjectToWorldNormal(input.normalOS);
			return output;
		}

		half4 WaterDepthNormalsFragment(WaterDepthNormalsVaryings input) : SV_Target
		{
			float3 normalWS = NormalizeNormalPerPixel(input.normalWS);
		#if defined(_GBUFFER_NORMALS_OCT)
			float2 octNormalWS = PackNormalOctQuadEncode(normalWS);
			return half4(PackFloat2To888(saturate(octNormalWS * 0.5 + 0.5)), 0.0);
		#else
			return half4(normalWS, 0.0);
		#endif
		}
		ENDHLSL
	}
}
}
