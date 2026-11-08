# Combat prototype

Open Assets/Scenes/MetalPushPullTest.unity. The existing metal tests, camera and renderer feature are preserved. Three red TestEnemy prefab instances share the baked Combat Navigation surface. The blue player uses primitive visuals; visual-only sword children have no colliders.

Controls: WASD / Space retain movement and jumping; left click attacks; F toggles enemy lock-on. RMB + wheel and Tab retain metal controls. RMB takes priority over sword attacks and cancels an in-progress swing. Enemy lock-on rotation yields to manipulation that moves the player, so wall facing remains intact. Metal aiming temporarily takes camera priority. A used RMB hold still cannot re-enter selection until released and pressed again.

Player components: existing Rigidbody, CapsuleCollider, PlayerController, PlayerMovement, inventory/selector/manipulator/metal indicator; new PlayerHealth, PlayerCombat, PlayerTargetLockOn. PlayerCombat.SwordPivot points to Combat Model/Sword Pivot; lock-on ViewCamera references the existing Main Camera. InputActions remains the existing asset, with Player/LockOn bound to F and existing Player/Attack bound to left mouse. Health defaults to 100; inspect PlayerHealth.CurrentHealth during play (no health UI).

Enemy prefab: Assets/Combat/TestEnemy.prefab contains Enemy, TestEnemy, NavMeshAgent, CapsuleCollider and a kinematic Rigidbody. It finds the Player by tag if TestEnemy.Player is not assigned. Scene instances already reference the PlayerHealth. Default damage 8, cooldown 1.3s, detection 12m, attack range 1.8m. Enemy health 100; player sword damage 25, range 2.5m, angle 65 degrees from forward, cooldown .65s, swing .45s. The active damage window is normalized .3 to .65 of the swing; every enemy is hit at most once per swing, even with child colliders. Line-of-sight is checked for melee and enemy attacks as well as lock selection. Enemy death raises Died, stops AI/agent, unlocks the player, and destroys the object after 2 seconds. Death cannot be healed.

NavMesh is already baked and saved in Assets/Combat/CombatNavMesh.asset. Combat Navigation collects child colliders (ground and fixed metal walls); dynamic metals have carving NavMeshObstacle components and remain free to push/pull. After changing fixed geometry, select Combat Navigation > NavMeshSurface > Bake. If baking again, save the scene and generated NavMesh asset. New enemy spawns must be on this NavMesh. No runtime NavMesh baking is required.

Verified in Unity Play Mode using synthetic Input System events, direct component calls, manual physics stepping and the real swing coroutine:
- All 3 agents on NavMesh; live Wander/Chase paths observed.
- Wander/Chase/Attack transitions; enemy melee deducted 8 player HP.
- Enemy damage/heal clamping; death and death-triggered lock release.
- F input locked/unlocked; obstruction blocked selection; out-of-range target unlocked.
- Locked strafe velocity ~6 while facing target (angle error ~1.6 degrees in moving test).
- Sword swing rotated ~95 degrees; an enemy with two colliders lost exactly 25 HP.
- Attack cooldown rejected repeated attack; left-click action started the swing.
- RMB + LMB selected metal without sword attack; Iron Pull moved the player and release restored gravity while retaining velocity.
- Shader/renderer feature files were not changed for combat. No C# compilation or gameplay error messages at verification; the MCP connector emitted one transient transport warning during play-mode reload.

Manual playtesting is still useful for camera framing, sword reach and difficulty tuning. No animation controller, combo system, health UI or player respawn is included. Stop/restart Play mode to reset a dead player or enemies.

## Combat tuning update
Lock-on camera now automatically follows the enemy; mouse flicks or the right stick switch visible targets. Unlocked camera remains free, and RMB metal aiming temporarily overrides lock camera. WASD is relative to the player-to-enemy direction during lock-on. During a sword swing, grounded WASD movement is stopped; airborne WASD steering is suppressed while gravity and launch momentum remain physical. Movement resumes when the swing finishes/cancels. Hits knock TestEnemy back through NavMeshAgent.Move, with AI movement/attacks paused during knockback. PlayerCombat exposes KnockbackSpeed (8) and KnockbackDuration (.3). Verified: mouse changes camera while locked, swing horizontal speed 0, movement resumes after swing, hit deducts 25 HP and moves an enemy about 1.28m while keeping it on NavMesh.

Lock-camera verification: W/S produced +6/-6 velocity toward the enemy; A/D produced -6/+6 lateral velocity, even with a manually displaced camera. Right-stick switched targets once per gesture, and a synthetic Gamepad event confirmed the actual input route. Target projected to viewport (.50,.60) and player to (.50,.27), keeping the player lower in the frame. LockCameraSmoothTime and LockCameraPitch tune tracking; PlayerTargetLockOn exposes switch cooldown and mouse/stick thresholds.

## Toggle sprint, air drag and enemy punch
The existing Player/Sprint action (Left Shift / gamepad left-stick press) now toggles sprint on press; releasing keeps the mode, the next press toggles it off. PlayerMovement exposes SprintSpeed (10) in addition to MovementSpeed (6), and CurrentMovementSpeed / IsSprinting for code. Attack movement lock and metal manipulation still take precedence over walking/running.

AirDeceleration is now 8 and also applies to released metal momentum when airborne with no movement input. Only horizontal velocity is affected; gravity/vertical speed are unchanged. Same-direction input retains high launch speed; opposite input still uses AirReverseDeceleration (15). Test results from horizontal speed 30 after 1s: idle 22, forward 30, reverse 15. Shift input test: first press speed 10, holding/releasing did not toggle, second press speed 6.

TestEnemy prefab now includes Enemy Model/Attack Pivot with a visual-only arm and fist. TestEnemy.AttackPivot points to that instance child. AttackDuration defaults .6s and AttackHitTime .5 (normalized). The arm winds back, punches forward and recovers; damage occurs once at the hit time with a fresh distance and visibility check. The NavMeshAgent stays stopped during the attack. Brass, knockback, disable and death cancel the coroutine and restore the arm. No new Animator is required and the attack does not move the root/body.

Verified punch rotation ~85 degrees; first damage in the latter half of the cycle (one hit, 8 damage); no instant windup damage; out-of-range damage blocked; Brass/knockback/death interrupted the attack. All 3 scene enemy arm references resolve to their own instance children. A paused impact pose screenshot was checked in Temp/SprintAttackVerification. Scene/prefab parameters are saved; no manual references or NavMesh rebake are required.

Player manipulation release now has a one-shot brake: PlayerMetalManipulator.ReleaseSpeedMultiplier defaults to .5 (remaining speed fraction). Releasing RMB while the ability controls the player queues one velocity scaling on the next FixedUpdate, restores gravity, and retains the remaining momentum. Repeated released frames do not repeatedly brake. Moving a lighter metal does not brake either the player or that metal; automatic range/inventory interruption retains its prior behavior. Isolated component tests: Push/Pull 30 -> 15, no repeat scaling, gravity restored; light-metal release velocities unchanged.

## Lock-camera obstacle correction
Camera framing still looks between player and target, but physical camera collision now casts from the player-side anchor, not the interpolated framing focus. This prevents a focus point inside a pillar from moving the physical camera into that pillar. Character colliders remain excluded. Initial zero-distance sphere hits are ignored to avoid false collapses when the cast sphere brushes nearby geometry; a center ray still checks real obstructions along the camera boom. Retraction remains immediate for clipping safety, outward recovery is limited once per frame. Inspector settings on PlayerController: CameraCollisionRadius (.25), CameraCollisionPadding (.08), CameraCollisionReturnSpeed (6), CameraCollisionMask (all layers).

Reproduction before fix: a solid cube at the lock focus caused ~7.93m of camera jump and placed the camera inside the cube. After fix, a focus blocker caused 0 movement and no penetration. A wall behind the player retracted the camera without penetration, keeping it behind the player. Removing it restored .12m on the first .02s step and no second movement in the same frame, then fully returned to the unobstructed pose. A side obstacle overlapping the initial cast sphere did not collapse the camera. No C# / gameplay Console errors during checks. Temporary blockers were discarded on leaving Play Mode.
