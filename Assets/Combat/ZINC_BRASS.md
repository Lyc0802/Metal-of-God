# Zinc / Brass enemy manipulation

Scene: Assets/Scenes/MetalPushPullTest.unity. PlayerEnemyManipulator is attached to the existing Player. Existing TestEnemy prefab and three instances use the new action registrations automatically. No new camera, movement controller, input map, outline feature or NavMesh bake is needed.

## Controls
Hold RMB to aim. The first solid collider blocks selection even when its layer is not selectable. Child colliders resolve their parent Enemy. A living Enemy takes precedence over metal classification and is never passed into Iron/Steel weight comparison.
- Metal: wheel up Steel Push; wheel down Iron Pull (unchanged).
- Enemy: wheel up Zinc; wheel down Brass.
- Zinc / Brass require their own PlayerMetalInventory.IsEnabled and positive CurrentAmount, but do not consume reserves.
- Only the first successful action consumes the RMB hold. Unavailable inventory or unregistered actions do not consume it. Release/repress to aim again. Releasing does not cancel an enemy's timer.

The existing focus-circle overlay reads the selected Enemy bounds, using the same texture/mask and radius calculation used for metal. Enemy selections do not populate MetalSurface, so no metal surface disk is displayed. Enemy prefab has no MetalDisplayer or OutlineTarget; the yellow F-lock indicator is independent.

RMB selection has its own target data. Per the later control correction, pressing RMB explicitly releases F lock-on and auto camera tracking, restoring normal player rotation and free camera before ability aiming. Left-click remains blocked while RMB selection/use is active. Previous RMB-consumed-hold behavior for Iron/Steel is retained.

## API and lifecycles
EnemyActionType lives in Enemy.cs with Zinc and Brass IDs. Enemy.RegisterAction(type, handler), UnregisterAction(type, handler), and TryExecuteAction(type) implement dispatch. Repeated registration of the same delegate is ignored. Multiple listeners can share an ID and unregister independently. Dispatch uses a snapshot; destroyed listeners are removed, disabled Behaviour owners are skipped, and dead/disabled Enemy objects cannot dispatch. Missing actions return false. Enemy has no knowledge of AI state or effect rules.

TestEnemy registers OnZinc and OnBrass in OnEnable and unregisters them in OnDisable. Death clears effects, disables navigation/AI, and clears the Enemy action registry. ZincDuration defaults to 8 seconds, BrassDuration to 6; these are editable on TestEnemy and the prefab. IsZincActive, IsBrassActive and EffectTimeRemaining are available for inspection in code.

One nullable action ID plus an absolute expiry time implements effects: Zinc forces Chase even beyond DetectionRange and still permits in-range Attack. Brass forces Wander and prevents Chase/Attack. Same-effect reactivation refreshes its duration; opposite effects replace it. Expiry clears the override and lets the ordinary detection rules run again. Effects are cleared on AI disable/death. Knockback continues to temporarily pause AI decisions without changing the effect expiry time.

## Verification
Unity Play Mode tests used real synthetic Input System mouse events, component calls, agent Warp and timer expiry advanced through reflection (rather than waiting for each duration):
- Action multi-registration, same-handler dedup, unregister, dead-action rejection passed.
- Far baseline Wander -> Zinc forced Chase; Zinc permitted close melee; expired Zinc restored far Wander.
- Brass blocked close Chase/Attack; expired Brass restored normal close Attack.
- Both same-effect refreshes and Zinc/Brass mutual overrides passed.
- AI disable removed registrations; enable re-registered; death cleared timers and stopped navigation.
- Enabled/disabled x empty/positive inventory matrix passed for Zinc and Brass independently; Iron/Steel disabled did not block valid enemy actions.
- First RMB+wheel up invoked Zinc once; further/reverse wheel input during the hold did nothing. Release retained the timed effect and cleared the input target; a new RMB+wheel down invoked Brass. Amounts were unchanged.
- Camera-center ray hit a child collider and selected its parent Enemy; focus bounds were valid; metal indicator was hidden; Enemy had no metal outline component. Focus screenshot captured in Temp/ZincBrassVerification.
- F target remained the same throughout enemy aiming, activation and release. Enemy death still unlocked F.
- Steel still moved light metal; Iron still moved the player toward a heavy wall. Release preserved velocity and restored player gravity.
- Isolated sword regression: one swing at 1.7m reduced Enemy HP from 100 to 75 exactly once at the existing damage window.
- No project C# compilation or gameplay Console errors during successful verification.

Manual tuning is optional: adjust ZincDuration / BrassDuration and existing SelectionDistance. Current scene/prefab references are already configured. No metal consumption, resistances, extra VFX or new buff framework were added.

Control regression after the RMB correction: verified an existing F lock is cleared on RMB press, ability selection begins, mouse turns the camera, and ordinary movement rotation resumes. Zinc/Brass target storage and timed effects are unchanged.
