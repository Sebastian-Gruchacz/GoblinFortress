# Architecture refactoring roadmap

## Why this exists

Several central files currently combine orchestration, domain rules, persistence,
presentation, input handling, and type-specific branching. The largest current
hotspots are approximately:

- `GoblinStronghold.Godot/Main.cs`: 8,700 lines;
- `GoblinStronghold.Simulation/SimulationEngine.cs`: 7,100 lines;
- `GoblinStronghold.Simulation/SimulationEngine.Jobs.cs`: 5,300 lines;
- `GoblinStronghold.Godot/WorldView.cs`: 3,300 lines;
- `GoblinStronghold.Simulation/Map/WorldMapState.cs`: 2,500 lines.

Splitting these files into more `partial` declarations helps navigation but does
not remove coupling. The goal is to move decisions and state into focused,
testable components while the large classes become composition and orchestration
boundaries.

## Dependency direction

The intended dependency flow is:

```text
Godot UI/controllers -> application orchestration -> simulation subsystems
                                            |-> immutable content catalogs
                                            |-> save/profile contracts
platform adapters (Steam/files) -> package/profile services
```

Simulation domain code must not depend on Godot, localized UI text, filesystem
paths, ZIP archives, or Steam. Platform adapters discover bytes and locations;
content-pack services validate them and build immutable catalogs; simulation
subsystems consume those catalogs through small interfaces.

## Target source layout

The layout grows by subsystem rather than by one flat collection of types:

```text
GoblinStronghold.Simulation/
  Animals/          species definitions, ecology, behavior and combat policies
  Actors/           goblin state, needs, traits, skills and equipment
  Combat/           targeting, damage, wounds and combat resolution
  Construction/     blueprints, sites, validation and completion
  ContentPacks/     manifests, IDs, discovery and immutable runtime composition
  Crafting/         recipe catalogs, orders, supply and work execution
  Economy/          resources, stock, storage and hauling contracts
  Factions/         human village, underground factions and relations
  Jobs/             planning, reservations, priorities and job executors
  Localization/     translation composition only
  Map/              geometry, generation, navigation and visibility
  Planning/         shared world-tool placement gestures and menu contracts
  Persistence/      versioned save DTOs, migration and compatibility gates
  Raids/            preparation, travel, combat, loot and return state machine
  Terrain/          mining, carving and other map-modification definitions
  Time/             clock, calendar and scheduled simulation updates

GoblinStronghold.Godot/
  Application/      game-session orchestration and composition
  Input/            shortcuts, selection and command construction
  Presentation/     presenters and localized view models
  UI/MainMenu/      title, load/profile, options and mod-manager controllers
  UI/Hud/           calendar, status, inspector and session controls
  UI/Windows/       focused controllers for construction, storage, raids, etc.
  Rendering/2D/     terrain, structures, entities, effects and render caches
  Rendering/3D/     terrain meshes, structures, entities and camera
  Platform/Steam/   Steam language, Cloud and Workshop adapters
```

Directories should be introduced when their first real component is extracted;
empty scaffolding is not useful.

## Component rules

### Catalog

Use an immutable catalog when behavior or data is selected by a stable content
ID or a legacy enum adapter. Catalog construction validates uniqueness,
completeness, references, ranges, and ownership. Runtime consumers query the
catalog instead of switching on every known content type.

### Policy

Use a stateless policy for one cohesive decision, such as habitat acceptance,
damage calculation, material compatibility, or targeting. A policy receives all
required context explicitly and does not reach into Godot or global mutable
state.

### Stateful subsystem/service

Use a stateful subsystem for a lifecycle with owned state and invariants, such
as crafting orders, raids, animal ecology, or storage logistics. It exposes
commands/queries and produces domain events. `SimulationEngine` determines tick
order and coordinates cross-subsystem work; it does not implement each rule.

### Presenter/controller

Godot controllers translate input into simulation commands. Presenters translate
snapshots into localized view models. Node lookup, popup lifecycle, drag/drop,
and rendering stay outside simulation code.

## Migration rules

1. Characterize the existing behavior with focused tests before moving it.
2. Extract one coherent responsibility without changing player behavior, enum
   numeric values, command JSON, or save JSON.
3. Introduce an interface at a real substitution/composition boundary, not for
   every class mechanically.
4. Preserve the old public entry point as a thin adapter until all consumers are
   migrated.
5. Replace type switches with validated definitions or strategies only when the
   cases represent open/moddable content. Exhaustive switches remain appropriate
   for genuinely closed engine states.
6. Keep changes small enough that a regression can be assigned to one subsystem.
7. Do not mix a behavior refactor with save-schema migration or visible UI
   redesign unless the new contract requires it.

## Stages

### Stage A: definitions and low-risk policies

- Extract animal species vital statistics behind `IAnimalSpeciesCatalog`.
  Completed: stable `core:*` identities adapt to existing `AnimalKind` values.
- Consolidate animal habitat, disposition, hunting yield, and combat traits.
  Completed: species definitions own the data, while focused habitat,
  disposition, and attack policies execute the rules. The former public combat
  policy remains as a compatibility facade.
- Move core animal definitions into embedded package data and expose validated
  load-order overrides. Completed: AI parameters, enemy selectors, spawn rules,
  harvest data, renderer/atlas/sprite IDs, and named palettes load from
  `content/animal-species.json`. Godot validates the referenced core visual
  assets before activating a package candidate. Localized animal inspection is
  now composed by the focused `AnimalTextPresenter`; simulation snapshots keep
  stable species adapters and contain no player-facing species prose.
- Move material, recipe, workshop, and construction definitions into matching
  subsystem namespaces while retaining compatibility facades.
  Construction blueprint data completed for the core pack: stable IDs,
  footprint geometry, material identity, quantity/work scaling, builder
  capabilities, and workshop links now live in the validated embedded
  `Construction/` catalog. `ConstructionKind`, commands, and save fields remain
  compatibility contracts. Package-level construction overrides and entirely
  new runtime construction kinds remain a later migration because placement,
  completion, rendering, and persistence still use the legacy enum.
  Planning metadata now distinguishes simple placement, building blueprints,
  and cell designations, plus point/line/area/fixed/directional gestures.
  Stable recursive menu paths prepare data-driven submenus without coupling
  construction to zones or terrain modification. See
  `docs/world-planning-tools.md`.

### Stage B: job planning and execution

- Split job candidates, reservations, scoring, and execution into separate
  contracts.
  Terrain work now owns actor capability checks, target exhaustion, forecast
  preference, legacy actor-job mapping, and deterministic candidate selection in
  `Terrain/Jobs`. The selector preserves the distinct route budgets and ordering
  rules for tunnel approaches and carved ramps. A complete immutable terrain
  assignment now combines the job kind, designation, target, route, and
  rock-sensitive work duration before actor state is mutated. Deterministic
  stone/deposit yields and experience rewards now come from a terrain policy,
  with multipliers, ranges, resource mappings, variants, and experience values
  loaded from the core terrain package. Deposit kinds still use their legacy enum
  adapter until geology definitions receive stable content IDs. A focused terrain
  execution service now performs the validated excavation or ramp mutation and
  returns its world change, output position, yield, and experience as one result.
  Stack indexing, reservation mutation, event publication, and tick execution
  remain at the central composition boundary.
- Replace the central job-kind dispatch with registered executors keyed by a
  stable job ID and a legacy `ActorJobKind` adapter.
- Keep tick ordering and deterministic tie-breaking explicit and covered by
  tests.

### Stage C: simulation state ownership

- Give animals, crafting, storage, combat, construction, and raids dedicated
  stateful subsystems.
- Surface contamination now has a focused `Contamination/SurfaceGrimeState`
  owner with deterministic pickup, deposition, cleaning, snapshot, and restore
  contracts. The goblin, human-villager, and animal movement boundaries all
  apply the same neutral tracked-dirt policy, while goblin movement additionally
  applies blood footprints; the legacy `CleanBlood` job/designation ID remains a
  save-compatible adapter for cleaning either kind of surface contamination.
  Move blood state beside this subsystem when the broader combat extraction
  reaches it, then replace the legacy name with a stable cleaning job ID.
- Move save/load conversion beside the subsystem that owns each state model.
- Retain `SimulationEngine` as the deterministic clock, command boundary, event
  collector, and cross-subsystem coordinator.

### Stage D: Godot application and UI

- Split `Main` into session orchestration plus focused menu/HUD/window
  controllers.
- Replace direct snapshot formatting with localized presenters/view models.
  Current actor-job descriptions completed: one focused presenter now formats
  every travel, collection, delivery, work, need, terrain, and raid phase from
  matching English/Polish catalog entries for all UI consumers.
- Separate selection/input command construction from popup and layout code.
- Replace the manual build/work button lists with a world-tool controller that
  composes construction, zone, terrain-modification, and general work catalogs
  into localized recursive menus.
  Menu composition completed for the current primary layout: Orders exposes four
  grouped rows in one second-level grid, while Basic constructions, Other
  constructions, and Digging and carving are flat toolbar-level grids.
  Construction content declares `basic/*`, `terrain/*`, or `advanced/*` paths.
  Future routes and terrain shaping,
  plus the drying rack and cooking fire, have localized disabled slots without
  speculative gameplay definitions. Material-bearing families remember their
  per-save variant through a Godot-owned `clientPreferences` envelope, defaulting
  to the largest compatible stored supply; simulation save contracts remain
  unchanged. Terrain target qualification and UI command creation remain behind
  focused terrain policies, while persisted designations still use the legacy
  `WorkDesignationKind` adapter and job tick dispatch remains centralized.

#### Disabled world-tool backlog

The following localized menu slots are deliberately visible but disabled. Keep
them in the roadmap until each has a complete content definition, simulation
command and job flow, placement validation, rendering, persistence, and tests:

- [ ] Path construction: define supported materials, placement cost, movement
  effect, work execution, and content-pack override rules.
- [ ] Road construction: define how roads differ from paths in cost, movement,
  footprint, and required work before enabling the existing slot.
- [ ] Raise terrain: place a chosen material over an area with explicit support,
  navigation, resource-consumption, and geology rules.
- [ ] Level terrain: use the first selected cell as the target elevation and
  validate excavation, fill, ramps, fluids, and unreachable fragments safely.
- [ ] Drying rack: add its construction blueprint, storage/input contract, work
  orders, and data-driven recipes for preserving fish and meat.
- [ ] Cooking fire: add its construction blueprint, fuel and ingredient flow,
  work orders, and data-driven recipes for simple meals.

The drying rack and cooking fire should use the future `Crafting/` recipe
catalog rather than introduce structure-specific recipe switches. Exact costs,
ingredients, outputs, durations, and skill requirements remain intentionally
undefined until the food-processing design is agreed.

### Stage E: rendering

- Split `WorldView` into ordered render layers with shared render context and
  explicit caches.
- Move per-entity and per-structure drawing into dedicated renderers selected by
  stable IDs where content is moddable.
- Keep draw ordering, culling, animation time, and resource lifetime centralized.
- Floor patterns and neutral grime currently remain code-native drawing layers.
  The grime layer reuses a recolored atlas mask and is ordered below blood; move
  both into a dedicated contamination renderer when render-layer extraction
  begins.

### Stage F: profiles, saves and mod content

- Record active package IDs, versions, and hashes in profile/save metadata.
- Build catalogs from the immutable active-package registry at session start.
- Gate loads before simulation mutation when required definitions are missing or
  incompatible.

## Validation for every slice

- focused regression tests for the extracted behavior;
- full simulation test suite across the thematic content, world, economy, and
  scenario projects described in `docs/testing.md`;
- Godot C# build with no warnings;
- localization key parity and hardcoded-prose review when UI is touched;
- `git diff --check` and per-file encoding/newline verification;
- headless Godot startup when composition, scenes, resources, or UI change.
