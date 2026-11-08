# Assassin player animation

The existing Assassin Model prefab, Generic avatar, 40 bones, skinned mesh and
materials are retained. Animation edits affect the visual skeleton only; gameplay
Rigidbody movement, Lock-On, combat, metal abilities and the camera keep their
original owners. Root motion stays disabled.

## Reference locomotion

The user-provided references are:

- `Assets/assassin-character-model/Standard Walk.fbx`: 1.167-second walk cycle.
- `Assets/assassin-character-model/Fast Run.fbx`: 0.533-second fast run cycle.

`Editor/AssassinReferenceLocomotion.cs` samples the actual FBX joint motion and
bakes it onto this model as `Animations/Walk.anim` and `Animations/Run.anim`.
The source bone coordinate system is mirrored to match this rig's conventions.
Spine/hips/head motion, arm and leg directions, flexion, foot pitch and cycle
lengths follow the references. Target bone lengths and bind poses are preserved.
Source-local hinge axes keep elbows/knees stable near full extension. Ankle frames
remain stable when running toes point vertically. Horizontal root travel is
removed; sole-height correction fits this model and preserves Run's flight phase.
The cycles are aligned at a left-foot forward contact before blending. Loop
values and derivatives match. There is no runtime retargeting or IK.

The five weighted cloth panels retain independent, delayed travelling waves;
Run adds more trailing flare. Fingers follow the hand bones, and cloth is animated
by bones rather than collision-based simulation. The sword follows RightHand.

## Velocity integration

`PlayerCharacterAnimation` reads actual planar Rigidbody velocity and grounded
state, rather than the sprint button or desired input. Stationary movement uses
Idle; positive grounded velocity selects the legacy `Walk` Animator state, which
now contains the `Velocity Walk Run` 1D Blend Tree:

- At speed 6 or below: Standard Walk.
- Between speeds 6 and 10: smoothly blended Walk/Run.
- At speed 10 or above: Fast Run.

Inspector fields WalkReferenceSpeed and RunReferenceSpeed set these thresholds.
Animator RunBlend is derived from velocity and damped over MovementBlendTime.
WalkRate adjusts locomotion playback around each reference's original cadence,
with separate upper limits (Walk 1.25, Run 1.35). Extreme ability speeds therefore
do not accelerate Idle or attack, or make the feet cycle without a limit.
The clips are in-place; gameplay speeds are unchanged.

## Attack and camera

Idle and Attack assets are preserved when rebuilding locomotion. PlayerCombat
owns the 0.64-second attack clock. AttackProgress samples SwingProgress, with
0.075-second entry and 0.12-second return blending. Hits occur at normalized
0.34-0.58 and the blade trail ends at 0.60. Each enemy can be hit once per swing;
cancellation preserves cooldown. Movement resumes through the velocity Blend Tree.

PlayerController's speed FOV uses actual 3D velocity, including metal-driven
flight. It smoothly adds up to 18 degrees between speeds 6 and 14, retaining the
camera's original 60-degree baseline. Damping advances once per frame. Disable or
camera replacement restores the original FOV. Existing Lock-On framing, camera
collision and selection continue through the same camera update.

## Authoring and verification

Tools > Assassin > Rebuild Walk Animation regenerates both reference locomotion
clips and their Blend Tree. The full rig/animation rebuild also uses the FBXs, so
it cannot silently replace reference motion with the earlier procedural gait.
Original FBXs are read only during authoring.

`PlayerAnimationVerification.VerifyAssets()` checks finite visual-only curves,
loop seams, source durations, distinct Run knee flexion/lean/flight, arm hinge
continuity, wrist limits, visible torso deformation and actual cloth hem vertices.
`BeginPlaySmoke()` checks live torso skinning, velocity-based Walk/Run selection,
acceleration/deceleration, attack entry/return/cancellation, Left Mouse input,
preserved ability components, unchanged animation-owned physics, and speed FOV.
Reports are written to `Temp/AnimationRework`. Test objects and repositioning are
confined to Play Mode. No dynamic previews are generated automatically.
