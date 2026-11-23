// URP version of the built-in Tree Creator Bark shader. Shared code: TreeCreatorURP.hlsl
Shader "Nature/Tree Creator Bark" {
Properties {
	_Color ("Main Color", Color) = (1,1,1,1)
	_Shininess ("Shininess", Range (0.01, 1)) = 0.078125
	_MainTex ("Base (RGB) Alpha (A)", 2D) = "white" {}
	_BumpMap ("Normalmap", 2D) = "bump" {}
	_GlossMap ("Gloss (A)", 2D) = "black" {}

	// These are here only to provide default values
	_SpecColor ("Specular Color", Color) = (0.5, 0.5, 0.5, 1)
	[HideInInspector] _TreeInstanceColor ("TreeInstanceColor", Vector) = (1,1,1,1)
	[HideInInspector] _TreeInstanceScale ("TreeInstanceScale", Vector) = (1,1,1,1)
	[HideInInspector] _SquashAmount ("Squash", Float) = 1
}

SubShader {
	Tags { "RenderType"="TreeBark" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" }
	LOD 200

	Pass {
		Name "ForwardLit"
		Tags { "LightMode"="UniversalForward" }

		HLSLPROGRAM
		#pragma target 3.0
		#pragma vertex TreeForwardVertex
		#pragma fragment TreeForwardFragment
		#pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
		#pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
		#pragma multi_compile _ _LIGHT_LAYERS
		#pragma multi_compile _ _CLUSTER_LIGHT_LOOP
		#pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
		#pragma multi_compile_fragment _ _SHADOWS_SOFT
		#pragma multi_compile_fragment _ _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
		#pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
		#pragma multi_compile_fragment _ _LIGHT_COOKIES
		#include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Fog.hlsl"
		#pragma multi_compile_instancing
		#include "TreeCreatorURP.hlsl"
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
		#pragma vertex TreeShadowVertex
		#pragma fragment TreeShadowFragment
		#pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
		#pragma multi_compile_instancing
		#include "TreeCreatorURP.hlsl"
		ENDHLSL
	}

	Pass {
		Name "DepthOnly"
		Tags { "LightMode"="DepthOnly" }
		ZWrite On
		ColorMask R

		HLSLPROGRAM
		#pragma target 3.0
		#pragma vertex TreeDepthVertex
		#pragma fragment TreeDepthFragment
		#pragma multi_compile_instancing
		#include "TreeCreatorURP.hlsl"
		ENDHLSL
	}

	Pass {
		Name "DepthNormals"
		Tags { "LightMode"="DepthNormals" }
		ZWrite On

		HLSLPROGRAM
		#pragma target 3.0
		#pragma vertex TreeDepthVertex
		#pragma fragment TreeDepthNormalsFragment
		#pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT
		#pragma multi_compile_instancing
		#include "TreeCreatorURP.hlsl"
		ENDHLSL
	}
}

Dependency "OptimizedShader" = "Hidden/Nature/Tree Creator Bark Optimized"
}
