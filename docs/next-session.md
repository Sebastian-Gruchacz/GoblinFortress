# Goblin Stronghold — next-session handoff

Updated: 2026-08-29

## Verified boundary

- Active branch: `master`.
- The working tree is intentionally dirty across the accumulated playable-prototype work. It contains both modified and new source, test, documentation and asset files. Do not reset, restore or remove unrelated changes when continuing.
- Save format is 59. Map generator version is 12 and the new-game default is 96×96. The world seed, generator version and immutable-map fingerprint remain part of every save.
- The user's current quicksave was loaded successfully by the headless startup profiler after the latest hill-geometry correction.
- Raised `SolidGround` and `Mud` columns now contain a mineable sandstone/granite volume below their material surface. Exposed fronts use ordinary pickaxe work, produce physical stone and become persistent traversable tunnels. The v12 fingerprint remains compatible with existing saves.
- Generated terrain ramps can now be designated with ordinary rock mining on their lower/current level. Excavation leaves the lower floor in place, removes the climb edge, invalidates topology, produces stone and persists in format 59. Intact ramps seen from one level above are deliberately brighter than other lower terrain.
- Mining candidates no longer rotate past the nearest feasible front. Gesture-start distance becomes the deterministic ordering tie-breaker for work cells, while linear walkways and walls persist as ordered segment groups and unlock delivery/building from the dragged start toward the end.
- Work-area selection has one authoritative target resolver shared by preview and command execution. Filtered gathering keeps only matching physical targets; excavation, scouting and cleaning keep applicable cells; ramp carving is single-cell; clearing orders apply to the whole selected region. Presentation style is centralized in `WorkToolCatalog`.
- Work-area commands issued while paused are applied without advancing simulation time, so their preview does not disappear before the planner can show the order.
- The last full validation completed with 311/311 simulation tests passing. The Godot C# project built with zero warnings and zero errors. `git diff --check` was clean.

## First continuation checks

1. Restart the Godot client so it loads the rebuilt assemblies.
2. Load the current quicksave and perform a short visual smoke test:
   - drag **Kop w skale** through an exposed mud-covered hill and confirm the planner receives the order;
   - let the pickaxe carrier remove the first front and confirm the next selected front becomes feasible;
   - extend a tunnel into a generated terrain ramp and confirm mining it removes only the climb to the upper level;
   - drag a multi-segment walkway from the accessible bank and confirm its segments are supplied and built in gesture order;
   - drag **Zbierz sitowie** across mixed wetland and confirm only live, discovered reed beds remain highlighted;
   - repeat one work designation while paused and confirm it appears immediately.
3. If the smoke test is clean, continue from the roadmap rather than reopening the resolved SolidGround-versus-Mud diagnosis.

## Deliberately unfinished

- Soil is still a thin cover over rock. Separate earth, clay or peat volumes, shovel work, unstable excavated earth walls and reinforcement are not implemented.
- Multi-cell player zones such as fields, larger stockpiles and dumping areas are specified as one object with one identity and a multi-cell footprint, but the existing primitive stockpiles have not yet been converted to that representation.
- The experimental 3D renderer remains parked beside the primary top-down 2D renderer. Fog, full materials and dynamic lighting are not reasons to block current 2D work.
- Deep-faction records are dormant until generation extends below `z=-5`; concrete dark-dwarf fortresses, inhabitants and expeditions are not materialized yet.
- Full fluid simulation, seepage, underground rivers, lava, drainage and pumps remain design commitments rather than completed mechanics.
- Raid aftermath now has persistent corpses, physical loot/recovery paths and dual-source corpse budding foundations, but concrete item identities throughout every inventory, player-selected loot destinations, pursuit/retreat policy, demolition and fire remain later raid slices.

## Useful validation commands

```powershell
dotnet build .\src\GoblinStronghold.Godot\GoblinStronghold.Godot.csproj --no-restore --verbosity minimal
dotnet test .\tests\GoblinStronghold.Simulation.Tests\GoblinStronghold.Simulation.Tests.csproj --no-restore --verbosity minimal
git -c safe.directory=J:/GIT/GoblinFortress diff --check
```
