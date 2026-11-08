# Copper / Bronze

The Player in MetalPushPullTest and TestEnemy prefab include CopperSuppression
and BronzeDetection. New PlayerMetalInventory / Enemy actors also ensure these
services exist at runtime. Existing metal toggles, amounts and legacy UsingMetal
values are preserved.

## Inspector

- Player: enable Copper or Bronze in PlayerMetalInventory.Metals; CurrentAmount
  must be greater than zero. Neither ability consumes reserves.
- Enemy: add MetalReserve entries to Enemy.MetalAbilities, selecting a Type,
  IsEnabled and CurrentAmount. Multiple entries may be active at once. Add an
  enabled Bronze entry to grant Bronze detection, or Copper to emit a cloud.
- Enemy.UsingMetal remains a compatible single-metal fallback. A reserve entry
  overrides that fallback for its own Type, including disabled or empty entries.
- CopperSuppression.CopperRadius: default 8 world units. Clouds cover their owner
  and all signal actors within a sphere, using actor collider centers when present.
  Multiple clouds form a union. Death, disable, an empty reserve or a toggle are
  evaluated live; disabling one cloud does not cancel another.
- BronzeDetection.DetectionRadius: default 60 world units. Player ShowOutlines is
  enabled and DetectionCamera references the existing gameplay camera. Enemy
  ShowOutlines is off; its detection still works through the shared API.
- BronzeDetection.OutlineColor / OutlinePixels control the separate amber X-Ray
  channel. Outlines are scoped to the observer camera, not every game camera.

## Shared status and AI

PlayerMetalInventory and Enemy implement IMetalAbilityStatus. An actor emits a
signal if any real metal (Iron through Bronze) is enabled and has a positive
amount. Copper itself emits a signal but its own cloud suppresses that signal.
Disabled/dead actors do not emit or protect others. Bronze queries do not perform
line-of-sight raycasts and do not modify physics, colliders, layers or damage.

Enemy.RegisterDetectionSource / HasAbilityDetection combine ability detection
providers. BronzeDetection registers itself; TestEnemy consumes that API in TickAI.
Only TestEnemy chooses Wander/Chase/Attack. Losing an ability-only track behind a
wall immediately releases that Chase; ordinary visual acquisition/memory remains
independent. Clear line of sight is still required to start/land a normal attack.
Zinc and Brass keep their existing action API and priority.

Enemy's Metal Reactions exposes On Zinc Affected and On Brass Affected as standard
UnityEvents. Press +, drag a GameObject/component into the slot, and select a public
method, just like Button.OnClick. The existing TestEnemy prefab and scene instances
bind TestEnemy.OnZinc / OnBrass there; replace those entries to customize reactions.
TestEnemy no longer registers those callbacks in code. RegisterAction and
UnregisterAction remain available for additional runtime code reactions. Empty or
disabled Inspector entries do not count as successful activation by themselves.

## Rendering isolation

OutlineRendererFeature keeps the original metal passes 0/1/2 unchanged. Bronze
adds passes 3/4 in the same referenced shader: an unconditional full silhouette
without scene depth, then an exterior-edge composite after transparent geometry.
It does not use the original visible-mask channel. BronzeOutlineRegistry owns
camera-scoped leases independently of OutlineTarget.ActiveTargets, _Outlined,
renderer material assignments and MaterialPropertyBlocks. Bronze OFF therefore
cannot clear MetalDisplayer's outline or highlighting.

## Verification

Tools > Metal Abilities > Integrate Copper and Bronze applies the scene/prefab
setup idempotently. Tools > Metal Abilities > Run Copper Bronze Checks runs the
Editor harness in Play Mode. It renders an actual opaque-wall scene and reads
amber/cyan pixels, checks Copper self/other/overlap/empty/toggle/range cases, then
uses a real TestEnemy prefab on the scene NavMesh for through-wall Chase, signal
loss/recovery, ordinary sight/attack, Zinc/Brass and inventory/Pewter regressions.

The harness writes Temp/CopperBronze/results.txt and exits Play Mode. Its test
objects/materials/render textures are removed. A nonempty
Temp/CopperBronze/request.txt queues the same run after the next Editor import;
this supports testing when an MCP editor session needs reconnecting. A queued
request is not a passing test result. Compilation can separately be checked with
the project's Unity Roslyn response files without overwriting Library assemblies.

Verified in Unity 6000.3.24f1 on 2026-11-01: all 45 Play Mode checks passed,
including serialized Zinc/Brass Inspector reactions, disabled events and the
compatible runtime registration API.
The wall test rendered 860 amber X-Ray outline pixels, and the independent
MetalDisplayer outline retained 1,166 cyan pixels before/after Bronze OFF.
Unity reported no script compilation or shader errors and no Console warnings
or errors during the completed run. The Editor returned to Edit Mode.
