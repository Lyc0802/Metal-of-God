using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace SimpleOutline
{
    /// <summary>
    /// 分開繪製一般金屬 Outline 與 Bronze 穿牆 Outline，避免互相覆寫顯示狀態。
    /// </summary>
    public sealed class OutlineRendererFeature : ScriptableRendererFeature
    {
        [SerializeField]
        private Shader _shader;
        [SerializeField]
        private Color _color = Color.white;
        [SerializeField, Range(1f, 6f)]
        private float _widthPixels = 4f;
        // Work counters, not FPS estimates: sum of composite rectangles in screen areas.
        public int LastEligibleTargets { get; private set; }
        public int LastRenderedTargets { get; private set; }
        public float LastCompositeScreenAreas { get; private set; }

        private Material _material;
        private OutlinePass _pass;
        private BronzeXRayPass bronzePass;
        public int LastBronzeRenderedTargets { get; private set; }

        public override void Create()
        {
            CoreUtils.Destroy(_material);
            _material = _shader ? CoreUtils.CreateEngineMaterial(_shader) : null;
            _pass = new OutlinePass(this);
            bronzePass = new BronzeXRayPass(this);
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            if (!_material || renderingData.cameraData.cameraType != CameraType.Game)
            {
                return;
            }

            if (OutlineTarget.ActiveTargets.Count == 0)
            {
                LastEligibleTargets = LastRenderedTargets = 0;
                LastCompositeScreenAreas = 0;
            }
            else
            {
                renderer.EnqueuePass(_pass);
            }

            if (BronzeOutlineRegistry.HasTargets(renderingData.cameraData.camera))
            {
                renderer.EnqueuePass(bronzePass);
            }
            else
            {
                LastBronzeRenderedTargets = 0;
            }
        }

        protected override void Dispose(bool disposing)
        {
            CoreUtils.Destroy(_material);
        }

        // Independent X-Ray channel. The existing metal silhouette/visible-mask pass
        // and its visibility-gated composite below are deliberately unchanged.
        private sealed class BronzeXRayPass : ScriptableRenderPass
        {
            private readonly OutlineRendererFeature settings;
            private readonly Plane[] planes = new Plane[6];
            private readonly MaterialPropertyBlock properties = new MaterialPropertyBlock();
            private static readonly int TextureId = Shader.PropertyToID("_BlitTexture"), ScaleId = Shader.PropertyToID("_BlitScaleBias"), TexelId = Shader.PropertyToID("_BlitTexture_TexelSize"), ColorId = Shader.PropertyToID("_OutlineColor"), WidthId = Shader.PropertyToID("_OutlinePixels");
            sealed class MaskData
            {
                public Renderer[] Renderers;
                public Material Material;
                public int CullingMask;
            }

            sealed class CompositeData
            {
                public TextureHandle Mask;
                public Material Material;
                public MaterialPropertyBlock Properties;
                public Rect Rect;
                public Color Color;
                public float Width;
                public Vector4 Texel;
            }

            public BronzeXRayPass(OutlineRendererFeature owner)
            {
                settings = owner;
                renderPassEvent = RenderPassEvent.AfterRenderingTransparents;
            }

            private static Mesh MeshOf(Renderer renderer) => renderer is SkinnedMeshRenderer skin
                ? skin.sharedMesh
                : renderer.GetComponent<MeshFilter>()?.sharedMesh;
            private Rect ScreenRect(Camera camera, Bounds bounds, int width, int height, float pixels)
            {
                if (camera.stereoEnabled)
                {
                    return new Rect(0, 0, width, height);
                }

                Vector2 min = Vector2.one * float.PositiveInfinity, max = Vector2.one * float.NegativeInfinity;
                for (int i = 0; i < 8; i++)
                {
                    var corner = bounds.center + Vector3.Scale(
                        bounds.extents,
                        new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                    var v = camera.WorldToViewportPoint(corner);
                    if (v.z <= camera.nearClipPlane)
                    {
                        return new Rect(0, 0, width, height);
                    }

                    var p = new Vector2(v.x * width, v.y * height);
                    min = Vector2.Min(min, p);
                    max = Vector2.Max(max, p);
                }

                float margin = Mathf.Ceil(pixels) + 2;
                return Rect.MinMaxRect(
                    Mathf.Clamp(Mathf.Floor(min.x - margin), 0, width),
                    Mathf.Clamp(Mathf.Floor(min.y - margin), 0, height),
                    Mathf.Clamp(Mathf.Ceil(max.x + margin), 0, width),
                    Mathf.Clamp(Mathf.Ceil(max.y + margin), 0, height));
            }

            public override void RecordRenderGraph(RenderGraph graph, ContextContainer frameData)
            {
                var resources = frameData.Get<UniversalResourceData>();
                var cameraData = frameData.Get<UniversalCameraData>();
                var camera = cameraData.camera;
                GeometryUtility.CalculateFrustumPlanes(camera, planes);
                settings.LastBronzeRenderedTargets = 0;
                var descriptor = cameraData.cameraTargetDescriptor;
                descriptor.graphicsFormat = UnityEngine.Experimental.Rendering.GraphicsFormat.R8G8_UNorm;
                descriptor.depthStencilFormat = UnityEngine.Experimental.Rendering.GraphicsFormat.None;
                descriptor.depthBufferBits = 0;
                descriptor.msaaSamples = 1;
                descriptor.bindMS = false;
                foreach (var entry in BronzeOutlineRegistry.Entries)
                {
                    // Re-query live inventory and Copper here: even a toggle after the
                    // detector's last tick cannot leave an outline visible this frame.
                    if (!entry.IsVisibleTo(camera))
                    {
                        continue;
                    }

                    bool found = false;
                    Bounds bounds = default;
                    foreach (var renderer in entry.Renderers)
                    {
                        if (!renderer || !renderer.enabled || !renderer.gameObject.activeInHierarchy
                            || renderer.forceRenderingOff
                            || (camera.cullingMask & (1 << renderer.gameObject.layer)) == 0
                            || !GeometryUtility.TestPlanesAABB(
                            planes,
                            renderer.bounds))
                        {
                            continue;
                        }

                        var mesh = MeshOf(renderer);
                        if (!mesh || mesh.vertexCount == 0)
                        {
                            continue;
                        }

                        if (!found)
                        {
                            bounds = renderer.bounds;
                            found = true;
                        }
                        else
                        {
                            bounds.Encapsulate(renderer.bounds);
                        }
                    }

                    if (!found)
                    {
                        continue;
                    }

                    var rect = ScreenRect(
                        camera,
                        bounds,
                        descriptor.width,
                        descriptor.height,
                        entry.Observer.OutlinePixels);
                    if (rect.width <= 0 || rect.height <= 0)
                    {
                        continue;
                    }

                    settings.LastBronzeRenderedTargets++;
                    var mask = UniversalRenderer.CreateRenderGraphTexture(
                        graph,
                        descriptor,
                        "Bronze X-Ray mask",
                        true,
                        FilterMode.Bilinear);
                    using (var builder = graph.AddRasterRenderPass<MaskData>("Bronze X-Ray silhouette", out var data))
                    {
                        data.Renderers = entry.Renderers;
                        data.Material = settings._material;
                        data.CullingMask = camera.cullingMask;
                        builder.SetRenderAttachment(mask, 0, AccessFlags.Write);
                        // No scene depth attachment: silhouettes remain complete behind walls.
                        builder.SetRenderFunc(static (MaskData pass, RasterGraphContext context) =>
                        {
                            foreach (var renderer in pass.Renderers)
                            {
                                if (!renderer || !renderer.enabled || !renderer.gameObject.activeInHierarchy
                                    || renderer.forceRenderingOff
                                    || (pass.CullingMask & (1 << renderer.gameObject.layer)) == 0)
                                {
                                    continue;
                                }

                                var mesh = MeshOf(renderer);
                                if (!mesh)
                                {
                                    continue;
                                }

                                for (int submesh = 0; submesh < mesh.subMeshCount; submesh++)
                                {
                                    context.cmd.DrawRenderer(renderer, pass.Material, submesh, 3);
                                }
                            }
                        });
                    }

                    using (var builder = graph.AddRasterRenderPass<CompositeData>("Bronze X-Ray outline", out var data))
                    {
                        data.Mask = mask;
                        data.Material = settings._material;
                        data.Properties = properties;
                        data.Rect = rect;
                        data.Color = entry.Observer.OutlineColor;
                        data.Width = entry.Observer.OutlinePixels;
                        data.Texel = new Vector4(
                            1f / descriptor.width,
                            1f / descriptor.height,
                            descriptor.width,
                            descriptor.height);
                        builder.UseTexture(mask, AccessFlags.Read);
                        builder.SetRenderAttachment(resources.activeColorTexture, 0, AccessFlags.ReadWrite);
                        builder.SetRenderFunc(static (CompositeData pass, RasterGraphContext context) =>
                        {
                            context.cmd.EnableScissorRect(pass.Rect);
                            RTHandle source = pass.Mask;
                            pass.Properties.SetTexture(TextureId, source);
                            pass.Properties.SetVector(ScaleId, new Vector4(1, 1, 0, 0));
                            pass.Properties.SetVector(TexelId, pass.Texel);
                            pass.Properties.SetColor(ColorId, pass.Color);
                            pass.Properties.SetFloat(WidthId, pass.Width);
                            context.cmd.DrawProcedural(Matrix4x4.identity, pass.Material, 4, MeshTopology.Triangles, 3, 1, pass.Properties);
                            context.cmd.DisableScissorRect();
                        });
                    }
                }
            }
        }

        private sealed class OutlinePass : ScriptableRenderPass
        {
            private readonly OutlineRendererFeature settings;
            private readonly Plane[] planes = new Plane[6];
            private // Reused per draw, just like Blitter's shared block. No per-object materials.
 readonly MaterialPropertyBlock compositeProperties = new MaterialPropertyBlock();
            private static readonly int BlitTextureId = Shader.PropertyToID("_BlitTexture");
            private static readonly int ScaleBiasId = Shader.PropertyToID("_BlitScaleBias");
            private static readonly int TexelSizeId = Shader.PropertyToID("_BlitTexture_TexelSize");
            private static readonly int ColorId = Shader.PropertyToID("_OutlineColor");
            private static readonly int WidthId = Shader.PropertyToID("_OutlinePixels");
            sealed class MaskData
            {
                public Renderer[] Renderers;
                public Material Material;
                public int CullingMask;
            }

            sealed class CompositeData
            {
                public TextureHandle Mask;
                public Material Material;
                public Rect Scissor;
                public MaterialPropertyBlock Properties;
                public Color Color;
                public float Width;
                public Vector4 TexelSize;
            }

            public OutlinePass(OutlineRendererFeature owner)
            {
                settings = owner;
                renderPassEvent = RenderPassEvent.AfterRenderingSkybox;
            }

            private Rect ScreenRect(Camera camera, Bounds bounds, int width, int height)
            {
                if (camera.stereoEnabled)
                {
                    return new Rect(0, 0, width, height);
                }

                Vector2 min = new Vector2(float.PositiveInfinity, float.PositiveInfinity);
                Vector2 max = new Vector2(float.NegativeInfinity, float.NegativeInfinity);
                for (int i = 0; i < 8; i++)
                {
                    var corner = bounds.center + Vector3.Scale(
                        bounds.extents,
                        new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                    var v = camera.WorldToViewportPoint(corner);
                    // Near-plane crossings need a conservative full-screen rectangle.
                    if (v.z <= camera.nearClipPlane)
                    {
                        return new Rect(0, 0, width, height);
                    }

                    var pixel = new Vector2(v.x * width, v.y * height);
                    min = Vector2.Min(min, pixel);
                    max = Vector2.Max(max, pixel);
                }

                float margin = Mathf.Ceil(settings._widthPixels) + 2;
                return Rect.MinMaxRect(
                    Mathf.Clamp(Mathf.Floor(min.x - margin), 0, width),
                    Mathf.Clamp(Mathf.Floor(min.y - margin), 0, height),
                    Mathf.Clamp(Mathf.Ceil(max.x + margin), 0, width),
                    Mathf.Clamp(Mathf.Ceil(max.y + margin), 0, height));
            }

            public override void RecordRenderGraph(RenderGraph graph, ContextContainer frameData)
            {
                var resources = frameData.Get<UniversalResourceData>();
                var cameraData = frameData.Get<UniversalCameraData>();
                var camera = cameraData.camera;
                GeometryUtility.CalculateFrustumPlanes(camera, planes);
                settings.LastEligibleTargets = settings.LastRenderedTargets = 0;
                settings.LastCompositeScreenAreas = 0;
                settings._material.SetColor("_OutlineColor", settings._color);
                settings._material.SetFloat("_OutlinePixels", settings._widthPixels);
                var descriptor = cameraData.cameraTargetDescriptor;
                descriptor.graphicsFormat = UnityEngine.Experimental.Rendering.GraphicsFormat.R8G8_UNorm;
                descriptor.depthStencilFormat = UnityEngine.Experimental.Rendering.GraphicsFormat.None;
                descriptor.depthBufferBits = 0;
                descriptor.bindMS = false;
                foreach (var target in OutlineTarget.ActiveTargets)
                {
                    if (!target || (target.Camera && target.Camera != camera) || target.Renderers == null)
                    {
                        continue;
                    }

                    settings.LastEligibleTargets++;
                    bool found = false;
                    Bounds bounds = default;
                    foreach (var renderer in target.Renderers)
                    {
                        if (!renderer || !renderer.enabled || !renderer.gameObject.activeInHierarchy
                            || (camera.cullingMask & (1 << renderer.gameObject.layer)) == 0
                            || !GeometryUtility.TestPlanesAABB(
                            planes,
                            renderer.bounds))
                        {
                            continue;
                        }

                        if (!found)
                        {
                            bounds = renderer.bounds;
                            found = true;
                        }
                        else
                        {
                            bounds.Encapsulate(renderer.bounds);
                        }
                    }

                    if (!found)
                    {
                        continue;
                    }

                    var rect = ScreenRect(camera, bounds, descriptor.width, descriptor.height);
                    if (rect.width <= 0 || rect.height <= 0)
                    {
                        continue;
                    }

                    settings.LastRenderedTargets++;
                    settings.LastCompositeScreenAreas += rect.width * rect.height / ((float)descriptor.width * descriptor.height);
                    // Independent masks preserve foreground borders and avoid occluder-cut edges.
                    // RenderGraph can recycle these short-lived textures between targets.
                    var mask = UniversalRenderer.CreateRenderGraphTexture(
                        graph,
                        descriptor,
                        "Outline mask",
                        true,
                        FilterMode.Bilinear);
                    using (var builder = graph.AddRasterRenderPass<MaskData>("Outline silhouette", out var data))
                    {
                        data.Renderers = target.Renderers;
                        data.Material = settings._material;
                        data.CullingMask = camera.cullingMask;
                        builder.SetRenderAttachment(mask, 0, AccessFlags.Write);
                        builder.SetRenderAttachmentDepth(resources.activeDepthTexture, AccessFlags.Read);
                        builder.SetRenderFunc(static (MaskData pass, RasterGraphContext context) =>
                        {
                            foreach (var renderer in pass.Renderers)
                            {
                                if (!renderer || !renderer.enabled || !renderer.gameObject.activeInHierarchy
                                    || (pass.CullingMask & (1 << renderer.gameObject.layer)) == 0)
                                {
                                    continue;
                                }

                                var mesh = renderer is SkinnedMeshRenderer skin ? skin.sharedMesh : renderer.GetComponent<MeshFilter>()?.sharedMesh;
                                if (!mesh)
                                {
                                    continue;
                                }

                                for (int submesh = 0; submesh < mesh.subMeshCount; submesh++)
                                {
                                    context.cmd.DrawRenderer(renderer, pass.Material, submesh, 0);
                                    context.cmd.DrawRenderer(renderer, pass.Material, submesh, 2);
                                }
                            }
                        });
                    }

                    using (var builder = graph.AddRasterRenderPass<CompositeData>(
                        "Outline local composite",
                        out var data))
                    {
                        data.Mask = mask;
                        data.Material = settings._material;
                        data.Scissor = rect;
                        data.Properties = compositeProperties;
                        // Capture a value per pass; mutating the shared material here would leak colors.
                        data.Color = target.GetOutlineColor(settings._color);
                        data.Width = settings._widthPixels;
                        data.TexelSize = new Vector4(
                            1f / descriptor.width,
                            1f / descriptor.height,
                            descriptor.width,
                            descriptor.height);
                        builder.UseTexture(mask, AccessFlags.Read);
                        builder.SetRenderAttachment(resources.activeColorTexture, 0, AccessFlags.ReadWrite);
                        builder.SetRenderFunc(static (CompositeData pass, RasterGraphContext context) =>
                        {
                            context.cmd.EnableScissorRect(pass.Scissor);
                            RTHandle source = pass.Mask;
                            pass.Properties.SetTexture(BlitTextureId, source);
                            pass.Properties.SetVector(ScaleBiasId, new Vector4(1, 1, 0, 0));
                            pass.Properties.SetVector(TexelSizeId, pass.TexelSize);
                            pass.Properties.SetColor(ColorId, pass.Color);
                            pass.Properties.SetFloat(WidthId, pass.Width);
                            // Same full-screen triangle as Blitter, with a per-draw color override.
                            context.cmd.DrawProcedural(Matrix4x4.identity, pass.Material, 1, MeshTopology.Triangles, 3, 1, pass.Properties);
                            context.cmd.DisableScissorRect();
                        });
                    }
                }
            }
        }
    }
}
