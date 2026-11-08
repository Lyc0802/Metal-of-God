# Tin / Pewter prototype

Open Assets/Scenes/MetalPushPullTest.unity. The existing Player now has PlayerTinAbility and PlayerPewterAbility. Toggle Tin/Pewter IsEnabled on PlayerMetalInventory during Play mode; CurrentAmount must be positive. There are no new keys and no reserve consumption. Existing inventory values were retained.

## Tin
PlayerTinAbility uses a private camera-local URP Volume / VolumeProfile with ColorCurves for selective shadow enhancement. Fixed +6 EV exposure and global gamma gain have been removed: exposure stays at zero and LiftGammaGain stays neutral. ShadowBoost (0-1, default 1) controls the monotonic master curve; it maps .025 -> .16, .5 -> .57 and .9 -> .904 at full strength, so dark detail is enhanced while bright highlights are nearly unchanged. Black remains black and white remains white. Neutral tonemapping is retained, and camera post-processing/dithering are restored when inactive. Curves rebuild only when strength changes. No scene Light or RendererFeature is modified by toggling Tin.

The test room has an extremely faint neutral ambient color (.025, .025, .025), ambient intensity 1, a fixed dim ceiling point light with soft shadows, and no skybox/reflection lighting. This retains low-light surface information for gamma to reveal; completely black input cannot reveal geometry through gamma. Tin toggling never changes global lighting. Tin OFF is still nearly black. The original near-black floor/enclosure materials remain.

SoundEmitter now requires and references an AudioSource on the source object. It observes actual playback using a reused 256-sample output buffer every .06 seconds, rather than sending animation-only events. Disabled, stopped, paused, muted, zero-volume and silent-output sources do not emit. SilenceThreshold (.0005), ReferenceRms (.08), Strength and interval are adjustable. Play(clip, volume) plays through that same AudioSource; only subsequent non-silent playback generates an event.

PlayerTinAbility converts sound positions into screen-edge wave icons relative to the camera yaw: left/left, right/right, front/top, rear/bottom. Closer sources have larger, brighter indicators; turning the camera updates their direction. SoundDetectionDistance defaults to 30m, SoundPersistence .65s, Minimum/MaximumCueWidth 140/340 pixels and Minimum/MaximumCueDepth 36/96 at 1080p, ScreenEdgeMargin 4, SoundColor amber, SoundPoolSize 16. One shared generated texture and pooled uGUI RawImages are reused; there is no per-sound Instantiate/Destroy. Each source refreshes its own cue instead of stacking duplicates. Cues use rectangle/ray intersection with the top pivot at the border, including diagonal bearings. The position/size/color are set before enabling the image, preventing a first-frame flash at screen center. Three bands grow wider from the outer border toward the center, and the entire cue expands over CueGrowthDuration (.18s) while remaining attached to that border. Inward depth is capped to 16% of the short screen dimension. Tin unavailable immediately hides all cues.

All three scene enemies and Assets/Combat/TestEnemy.prefab have the AudioSource reference, 3D linear rolloff (2-35m), FootstepClip and AttackClip configured. Movement every 1.6m plays the footstep; the attack impact plays the attack sound. Assets/Abilities/Audio contains two simple original prototype WAV clips that can be replaced in TestEnemy's Inspector. For another sound-producing object, add SoundEmitter and assign its own AudioSource; existing Play, PlayOneShot and looping playback are observed automatically.

### Tin revision verification
- Visually inspected neutral gamma ON versus nearly black OFF, without an added runtime Light.
- Actual left and right AudioSource playback produced correctly placed amber screen-edge wave icons; inspected full Game View captures in Temp/TinRevision/sound-left-visible.png and sound-right-visible.png.
- 3m versus 25m produced proximity strengths .9 versus .167; beyond 30m was rejected. Direction updates relative to camera yaw were verified.
- Muted, volume zero, paused, stopped and playing a silent clip all returned no signal. Disabling Tin or setting its reserve to zero hid the Volume and cues immediately.
- Real imported enemy footstep/attack clips play through the assigned AudioSource; sampling audible playback produced cue events. The previous position-only Emit API is no longer used.

## Pewter
PlayerPewterAbility derives current modifiers from Inventory each time; it never overwrites base movement, jump, health or Weight fields. Defaults: movement x1.5 (including sprint), jump x1.4, logical Weight x2 + 20, Strength x1.5 for breakable damage, regeneration 3 HP/s. Current scene base values remain walk 6, sprint 10, jump 10, Weight 80; ON values are 9,15,14,180. Ordinary Enemy sword damage remains the existing AttackDamage (25); Strength multiplies only damage to Pewter-only breakables. Regeneration uses PlayerHealth.Heal and cannot exceed MaxHealth or revive a dead player.

PlayerMovement exposes CurrentMovementSpeed / CurrentJumpForce. PlayerMetalManipulator exposes EffectiveWeight. All existing metal weight comparisons, movement/lock-on rotation ownership and blue/orange metal color comparisons now read EffectiveWeight; the base Weight Inspector value remains unchanged. Other force, range, release-brake and input behavior is retained.

BreakableObstacle contains MaxHealth / CurrentHealth (default 60). Its TakeSwordDamage requires an active PlayerPewterAbility. PlayerCombat uses the existing swing damage window, forward cone, distance and occlusion checks, with a per-swing obstacle hit set. With default 25 damage and Strength x1.5, a valid swing deals 37.5, so two swings break a 60 HP block. Breaking disables colliders/carving and destroys the block. Three test blocks are at the front of the existing test area. These are solid non-metal objects and correctly obstruct ray selection and camera movement. OFF attacks cannot damage them.

## Dark scene
Four solid black enclosure walls and a black ceiling surround the existing floor at its current edges, height 14m. Existing floor/characters/enemies/22 metals/fixed metal walls are retained. Floor/enclosure use a near-black URP Lit material so they can reflect the Tin local fill. The original Directional Light setting is retained; a very dim fixed room point light supplies soft shadows inside the enclosure, reflection intensity is zero, skybox lighting is removed, and the Camera clears to black. Old world-space TextMesh label objects are retained with renderers hidden so their unlit white font does not glow in the dark.

Combat Navigation now references Assets/Abilities/DarkRoomNavMesh.asset, baked with the enclosure. The previous NavMesh asset is retained. The three breakables have carving NavMeshObstacles; destroying them frees their paths without a bake. Select Combat Navigation / NavMeshSurface and Bake after later changes to fixed geometry. Three agents are on NavMesh and no enemy spawn overlaps an enclosure wall. Player spawn and original actor positions remain unchanged.

## Verification
Unity Play Mode component and physics tests plus image inspection passed:
- Pewter OFF/ON values matched 6/9 movement, 10/14 jump, 80/180 Weight; sprint ON became 15. Repeated toggles did not stack. Empty reserve restored baseline immediately. Physics jump velocities were 10 and 14.
- Regeneration increased 50 HP to ~56 over 100 .02s updates, capped at 100, and stopped while OFF.
- A 120-weight metal pulled the player with Pewter OFF and was pulled by the player with Pewter ON. Outline colors changed orange/blue consistently. Iron's enabled/amount guard remained enforced.
- OFF sword and direct breakable API did no damage; ON one swing against duplicate child colliders reduced 60 ->22.5 exactly once, second swing broke it. Normal Enemy HP went 100 ->75 with Pewter ON.
- Original Iron/Steel and Zinc/Brass activated with valid inventory; F lock and sword activation worked. Original 22 metals retained, 3 agents on NavMesh, enemy/enclosure overlaps zero.
- No C# compilation or gameplay Console errors in final checks. No URP RendererFeature code/settings were changed for these abilities.

All components, references, scene geometry and bake are configured. Optional manual work is only tuning Inspector parameters and toggling inventory to compare modes; the darkness is intentional when Tin is unavailable.

## Enemy metal / edge-and-shadow revision
Enemy.UsingMetal now stores the existing MetalType enum (Iron through Bronze plus the existing None / All), default None. The former unused isUsingMetal boolean was replaced; the prefab and three test enemies are saved as None. No MetalType values or inventory indexing were changed.

Verified all 360 horizontal bearings touch a rectangular screen edge; maximum inward depth is 96 pixels at 1920x1080. A newly activated diagonal cue already had its outer pivot on the top boundary before the next Update (536,536 relative to screen center). Full Game View images in Temp/TinEdgeRevision show narrow bands at the outside and progressively wider bands inward, including diagonal sounds on the top edge. Night ON/OFF captures were inspected: ON retains shaded faces and soft contact/cast shadows, OFF remains nearly black.

## Highlight protection update
Inspected bright-scene OFF/ON captures and low-light OFF/ON captures in Temp/TrailTinTuning. Bright surfaces no longer receive a 64x global gain; the low-light test (.005 directional intensity, changed only in Play Mode) still gained visibility. The curve was sampled at 1001 points: zero monotonicity or [0,1] bound violations. Empty Tin disabled the local Volume and sound cues. Test lighting/camera/inventory/time edits were discarded on exiting Play Mode.
