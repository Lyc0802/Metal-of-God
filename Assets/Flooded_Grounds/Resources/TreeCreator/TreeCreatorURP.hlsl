#ifndef TREE_CREATOR_URP_INCLUDED
#define TREE_CREATOR_URP_INCLUDED

// URP port of Unity's built-in Tree Creator library (UnityBuiltin3xTreeLibrary.cginc / TerrainEngine.cginc).
// Built-in surface shaders cannot render in URP, which made every tree material show as missing.
//
// Optional defines (set in each .shader before including this file):
//   TREE_LEAF          billboard expansion, two-sided normals and alpha clipping with _Cutoff
//   TREE_OPTIMIZED     packed _BumpSpecMap (normal in GA) instead of _BumpMap/_GlossMap
//   TREE_TRANSLUCENCY  leaf translucency from _TranslucencyMap (Tree Creator Leaves lighting)
//   TREE_VIEW_FADE     fades leaf cards seen head-on (Flooded Grounds optimized leaves)
//   TREE_EMISSION_TINT small emission from _TranslucencyColor (Flooded Grounds optimized leaves)
//   TREE_SHADOW_TEX    shadow caster clips with _ShadowTex.r instead of the main texture alpha

#define _SPECULAR_COLOR

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

TEXTURE2D(_MainTex);         SAMPLER(sampler_MainTex);
TEXTURE2D(_BumpMap);         SAMPLER(sampler_BumpMap);
TEXTURE2D(_GlossMap);
TEXTURE2D(_TranslucencyMap);
TEXTURE2D(_BumpSpecMap);
TEXTURE2D(_ShadowTex);       SAMPLER(sampler_ShadowTex);

CBUFFER_START(UnityPerMaterial)
    float4 _MainTex_ST;
    float4 _ShadowTex_ST;
    half4 _Color;
    half4 _SpecColor;
    half4 _TranslucencyColor;
    half _Shininess;
    half _Cutoff;
    half _TranslucencyViewDependency;
    half _ShadowStrength;
    float _ShadowOffsetScale;
    float4 _TreeInstanceColor;
    float4 _TreeInstanceScale;
    float _SquashAmount;
CBUFFER_END

// Set by the tree / terrain systems, not by the material.
float4 _Wind;
float4 _SquashPlaneNormal;

// Set by URP while rendering shadow maps.
float3 _LightDirection;
float3 _LightPosition;

struct TreeAttributes
{
    float4 positionOS : POSITION;
    float3 normalOS   : NORMAL;
    float4 tangentOS  : TANGENT;
    float4 color      : COLOR;     // rgb: wind parameters, a: ambient occlusion
    float2 uv         : TEXCOORD0;
    float2 uv1        : TEXCOORD1; // wind parameters
    UNITY_VERTEX_INPUT_INSTANCE_ID
};

// ---------------------------------------------------------------------------------------------
// Vertex animation (same math as TerrainEngine.cginc)
// ---------------------------------------------------------------------------------------------

float4 TreeSmoothCurve(float4 x) { return x * x * (3.0 - 2.0 * x); }
float4 TreeTriangleWave(float4 x) { return abs(frac(x + 0.5) * 2.0 - 1.0); }
float4 TreeSmoothTriangleWave(float4 x) { return TreeSmoothCurve(TreeTriangleWave(x)); }

// animParams: x = branch phase, y = edge flutter, z = primary bend, w = secondary bend
float4 TreeAnimateVertex(float4 pos, float3 normal, float4 animParams)
{
    const float detailAmp = 0.1;
    const float branchAmp = 0.3;

    float objectPhase = dot(GetObjectToWorldMatrix()._m03_m13_m23, 1);
    float branchPhase = objectPhase + animParams.x;
    float vertexPhase = dot(pos.xyz, animParams.y + branchPhase);

    float2 wavesIn = _Time.yy + float2(vertexPhase, branchPhase);
    float4 waves = frac(wavesIn.xxyy * float4(1.975, 0.793, 0.375, 0.193)) * 2.0 - 1.0;
    waves = TreeSmoothTriangleWave(waves);
    float2 wavesSum = waves.xz + waves.yw;

    float3 bend = animParams.y * detailAmp * normal;
    bend.y = animParams.w * branchAmp;
    pos.xyz += ((wavesSum.xyx * bend) + (_Wind.xyz * wavesSum.y * animParams.w)) * _Wind.w;
    pos.xyz += animParams.z * _Wind.xyz;
    return pos;
}

float4 TreeSquash(float4 pos)
{
    float3 planeNormal = _SquashPlaneNormal.xyz;
    float3 projected = pos.xyz - (dot(planeNormal, pos.xyz) + _SquashPlaneNormal.w) * planeNormal;
    return float4(lerp(projected, pos.xyz, _SquashAmount), 1);
}

// Leaf billboards store their view-space offset in the normal; tangent.w == 0 marks them.
void TreeExpandBillboard(float4x4 mat, inout float4 pos, inout float3 normal, inout float4 tangent)
{
    float isBillboard = 1.0 - abs(tangent.w);
    float3 billboardNormal = normalize(mul(float4(normal, 0), mat)).xyz;
    float3 billboardTangent = normalize(mul(float4(tangent.xyz, 0), mat)).xyz;
    pos += mul(float4(normal.xy, 0, 0), mat) * isBillboard;
    normal = lerp(normal, billboardNormal, isBillboard);
    tangent = lerp(tangent, float4(billboardTangent, -1.0), isBillboard);
}

void TreeVertex(inout TreeAttributes v)
{
#if defined(TREE_LEAF)
    TreeExpandBillboard(UNITY_MATRIX_IT_MV, v.positionOS, v.normalOS, v.tangentOS);
#endif
    v.positionOS.xyz *= _TreeInstanceScale.xyz;
    // Same packing as the built-in TreeVertBark/TreeVertLeaf: color.xy + uv1.xy.
    v.positionOS = TreeAnimateVertex(v.positionOS, v.normalOS, float4(v.color.xy, v.uv1.xy));
    v.positionOS = TreeSquash(v.positionOS);
    v.color.rgb = _TreeInstanceColor.rgb * _Color.rgb;
    v.normalOS = normalize(v.normalOS);
    v.tangentOS.xyz = normalize(v.tangentOS.xyz);
}

// ---------------------------------------------------------------------------------------------
// Surface
// ---------------------------------------------------------------------------------------------

half TreeAlpha(float2 uv, float3 positionWS, half3 normalWS)
{
    half alpha = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv).a;
#if defined(TREE_VIEW_FADE)
    half facing = pow(saturate(abs(dot(GetWorldSpaceNormalizeViewDir(positionWS), normalize(normalWS)))), 12);
    alpha = saturate(alpha * (1 - facing));
#endif
    return alpha;
}

void TreeClip(float2 uv, float3 positionWS, half3 normalWS)
{
#if defined(TREE_LEAF)
    clip(TreeAlpha(uv, positionWS, normalWS) - _Cutoff);
#endif
}

// ---------------------------------------------------------------------------------------------
// Forward pass
// ---------------------------------------------------------------------------------------------

struct TreeVaryings
{
    float4 positionCS              : SV_POSITION;
    float2 uv                      : TEXCOORD0;
    half4 color                    : TEXCOORD1;
    float3 positionWS              : TEXCOORD2;
    half3 normalWS                 : TEXCOORD3;
    half4 tangentWS                : TEXCOORD4;
    half4 fogFactorAndVertexLight  : TEXCOORD5;
#if defined(REQUIRES_VERTEX_SHADOW_COORD_INTERPOLATOR)
    float4 shadowCoord             : TEXCOORD6;
#endif
    UNITY_VERTEX_INPUT_INSTANCE_ID
    UNITY_VERTEX_OUTPUT_STEREO
};

TreeVaryings TreeForwardVertex(TreeAttributes input)
{
    TreeVaryings output = (TreeVaryings)0;
    UNITY_SETUP_INSTANCE_ID(input);
    UNITY_TRANSFER_INSTANCE_ID(input, output);
    UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

    TreeVertex(input);
    VertexPositionInputs vertexInput = GetVertexPositionInputs(input.positionOS.xyz);
    VertexNormalInputs normalInput = GetVertexNormalInputs(input.normalOS, input.tangentOS);

    output.positionCS = vertexInput.positionCS;
    output.positionWS = vertexInput.positionWS;
    output.uv = TRANSFORM_TEX(input.uv, _MainTex);
    output.color = input.color;
    output.normalWS = normalInput.normalWS;
    output.tangentWS = half4(normalInput.tangentWS, input.tangentOS.w * GetOddNegativeScale());
    output.fogFactorAndVertexLight = half4(ComputeFogFactor(vertexInput.positionCS.z),
        VertexLighting(vertexInput.positionWS, normalInput.normalWS));
#if defined(REQUIRES_VERTEX_SHADOW_COORD_INTERPOLATOR)
    output.shadowCoord = GetShadowCoord(vertexInput);
#endif
    return output;
}

// Tree Creator Leaves lighting: light passing through the leaf, strongest when looking into the sun.
half3 TreeTranslucency(InputData inputData, half3 albedo, half3 translucency)
{
    Light mainLight = GetMainLight(inputData.shadowCoord, inputData.positionWS, inputData.shadowMask);
    half nl = dot(inputData.normalWS, mainLight.direction);
    half back = saturate(dot(inputData.viewDirectionWS, -mainLight.direction));
    back = lerp(saturate(-nl), back, _TranslucencyViewDependency);
    half shadow = lerp(1, mainLight.shadowAttenuation, _ShadowStrength);
    return albedo * translucency * _TranslucencyColor.rgb * back * 2 * mainLight.color
        * mainLight.distanceAttenuation * shadow;
}

half4 TreeForwardFragment(TreeVaryings input, FRONT_FACE_TYPE isFrontFace : FRONT_FACE_SEMANTIC) : SV_Target
{
    UNITY_SETUP_INSTANCE_ID(input);
    UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

    TreeClip(input.uv, input.positionWS, input.normalWS);

    half4 albedoAlpha = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv);
    SurfaceData surfaceData = (SurfaceData)0;
    surfaceData.albedo = albedoAlpha.rgb * input.color.rgb * input.color.a;
    surfaceData.alpha = 1;
    surfaceData.occlusion = 1;
#if defined(TREE_OPTIMIZED)
    surfaceData.normalTS = UnpackNormalAG(SAMPLE_TEXTURE2D(_BumpSpecMap, sampler_MainTex, input.uv), 1.0);
#else
    surfaceData.normalTS = UnpackNormal(SAMPLE_TEXTURE2D(_BumpMap, sampler_BumpMap, input.uv));
    half gloss = SAMPLE_TEXTURE2D(_GlossMap, sampler_MainTex, input.uv).a;
    // Built-in Blinn-Phong used pow(nh, _Shininess * 128); URP uses exp2(10 * smoothness + 1).
    surfaceData.smoothness = saturate((log2(max(_Shininess * 128.0, 2.0)) - 1.0) / 10.0);
    #if defined(TREE_LEAF)
        surfaceData.specular = gloss.xxx;
    #else
        surfaceData.specular = _SpecColor.rgb * gloss;
    #endif
#endif
#if defined(TREE_EMISSION_TINT)
    surfaceData.emission = _TranslucencyColor.rgb * 0.02;
#endif

    InputData inputData = (InputData)0;
    inputData.positionWS = input.positionWS;
    half3 bitangentWS = input.tangentWS.w * cross(input.normalWS, input.tangentWS.xyz);
    half3x3 tangentToWorld = half3x3(input.tangentWS.xyz, bitangentWS, input.normalWS);
    half3 normalWS = NormalizeNormalPerPixel(TransformTangentToWorld(surfaceData.normalTS, tangentToWorld));
#if defined(TREE_LEAF)
    normalWS = IS_FRONT_VFACE(isFrontFace, normalWS, -normalWS);
#endif
    inputData.normalWS = normalWS;
    inputData.tangentToWorld = tangentToWorld;
    inputData.viewDirectionWS = GetWorldSpaceNormalizeViewDir(input.positionWS);
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
    inputData.bakedGI = SampleSH(normalWS);
    inputData.shadowMask = half4(1, 1, 1, 1);

#if defined(TREE_TRANSLUCENCY)
    half3 translucency = SAMPLE_TEXTURE2D(_TranslucencyMap, sampler_MainTex, input.uv).rgb;
    surfaceData.emission += TreeTranslucency(inputData, surfaceData.albedo, translucency);
#endif

    half4 color = UniversalFragmentBlinnPhong(inputData, surfaceData);
    color.rgb = MixFog(color.rgb, inputData.fogCoord);
    color.a = 1;
    return color;
}

// ---------------------------------------------------------------------------------------------
// Shadow caster
// ---------------------------------------------------------------------------------------------

struct TreeShadowVaryings
{
    float4 positionCS : SV_POSITION;
    float2 uv         : TEXCOORD0;
    UNITY_VERTEX_INPUT_INSTANCE_ID
};

TreeShadowVaryings TreeShadowVertex(TreeAttributes input)
{
    TreeShadowVaryings output = (TreeShadowVaryings)0;
    UNITY_SETUP_INSTANCE_ID(input);
    UNITY_TRANSFER_INSTANCE_ID(input, output);

    TreeVertex(input);
#if defined(TREE_SHADOW_TEX)
    output.uv = TRANSFORM_TEX(input.uv, _ShadowTex);
#else
    output.uv = TRANSFORM_TEX(input.uv, _MainTex);
#endif

    float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
    float3 normalWS = TransformObjectToWorldNormal(input.normalOS);
#if defined(_CASTING_PUNCTUAL_LIGHT_SHADOW)
    float3 lightDirectionWS = normalize(_LightPosition - positionWS);
#else
    float3 lightDirectionWS = _LightDirection;
#endif
    output.positionCS = ApplyShadowClamping(TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, lightDirectionWS)));
    return output;
}

half4 TreeShadowFragment(TreeShadowVaryings input) : SV_Target
{
    UNITY_SETUP_INSTANCE_ID(input);
#if defined(TREE_SHADOW_TEX)
    clip(SAMPLE_TEXTURE2D(_ShadowTex, sampler_ShadowTex, input.uv).r - _Cutoff);
#elif defined(TREE_LEAF)
    clip(SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv).a - _Cutoff);
#endif
    return 0;
}

// ---------------------------------------------------------------------------------------------
// Depth only / depth normals (depth prepass, SSAO)
// ---------------------------------------------------------------------------------------------

struct TreeDepthVaryings
{
    float4 positionCS : SV_POSITION;
    float2 uv         : TEXCOORD0;
    float3 positionWS : TEXCOORD1;
    half3 normalWS    : TEXCOORD2;
    UNITY_VERTEX_INPUT_INSTANCE_ID
    UNITY_VERTEX_OUTPUT_STEREO
};

TreeDepthVaryings TreeDepthVertex(TreeAttributes input)
{
    TreeDepthVaryings output = (TreeDepthVaryings)0;
    UNITY_SETUP_INSTANCE_ID(input);
    UNITY_TRANSFER_INSTANCE_ID(input, output);
    UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

    TreeVertex(input);
    output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
    output.positionCS = TransformWorldToHClip(output.positionWS);
    output.uv = TRANSFORM_TEX(input.uv, _MainTex);
    output.normalWS = TransformObjectToWorldNormal(input.normalOS);
    return output;
}

half TreeDepthFragment(TreeDepthVaryings input) : SV_Target
{
    UNITY_SETUP_INSTANCE_ID(input);
    UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
    TreeClip(input.uv, input.positionWS, input.normalWS);
    return input.positionCS.z;
}

half4 TreeDepthNormalsFragment(TreeDepthVaryings input, FRONT_FACE_TYPE isFrontFace : FRONT_FACE_SEMANTIC) : SV_Target
{
    UNITY_SETUP_INSTANCE_ID(input);
    UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
    TreeClip(input.uv, input.positionWS, input.normalWS);

    float3 normalWS = NormalizeNormalPerPixel(input.normalWS);
#if defined(TREE_LEAF)
    normalWS = IS_FRONT_VFACE(isFrontFace, normalWS, -normalWS);
#endif
#if defined(_GBUFFER_NORMALS_OCT)
    float2 octNormalWS = PackNormalOctQuadEncode(normalWS);
    return half4(PackFloat2To888(saturate(octNormalWS * 0.5 + 0.5)), 0.0);
#else
    return half4(normalWS, 0.0);
#endif
}

#endif
