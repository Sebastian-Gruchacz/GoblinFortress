# Goblin Stronghold — development

## Active foundation

The active implementation is the modern solution:

```text
GoblinStronghold.slnx
```

The historical `Goblin Fortress.sln`, XNA project and d20 libraries remain in the repository only until the archival reset is performed. New code must not reference them.

The current projects are:

- `src/GoblinStronghold.Simulation` — pure deterministic simulation and map generation;
- `src/GoblinStronghold.Godot` — thin Godot 4.7.2 .NET presentation probe;
- `src/GoblinStronghold.Headless` — executable validation scenario with no renderer;
- `tests/GoblinStronghold.Simulation.Tests` — deterministic unit and scenario tests.

All active projects target `net10.0` and C# 14. The repository pins the .NET 10 SDK family through `global.json` while allowing newer .NET 10 feature bands.

The Godot 4.7.2 .NET desktop integration targets .NET 10 and has been verified against the locally installed Windows x64 editor and its headless host.

The game targets desktop PC only. Windows x64 is the primary development platform; Windows x64 and Linux x64 are intended release targets. Linux ARM64 and Raspberry Pi are experimental smoke-test targets without a performance promise. Mobile, web and console exports are outside scope.

The simulation and headless projects must avoid Windows-specific APIs. Linux x64 export and the experimental Linux ARM64/Raspberry Pi renderer and graphics stack still require end-to-end verification.

## Commands

Restore dependencies:

```powershell
dotnet restore .\GoblinStronghold.slnx
```

Build the active solution:

```powershell
dotnet build .\GoblinStronghold.slnx --no-restore -c Release
```

Run tests:

```powershell
dotnet test .\GoblinStronghold.slnx --no-build --no-restore -c Release
```

Run the headless scenario:

```powershell
dotnet run --project .\src\GoblinStronghold.Headless\GoblinStronghold.Headless.csproj --no-build --no-restore -c Release
```

Build and open the Godot sandbox with the detected local editor:

```powershell
.\run-goblin-stronghold.ps1
```

The sandbox starts with one food stockpile at the goblin spawn. Left-click a visible goblin to select it, then right-click any map cell to order a move, including a scouting move into unknown territory. Left-click terrain to clear the selection. With no goblin selected, right-click a known reachable cell to order another food stockpile. Left-click also inspects terrain, actors, loose stacks and stored quantities.

## Deterministic contract

- Simulation time advances through fixed integer ticks.
- Commands target future tick boundaries and are ordered by an explicit sequence.
- Random samples are keyed by world seed, subsystem domain, stable entity, tick and sample key.
- Simulation detail and update frequency never consume a mutable RNG stream; future coarse systems use stable logical intervals or scheduled-event identities.
- Render speed and snapshot frequency do not participate in authoritative state.
- State hashes exclude the event delivery buffer, so observing or draining events cannot alter the world.
- Saves include pending commands and undelivered events.
- Normal, accelerated and unthrottled runners must agree for the same command schedule.

Timing metrics are diagnostic and deliberately excluded from saves and state hashes.

## Current implemented slice

The foundation currently provides:

- stable simulation ticks and entity identifiers;
- deterministic, domain-separated random samples;
- scheduled forage commands and autonomous forage jobs;
- a minimal goblin hunger and physical-food loop;
- ordered event delivery;
- immutable presentation snapshots;
- canonical state hashing;
- JSON save and load;
- pause and normal, 2x, 4x, 8x and unthrottled runner modes;
- lightweight execution metrics;
- deterministic swamp terrain generation;
- moisture, fertility and traversal costs;
- surface coordinates that reserve a height component;
- validated goblin and human settlement sites;
- traversability queries and map fingerprints;
- generated-map ownership in snapshots, hashes and saves;
- versioned map generation recorded in snapshots, fingerprints, hashes and saves;
- a mutable runtime vegetation layer above the immutable generated baseline;
- deterministic initial berry patches, local biomass depletion and interval-based regrowth;
- ordered per-cell world-change deltas with an independent delivery buffer;
- stable multi-cell world objects with anchors, orientations and relative 3D parts;
- independent surface, solid, overhead and subsurface occupancy channels;
- deterministic human cottages, barn and well plus two or three goblin huts;
- structure-aware surface traversal that keeps generated settlement access open;
- physical resource stacks with stable identifiers;
- ground, actor-inventory and storage-zone ownership;
- partial stack pickup and actor carry capacity;
- typed single-cell storage zones with capacity;
- deterministic pickup and storage commands;
- soft rejection of stale or impossible queued commands;
- deterministic selection of distinct berry patches by idle goblins;
- structure-aware cardinal routes, visible cell-by-cell travel and timed gathering work;
- autonomous two-leg hauling with timed loading and unloading;
- deterministic reservations that prevent double-claiming item quantities and storage capacity;
- recovery of carried goods when a delivery is interrupted or its destination becomes unavailable;
- individual fatigue accumulated during work and travel;
- survival-priority rest jobs that route tired goblins into reachable goblin huts;
- hunger-driven meal jobs that reserve and fetch one physical food portion from ground or storage;
- survival gathering when a hungry goblin cannot find an unreserved prepared meal;
- critical hunger interruption for rest and haul collection when a reachable meal exists;
- starvation health damage, actor death and deterministic release of work reservations;
- cancellation of dead actors' pending commands and recovery of carried items on the ground;
- persistent unknown, explored and currently visible fog-of-war states;
- deterministic vision updates stored in snapshots, hashes and saves;
- one autonomous explorer selecting the nearest reachable edge of unknown terrain;
- autonomous job and reservation state included in snapshots, hashes and saves;
- one authoritative coarse human village with population, food, wood and goods stocks;
- stable farmer, worker and guard cohorts whose populations sum to the village population;
- fixed daily village production and consumption plus deterministic local cohort movement;
- village state and cohort positions included in snapshots, hashes and saves;
- human cohort markers and village inspection concealed by the same fog of war as the terrain;
- deterministic human detection of nearby goblin intruders and persistent village hostility;
- guard pursuit constrained to the village activity area;
- simultaneous interval-based close combat between the guard cohort and adjacent goblins;
- combat damage, goblin deaths and human guard casualties reflected in both communities;
- alert, combat-hit and human-death events preserved across save and load;
- player-issued single-goblin move commands with structure-aware cell-by-cell routes;
- ordered movement into unknown territory without revealing whether its destination is traversable;
- soft execution-time rejection of unreachable or impassable scouting destinations;
- ordered routes, targets and completion events included in saves and deterministic state;
- Godot selection outlines, movement target lines and right-click scouting orders;
- two-portion personal food and water capacities independent from carried work cargo;
- physical transfer of food from world stacks into personal provisions and recovery on death;
- shallow-water refill jobs for primitive personal containers;
- individual thirst, automatic drinking and dehydration damage;
- slower authoritative actor movement at one cell per second in normal speed;
- non-authoritative Godot interpolation that keeps `1×` movement continuous and scales with acceleration;
- overhead intent icons for forage, haul collection/delivery, rest, eating, exploration, ordered movement and food/water resupply.

Manual transport commands still resolve atomically after a path check. Autonomous foraging and hauling use explicit routes and action duration. The current hauler selects the shortest valid source/destination pair and respects filters, carry capacity and physical reservations; it does not yet implement player-defined local/global priorities, reserve floors or workshop demand.

`GeneratedMap` is the immutable generated baseline owned by the simulation. Its generator version is included in snapshots, fingerprints, state hashes and saves; unsupported versions fail explicitly instead of silently regenerating another landscape. `WorldMapState` now owns the first mutable overlay: berry patches with biomass and capacity. Harvest and regrowth advance the world version and publish dirty-cell deltas without changing the baseline fingerprint.

The vegetation prototype deliberately has one plant kind, no seasons and no habitat spread. Later world-mutation slices add coordinate-addressed region generation, ground and construction overlays, per-domain topology versions and larger dirty regions. Rest currently treats any reachable floor or doorway in a goblin hut as unlimited primitive sleeping space; beds, occupancy, comfort and shelter quality are deferred. Health currently models starvation and dehydration damage only. Fog uses a circular radius without line-of-sight or lighting occlusion, and the explorer uses authoritative topology to select a nearby frontier. Corpses, wounds, sickness, healing, stealth and threat-driven safety jobs remain later slices.

The first human village model is deliberately coarse and authoritative rather than a second full actor simulation. Its three moving map markers represent work cohorts, not twelve individually simulated humans. Daily food, wood and goods changes are fixed and do not yet react to seasons, weather, damaged facilities or shortages. The first combat probe treats the guards as one aggregate health pool: it has no weapons, armor, wounds, poison, surrender, capture, corpses, retreat or tactical orders yet. Hostility currently persists once raised and only living guards detect and pursue intruders. Observation-driven materialization of notable individuals and reports about knowledge carriers belong to the next slices.

Manual movement currently controls one selected goblin and one destination. It has no box selection, groups, waypoints, formations, explicit attack order or path preview. A critical survival need may interrupt the order, while a carried item remains physically attached and is delivered after the move when possible. Unknown impassable destinations are accepted without leaking terrain knowledge and rejected softly when the command executes.

Personal food remains part of the physical tribal total even while packed by a goblin. Personal water currently represents a filled primitive container and is gathered from traversable shallow water without depleting the water body; container crafting, water volume, contamination and boiling are deferred. The initial containers begin filled. There is no cleaning job yet, so the intent-icon vocabulary covers only implemented work and will gain a cleaning symbol with the corresponding physical dirt/refuse system.

Godot interpolates actor markers between authoritative cells but never feeds presentation positions back into simulation, saves, visibility or combat. At `1×`, the current movement cadence is one cell per second. Higher speeds scale the visual catch-up rate and may still skip when the renderer cannot present every simulation step, which is intentional.

Generated structures currently validate the spatial contract but are static: they have no materials, construction progress, damage, fire or functional rooms yet. Trees remain single-cell vegetation patches, and bridges, streams and river networks are not generated. Those systems reuse the footprint and occupancy model instead of extending `TerrainKind` for every possible combination.
