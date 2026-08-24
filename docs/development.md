# Goblin Stronghold — development

## Active foundation

The active implementation is the modern solution:

```text
GoblinStronghold.slnx
```

The historical `Goblin Fortress.sln`, XNA project and d20 libraries remain in the repository only until the archival reset is performed. New code must not reference them.

The current projects are:

- `src/GoblinStronghold.Simulation` — pure deterministic simulation and map generation;
- `src/GoblinStronghold.Headless` — executable validation scenario with no renderer;
- `tests/GoblinStronghold.Simulation.Tests` — deterministic unit and scenario tests.

All active projects target `net10.0` and C# 14. The repository pins the .NET 10 SDK family through `global.json` while allowing newer .NET 10 feature bands.

Godot 4.6 .NET packages target .NET 8 as their minimum and permit game projects to target newer runtimes. Desktop integration uses .NET 10. The actual Godot host must still be verified when the presentation project is added.

The game targets desktop PC only. Windows x64 is the primary development platform; Windows x64 and Linux x64 are intended release targets. Linux ARM64 and Raspberry Pi are experimental smoke-test targets without a performance promise. Mobile, web and console exports are outside scope.

The simulation and headless projects must avoid Windows-specific APIs. Godot 4.6 publishes .NET editor builds for Linux x64 and ARM64, but the renderer, export template and actual Raspberry Pi graphics stack still require end-to-end verification.

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
- scheduled forage commands;
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
- physical resource stacks with stable identifiers;
- ground, actor-inventory and storage-zone ownership;
- partial stack pickup and actor carry capacity;
- typed single-cell storage zones with capacity;
- deterministic pickup and storage commands;
- soft rejection of stale or impossible queued commands.

Transport currently resolves atomically after a path check: the actor moves to the completed action position without simulating intermediate travel. The next roadmap slice introduces autonomous jobs, action duration and survival priorities without changing item ownership.

`GeneratedMap` is the immutable generated baseline owned by the simulation. Its generator version is included in snapshots, fingerprints, state hashes and saves; unsupported versions fail explicitly instead of silently regenerating another landscape. `WorldMapState` now owns the first mutable overlay: berry patches with biomass and capacity. Harvest and regrowth advance the world version and publish dirty-cell deltas without changing the baseline fingerprint.

The vegetation prototype deliberately has one plant kind, no seasons and no habitat spread. Later world-mutation slices add coordinate-addressed region generation, ground and construction overlays, per-domain topology versions and larger dirty regions. Transport still resolves atomically after a path check; autonomous jobs and action duration remain the next gameplay slice.
