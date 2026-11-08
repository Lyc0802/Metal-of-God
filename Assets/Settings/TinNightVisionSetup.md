# Tin night vision renderer setup

`PC_Renderer` stays Forward+ with its original SSAO and Outline features.
`Tin Surface Night Vision` is camera-specific and runs only when Tin is usable.
It reads Lit materials' own GBuffer passes using the existing visible depth, then
fills missing diffuse illumination before transparent effects and UI render.
No exposure, scene lights, material instances or volume settings are changed.

`PC_RPAsset` also references `TinMaterialBufferRenderer` as a secondary, unused
Deferred renderer. Keep this reference: URP otherwise strips every Lit GBuffer
shader pass from Forward-only player builds. The default renderer remains index 0;
no cameras should be assigned to the secondary renderer.

PlayerTinAbility Inspector controls reference intensity (hard-capped at 2),
minimum surface lighting, retained shadow contrast and effect strength.
The assigned directional light supplies direction and color, never its live intensity.
Tin off, zero inventory, disabled component and player death bypass the render pass.
Sound visualization continues to use the original AudioSource-driven edge HUD.

Scope: opaque URP Lit/Simple Lit materials with a UniversalGBuffer pass. Unlit,
transparent VFX, sky and overlay UI preserve their original rendering. Custom shaders
need a UniversalGBuffer pass to participate in the material-based night vision.
Editor Play Mode was tested; an executable player build has not been run.
