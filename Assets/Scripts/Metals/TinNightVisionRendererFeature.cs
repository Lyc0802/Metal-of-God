using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;
using UnityEngine.Experimental.Rendering;

// Adds only missing surface lighting. Does not change exposure, scene lights or the other renderer features.
/// <summary>
/// 使用材質緩衝重建 Tin 夜視補光，保留場景原有較亮的表面與特效。
/// </summary>
public sealed class TinNightVisionRendererFeature : ScriptableRendererFeature
{
    [SerializeField]
    private Shader shader;
    private Material material;
    private NightPass pass;
    public override void Create()
    {
        CoreUtils.Destroy(material);
        material = shader ? CoreUtils.CreateEngineMaterial(shader) : null;
        pass = new NightPass();
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        var tin = PlayerTinAbility.ActiveInstance;
        if (!material || !tin || !tin.IsActive || tin.NightVisionStrength <= 0
            || renderingData.cameraData.camera != tin.ViewCamera)
        {
            return;
        }

        pass.Setup(material, tin);
        renderer.EnqueuePass(pass);
    }

    protected override void Dispose(bool disposing)
    {
        CoreUtils.Destroy(material);
    }

    sealed class NightPass : ScriptableRenderPass
    {
        private Material material;
        private PlayerTinAbility tin;
        private readonly MaterialPropertyBlock properties = new MaterialPropertyBlock();
        sealed class PassData
        {
            public TextureHandle Source, Albedo, Specular, Normals, Depth;
            public Material Material;
            public MaterialPropertyBlock Properties;
            public Vector4 Direction, Settings;
            public Color LightColor;
        }

        public NightPass()
        {
            renderPassEvent = RenderPassEvent.AfterRenderingOpaques;
            requiresIntermediateTexture = true;
        }

        public void Setup(Material mat, PlayerTinAbility ability)
        {
            material = mat;
            tin = ability;
        }

        sealed class SurfaceData
        {
            public RendererListHandle List;
        }

        private readonly TextureHandle[] surfaceBuffers = new TextureHandle[4];
        private static readonly GraphicsFormat[] SurfaceFormats =
        {
            GraphicsFormat.R8G8B8A8_SRGB,
            GraphicsFormat.R8G8B8A8_UNorm,
            GraphicsFormat.R16G16B16A16_SFloat,
            GraphicsFormat.R16G16B16A16_SFloat
        };
        private TextureHandle[] CreateSurfaceBuffers(
            RenderGraph graph,
            ContextContainer frameData,
            UniversalResourceData resources)
        {
            var camera = frameData.Get<UniversalCameraData>();
            var rendering = frameData.Get<UniversalRenderingData>();
            var desc = camera.cameraTargetDescriptor;
            desc.msaaSamples = 1;
            desc.depthBufferBits = 0;
            desc.depthStencilFormat = GraphicsFormat.None;
            var buffers = surfaceBuffers;
            for (int i = 0; i < 4; i++)
            {
                desc.graphicsFormat = SurfaceFormats[i];
                buffers[i] = UniversalRenderer.CreateRenderGraphTexture(
                    graph,
                    desc,
                    "Tin Material " + i,
                    true,
                    FilterMode.Point);
            }

            var parameters = new UnityEngine.Rendering.RendererUtils.RendererListDesc(
                new ShaderTagId("UniversalGBuffer"),
                rendering.cullResults,
                camera.camera)
            {
                sortingCriteria = SortingCriteria.CommonOpaque,
                renderQueueRange = RenderQueueRange.opaque,
                layerMask = camera.camera.cullingMask,
                rendererConfiguration = rendering.perObjectData,
                stateBlock = new RenderStateBlock(RenderStateMask.Depth)
                {
                    depthState = new DepthState(false, CompareFunction.Equal)
                }
            };
            using (var builder = graph.AddRasterRenderPass<SurfaceData>(
                "Tin: read visible material surfaces",
                out var data))
            {
                data.List = graph.CreateRendererList(parameters);
                builder.UseRendererList(data.List);
                for (int i = 0; i < 4; i++)
                {
                    builder.SetRenderAttachment(buffers[i], i, AccessFlags.Write);
                }

                builder.SetRenderAttachmentDepth(resources.activeDepthTexture, AccessFlags.Read);
                builder.SetRenderFunc(static (SurfaceData p, RasterGraphContext context) => context.cmd.DrawRendererList(p.List));
            }

            return buffers;
        }

        public override void RecordRenderGraph(RenderGraph graph, ContextContainer frameData)
        {
            var resources = frameData.Get<UniversalResourceData>();
            var gb = resources.gBuffer;
            if (resources.isActiveTargetBackBuffer)
            {
                return;
            }

            // Forward/Forward+ does not produce material buffers. Render the materials' own GBuffer
            // passes into temporary targets, using the existing camera depth; no replacement materials.
            if (gb == null || gb.Length < 3 || !gb[0].IsValid() || !gb[1].IsValid() || !gb[2].IsValid())
            {
                gb = CreateSurfaceBuffers(graph, frameData, resources);
            }

            var source = resources.activeColorTexture;
            var desc = graph.GetTextureDesc(source);
            desc.name = "Tin Relit Camera Color";
            desc.clearBuffer = false;
            var result = graph.CreateTexture(desc);
            using (var builder = graph.AddRasterRenderPass<PassData>("Tin: fill missing surface light", out var data))
            {
                data.Source = source;
                data.Albedo = gb[0];
                data.Specular = gb[1];
                data.Normals = gb[2];
                data.Depth = resources.activeDepthTexture;
                data.Material = material;
                data.Properties = properties;
                data.Direction = tin.ReferenceLightDirection;
                data.LightColor = tin.ReferenceLightColor;
                // Use URP's exact temperature/color conversion when the reference is visible.
                foreach (var visible in frameData.Get<UniversalLightData>().visibleLights)
                {
                    if (visible.light && visible.light == tin.DirectionReference && visible.light.intensity > .000001f)
                    {
                        data.LightColor = visible.finalColor / visible.light.intensity;
                        break;
                    }
                }

                data.Settings = new Vector4(
                    Mathf.Clamp(tin.ReferenceLightIntensity, 0, 2),
                    Mathf.Clamp01(tin.MinimumSurfaceLighting),
                    Mathf.Clamp01(tin.ShadowRetention),
                    Mathf.Clamp01(tin.NightVisionStrength));
                builder.UseTexture(data.Source, AccessFlags.Read);
                builder.UseTexture(data.Albedo, AccessFlags.Read);
                builder.UseTexture(data.Specular, AccessFlags.Read);
                builder.UseTexture(data.Normals, AccessFlags.Read);
                builder.UseTexture(data.Depth, AccessFlags.Read);
                builder.SetRenderAttachment(result, 0, AccessFlags.Write);
                builder.SetRenderFunc(static (PassData p, RasterGraphContext context) =>
                {
                    p.Properties.Clear();
                    p.Properties.SetTexture("_BlitTexture", (RTHandle)p.Source);
                    p.Properties.SetTexture("_TinAlbedo", (RTHandle)p.Albedo);
                    p.Properties.SetTexture("_TinSpecular", (RTHandle)p.Specular);
                    p.Properties.SetTexture("_TinNormals", (RTHandle)p.Normals);
                    p.Properties.SetTexture("_TinDepth", (RTHandle)p.Depth);
                    p.Properties.SetVector("_BlitScaleBias", new Vector4(1, 1, 0, 0));
                    p.Properties.SetVector("_TinLightDirection", p.Direction);
                    p.Properties.SetVector("_TinLightColor", (Vector4)p.LightColor);
                    p.Properties.SetVector("_TinSettings", p.Settings);
                    context.cmd.DrawProcedural(Matrix4x4.identity, p.Material, 0, MeshTopology.Triangles, 3, 1, p.Properties);
                });
            }

            resources.cameraColor = result;
        }
    }
}
