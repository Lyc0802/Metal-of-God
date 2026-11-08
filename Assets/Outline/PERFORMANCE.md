# Outline performance

The per-target masks remain independent so rear outlined objects cannot erase foreground borders. Full silhouette R and visible silhouette G still prevent outlines along non-metal occluder cuts. Compositing remains after skybox rendering.

Previously each target ran a 16-direction dilation over the whole screen. The optimized feature computes conservative projected renderer bounds, including an outline/bilinear margin, and enables a GPU scissor rectangle only for the composite pass. Off-frustum targets are excluded before allocating/rendering masks. Near-plane crossings and stereo use a conservative full-screen rectangle. The shader skips fully filled interior pixels and samples the RG mask channels together. Per-target temporary renderer lists/arrays were removed; the cached renderer arrays are reused. Scissor state is restored after each draw.

Runtime feature counters LastEligibleTargets, LastRenderedTargets, LastCompositeScreenAreas describe submitted composite area in screen equivalents. They are not frame time or GPU-time measurements; geometry masking, clears, render pass overhead and other game systems still have costs.

Verification in Unity, 2026-10-31:
- 22 metals in the existing test scene: 22 rendered, composite area .3731 screen equivalents instead of 22, about 98.3% less composite area.
- Synthetic stress setup: 122 targets (62 in view, 60 outside), 62 rendered, .4174 screen equivalents instead of 122.
- Before/after 1920x1080 capture of overlapping front/rear outlined cubes with a non-metal occluder against skybox: zero pixels differed by more than 2/255 per RGB channel. Captures are in Temp/OutlinePerformance.
- Shader and C# compile checks passed; runtime Console contained no gameplay errors during checks.
- Temporary test objects were discarded on exiting Play Mode; gameplay scene contents and outline appearance settings were preserved.

## Per-object outline colors

OutlineTarget (including inherited MetalDisplayer) exposes UseCustomOutlineColor and OutlineColor. Unchecked retains the RendererFeature default color, preserving existing scene appearance. Runtime code can call target.SetOutlineColor(color), or update the public fields; rendering reads the color each frame without re-registering the target. Alpha is supported by the existing shader blend.

The composite pass captures each target color as a value and submits it with a reused MaterialPropertyBlock on the same fullscreen triangle used by Blitter. It keeps one shared outline Material, the existing per-object masks, scissor rectangles, shader sampling, early-out and frustum culling. It does not mutate a shared Material color per target during RenderGraph recording, allocate per-target materials, use global shader color state, or add render passes.

Verified per-color update: front/rear overlapping objects rendered distinct red/green outlines, including non-metal occlusion and skybox; default color fallback visually retained the prior result. 122 differently colored targets (62 visible, 60 outside view) retained the same .4174149 composite screen-area count as the previous single-color stress test. Outline Material count remained 1 before/after creating 120 custom-color targets. Shader/C# compilation and runtime Console checks had no errors. These checks establish unchanged rendering work structure, not a measured FPS guarantee.

## Metal weight colors
MetalDisplayer now overrides GetOutlineColor using the cached PlayerMetalManipulator.Weight: lighter blue, heavier orange, equal neutral gray. UseWeightOutlineColors defaults true; disable it to use OutlineTarget custom/global colors. LighterOutlineColor, HeavierOutlineColor, EqualOutlineColor and SelectedOutlineBrightness are editable per metal. RMB selection calls the existing Highlight/Unhighlight API; selected RGB is multiplied by .5 by default while alpha is preserved. Moving off or releasing clears the highlight. Values are evaluated at render recording, so live weight changes update colors without extra materials/passes. Play Mode checks verified correct blue/orange colors, selected color at .5 RGB, release restoration, live player-weight updates and equal-weight gray.

Selected metal outline now brightens instead of darkens: default SelectedOutlineBrightness is 1.6, Inspector range 1 to 3, RGB is multiplied while alpha stays unchanged. Existing loaded scene metals were updated to 1.6.
