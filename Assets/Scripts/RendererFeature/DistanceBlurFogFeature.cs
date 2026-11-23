using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

// Blurs and slightly fogs everything beyond a distance (soft atmospheric haze).
// Add to the URP Renderer Data asset: Add Renderer Feature > Distance Blur Fog Feature.
public sealed class DistanceBlurFogFeature : ScriptableRendererFeature
{
    [System.Serializable]
    public sealed class Settings
    {
        public RenderPassEvent PassEvent = RenderPassEvent.BeforeRenderingPostProcessing;
        [Tooltip("Distance (m) from the camera where blur/fog starts.")]
        [Min(0)] public float StartDistance = 25;
        [Tooltip("Distance (m) over which the effect ramps from none to full.")]
        [Min(.01f)] public float FadeDistance = 40;
        [Range(0, 1)] public float MaxBlur = 1;
        [Tooltip("Blur spread in source pixels per tap.")]
        [Range(.5f, 4)] public float BlurRadius = 1.5f;
        [Range(1, 4)] public int Iterations = 2;
        [Tooltip("Blur buffer resolution divider. Higher is cheaper and softer.")]
        [Range(1, 8)] public int Downsample = 2;
        public Color FogColor = new Color(.62f, .66f, .7f, 1);
        [Range(0, 1)] public float FogStrength = .3f;
        public bool AffectSkybox = true;
        public bool ShowInSceneView = true;
    }

    public Settings settings = new Settings();
    [SerializeField] private Shader shader;
    private Material material;
    private DistanceBlurFogPass pass;

    public override void Create()
    {
        if (!shader) shader = Shader.Find("Hidden/DistanceBlurFog");
        if (shader && !material) material = CoreUtils.CreateEngineMaterial(shader);
        pass = new DistanceBlurFogPass();
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        if (!material || (settings.MaxBlur <= 0 && settings.FogStrength <= 0)) return;
        var type = renderingData.cameraData.cameraType;
        if (type == CameraType.Preview || type == CameraType.Reflection) return;
        if (type == CameraType.SceneView && !settings.ShowInSceneView) return;
        pass.Setup(material, settings);
        pass.ConfigureInput(ScriptableRenderPassInput.Depth);
        renderer.EnqueuePass(pass);
    }

    protected override void Dispose(bool disposing)
    {
        CoreUtils.Destroy(material);
        material = null;
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        // Keeps a serialized reference so the hidden shader is included in builds.
        if (!shader) shader = Shader.Find("Hidden/DistanceBlurFog");
    }
#endif

    private sealed class DistanceBlurFogPass : ScriptableRenderPass
    {
        private const int HorizontalPass = 0, VerticalPass = 1, CompositePass = 2;
        private static readonly int BlitTextureId = Shader.PropertyToID("_BlitTexture");
        private static readonly int BlitScaleBiasId = Shader.PropertyToID("_BlitScaleBias");
        private static readonly int BlurTexId = Shader.PropertyToID("_DistanceBlurTex");
        private static readonly int DepthTexId = Shader.PropertyToID("_CameraDepthTexture");
        private static readonly int TexelSizeId = Shader.PropertyToID("_DistanceBlurTexelSize");
        private static readonly int ParamsId = Shader.PropertyToID("_DistanceBlurParams");
        private static readonly int FogColorId = Shader.PropertyToID("_DistanceBlurFogColor");
        private static readonly int RadiusId = Shader.PropertyToID("_DistanceBlurRadius");
        private static readonly int AffectSkyId = Shader.PropertyToID("_DistanceBlurAffectSky");
        private static readonly MaterialPropertyBlock block = new MaterialPropertyBlock();

        private Material material;
        private Settings settings;

        private sealed class PassData
        {
            public Material Material;
            public int Pass;
            public TextureHandle Source, Blur, Depth;
            public Vector4 TexelSize;
        }

        public DistanceBlurFogPass()
        {
            profilingSampler = new ProfilingSampler("Distance Blur Fog");
            requiresIntermediateTexture = true;
        }

        public void Setup(Material mat, Settings s)
        {
            material = mat;
            settings = s;
            renderPassEvent = s.PassEvent;
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            var resources = frameData.Get<UniversalResourceData>();
            var cameraData = frameData.Get<UniversalCameraData>();
            if (resources.isActiveTargetBackBuffer) return;
            TextureHandle source = resources.activeColorTexture;
            TextureHandle depth = resources.cameraDepthTexture;
            if (!source.IsValid() || !depth.IsValid()) return;

            var s = settings;
            material.SetVector(ParamsId, new Vector4(s.StartDistance, 1f / Mathf.Max(.01f, s.FadeDistance), s.MaxBlur, s.FogStrength));
            material.SetColor(FogColorId, s.FogColor);
            material.SetFloat(RadiusId, s.BlurRadius);
            material.SetFloat(AffectSkyId, s.AffectSkybox ? 1 : 0);

            var desc = cameraData.cameraTargetDescriptor;
            desc.depthStencilFormat = GraphicsFormat.None;
            desc.msaaSamples = 1;
            Vector2 fullSize = new Vector2(desc.width, desc.height);
            TextureHandle destination = UniversalRenderer.CreateRenderGraphTexture(renderGraph, desc, "_DistanceBlurFogColor", false, FilterMode.Bilinear);

            int divider = Mathf.Max(1, s.Downsample);
            desc.width = Mathf.Max(1, desc.width / divider);
            desc.height = Mathf.Max(1, desc.height / divider);
            Vector2 blurSize = new Vector2(desc.width, desc.height);
            TextureHandle blurA = UniversalRenderer.CreateRenderGraphTexture(renderGraph, desc, "_DistanceBlurA", false, FilterMode.Bilinear);
            TextureHandle blurB = UniversalRenderer.CreateRenderGraphTexture(renderGraph, desc, "_DistanceBlurB", false, FilterMode.Bilinear);

            // Separable gaussian: first pass also downsamples the scene color.
            AddDraw(renderGraph, "Distance Blur H", source, fullSize, blurA, HorizontalPass, TextureHandle.nullHandle, TextureHandle.nullHandle);
            AddDraw(renderGraph, "Distance Blur V", blurA, blurSize, blurB, VerticalPass, TextureHandle.nullHandle, TextureHandle.nullHandle);
            for (int i = 1; i < s.Iterations; i++)
            {
                AddDraw(renderGraph, "Distance Blur H", blurB, blurSize, blurA, HorizontalPass, TextureHandle.nullHandle, TextureHandle.nullHandle);
                AddDraw(renderGraph, "Distance Blur V", blurA, blurSize, blurB, VerticalPass, TextureHandle.nullHandle, TextureHandle.nullHandle);
            }
            AddDraw(renderGraph, "Distance Blur Composite", source, fullSize, destination, CompositePass, blurB, depth);
            resources.cameraColor = destination;
        }

        private void AddDraw(RenderGraph renderGraph, string name, TextureHandle source, Vector2 sourceSize,
            TextureHandle destination, int shaderPass, TextureHandle blur, TextureHandle depth)
        {
            using var builder = renderGraph.AddRasterRenderPass<PassData>(name, out var data, profilingSampler);
            data.Material = material;
            data.Pass = shaderPass;
            data.Source = source;
            data.Blur = blur;
            data.Depth = depth;
            data.TexelSize = new Vector4(1f / sourceSize.x, 1f / sourceSize.y, sourceSize.x, sourceSize.y);
            builder.UseTexture(source);
            if (blur.IsValid()) builder.UseTexture(blur);
            if (depth.IsValid()) builder.UseTexture(depth);
            builder.SetRenderAttachment(destination, 0, AccessFlags.WriteAll);
            builder.SetRenderFunc((PassData d, RasterGraphContext context) => Execute(d, context));
        }

        private static void Execute(PassData d, RasterGraphContext context)
        {
            // Property block values are copied when recorded, so each draw keeps its own inputs.
            block.Clear();
            RTHandle source = d.Source;
            block.SetTexture(BlitTextureId, source);
            block.SetVector(BlitScaleBiasId, new Vector4(1, 1, 0, 0));
            block.SetVector(TexelSizeId, d.TexelSize);
            if (d.Blur.IsValid()) { RTHandle blur = d.Blur; block.SetTexture(BlurTexId, blur); }
            if (d.Depth.IsValid()) { RTHandle depth = d.Depth; block.SetTexture(DepthTexId, depth); }
            context.cmd.DrawProcedural(Matrix4x4.identity, d.Material, d.Pass, MeshTopology.Triangles, 3, 1, block);
        }
    }
}
