# Player action particles and sounds

Open Assets/Scenes/MetalPushPullTest.unity. All references are assigned on Player / PlayerActionEffects. Player Action Feedback is an instance of PlayerActionFeedback.prefab. The existing controls, damage, movement, Pewter modifiers, camera and URP RendererFeatures are retained.

## Three action groups
- Sword: the blade now uses SwordSwingTrail, a short world-space ribbon built from the actual Blade Trail Base / Blade Trail Tip transforms on Sword Blade. It samples the physical forward stroke (normalized animation 0-StrikeEnd (default .65)), fades over .085 seconds, and emits no trail during windup or the return motion. This replaces the free camera-facing crescent sprite. The material still uses trace_01.png from Slash VFX Pack. PlayerCombat's original animation and damage window are unchanged. SwingEffectTriggered plays the existing golden sparks and SwordWhoosh.wav once at HitStart; their emitter follows the actual sword tip, including at the moment of emission. CancelAttack clears the mesh and sparks immediately.
- Jump: PlayerMovement raises Jumped only after a grounded jump actually applies its impulse. A cyan burst at the takeoff position plays with JumpTakeoff.wav. Particles simulate in world space, so the burst remains at takeoff while the character rises. An unsuccessful request in midair does not replay it. The burst uses the pack light_01.png texture.
- Pewter: gold rising aura particles and PewterHum.wav run continuously while PlayerPewterAbility.IsActive (enabled component, enabled inventory reserve, positive amount and living player). OFF / zero reserve / disabling the feedback component stops the loop and clears the particles. Repeated active updates do not restart playback or stack emitters. Aura uses the pack light_01.png texture.

There are THREE distinct ParticleSystems (sword sparks, jump burst, Pewter aura) plus the blade-swept ribbon mesh and THREE distinct AudioSources/clips. The original simple prototype WAVs are in Audio/. PewterHum is a periodic four-second waveform for seamless looping. Sources are local player feedback (2D audio), with playOnAwake disabled. Only the Pewter source loops. Replace a clip on the corresponding AudioSource to change its sound. Original Slash VFX Pack assets were not edited.

## Inspector tuning
PlayerActionEffects exposes SwordTrail, SwordTip, three ParticleSystem references, three AudioSource references, SlashDuration (.28 seconds) and SwordPitchVariation. SwordSwingTrail exposes Combat, BladeBase, BladeTip, Lifetime, MaximumSamples and TrailColor. Each ParticleSystem exposes its own color, emission, size and lifetime. AudioSource volume defaults are sword .7, jump .65, Pewter .3. The separate feedback prefab can be edited without changing combat/movement behavior.

## Metal overlay
The scene has a Screen Space Overlay Canvas with eight buttons linked by PlayerMetalInventoryUI directly to PlayerMetalInventory.IsEnabled. ON/OFF/EMPTY and reserve amounts update from inventory state. Esc releases the cursor; clicking outside the panel resumes mouse control without swinging on that first click. UI clicks do not start combat or metal selection. InputSystemUIInputModule uses the existing UI action references in InputSystem_Actions.inputactions. The scene crosshair remains visible and no longer intercepts UI raycasts.

## Verification
- Real sword TryAttack succeeded; a second request during the swing was rejected. One feedback event fired at .15 seconds at a controlled 60Hz step (the .135-second threshold is crossed on the next frame). Sword audio was playing and crescent + sparks had 23 particles.
- A real grounded ReadInput jump raised one Jumped event, played jump audio and emitted 24 particles. A second request in midair did not create another event.
- Pewter ON produced continuous aura particles and a playing looping source. Toggling its actual UI button OFF cleared all aura particles and stopped audio. Zero reserve also prevented playback.
- Disabling PlayerActionEffects stopped all three sources and cleared all particles.
- All eight buttons toggled their reserves and restored their original values correctly. A real Input System virtual mouse press/release processed through InputSystemUIInputModule toggled a button without capturing the cursor or starting an attack; clicking outside captured the cursor without an unintended sword attack.
- Game View captures in Temp/ActionFeedbackVerification were inspected for crescent, cyan takeoff burst, gold aura and overlay layout.
- Final compilation and Play Mode Console checks had no errors or warnings.

All test-only inventory values, time settings and actor changes were discarded on leaving Play Mode. No additional manual setup is required for this scene.

## Blade alignment verification
The former billboard used a fixed point in front of the player and independent screen-space rotation; it did not follow Sword Pivot. That object has been removed from the feedback prefab. The new ribbon is bounded by the blade base and tip in world space, so player scale, camera angle and character rotation do not change the attachment. World positions are converted into the renderer local space only when building the mesh.

Real sword animations were checked after rendering at phases .370, .519 and .667. In both the ordinary camera and a second oblique camera, the newest ribbon edge and spark emitter matched the actual blade markers with reported world-space error 0.000000m. At phase .963, the return motion had zero trail vertices. Cancellation also cleared all vertices and sparks. Images are in Temp/BladeAlignmentVerification (side-0/1/2.png show the blade-aligned stroke). Test-only lighting/camera/inventory/time changes were discarded on leaving Play Mode.

## Wider blade coverage
SwordSwingTrail now adds a soft current-blade cap as well as the history ribbon. BladeHalfWidth defaults to .12m (full geometric width .24m), BasePadding .08m and TipPadding .14m. Both endpoints extend along the actual blade axis; the cap extends to both sides, so it surrounds the entire blade instead of appearing only behind the motion. Texture coordinates use the brighter central region of the original pack trace texture. The tested cap length was 1.3176m around an actual marker span of 1.0976m. See Temp/TrailTinTuning/wide-blade.png. These controls can be adjusted on the Sword Blade Trail component; the original blade markers and animation remain unchanged.

## Single left cut and pack cleanup
PlayerCombat now performs one cut from the resting pose toward -105 degrees, followed by a non-attacking return. There is no opposite-direction windup or alternating cut. SwingDuration is .28s (previously .45s), AttackCooldown .4s (previously .65s), StrikeEnd .65, HitStart .08 and HitEnd .58. SwordPitch defaults to 1.25 for the faster whoosh. The trail and damage window are inactive during the return. The animation was recorded at 60Hz: cut angles decreased monotonically (-19.4, -60.3, -102.9 degrees), with zero rightward-cut violations, one feedback trigger, one 25-damage hit (100 ->75), and completion at .283s.

Slash VFX Pack was pruned from 151 asset files to the two actually referenced textures: Textures/ParticleTextures/SingleParticles/light_01.png and trace_01.png, keeping their original GUIDs/metas and necessary parent folders. Unused sample prefabs, VFX Graphs, shaders, mesh, demo scene and textures were removed. The unreferenced former SwordCrescent.mat test material was also removed. External dependencies and serialized GUID references in Assets/ProjectSettings were audited before/after deletion; no references to removed assets remain. Backup: Temp/SlashVFXPack-before-prune.zip. Audit manifest: Temp/SlashPackPruneManifest.json. Used particle materials/textures and Pewter looping feedback passed checks after pruning. Capture frames: Temp/LeftSwingVerification.
