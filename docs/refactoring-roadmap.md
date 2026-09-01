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
  Persistence/      versioned save DTOs, migration and compatibility gates
  Raids/            preparation, travel, combat, loot and return state machine
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
  assets before activating a package candidate.
- Move material, recipe, workshop, and construction definitions into matching
  subsystem namespaces while retaining compatibility facades.

### Stage B: job planning and execution

- Split job candidates, reservations, scoring, and execution into separate
  contracts.
- Replace the central job-kind dispatch with registered executors keyed by a
  stable job ID and a legacy `ActorJobKind` adapter.
- Keep tick ordering and deterministic tie-breaking explicit and covered by
  tests.

### Stage C: simulation state ownership

- Give animals, crafting, storage, combat, construction, and raids dedicated
  stateful subsystems.
- Move save/load conversion beside the subsystem that owns each state model.
- Retain `SimulationEngine` as the deterministic clock, command boundary, event
  collector, and cross-subsystem coordinator.

### Stage D: Godot application and UI

- Split `Main` into session orchestration plus focused menu/HUD/window
  controllers.
- Replace direct snapshot formatting with localized presenters/view models.
- Separate selection/input command construction from popup and layout code.

### Stage E: rendering

- Split `WorldView` into ordered render layers with shared render context and
  explicit caches.
- Move per-entity and per-structure drawing into dedicated renderers selected by
  stable IDs where content is moddable.
- Keep draw ordering, culling, animation time, and resource lifetime centralized.

### Stage F: profiles, saves and mod content

- Record active package IDs, versions, and hashes in profile/save metadata.
- Build catalogs from the immutable active-package registry at session start.
- Gate loads before simulation mutation when required definitions are missing or
  incompatible.

## Validation for every slice

- focused regression tests for the extracted behavior;
- full simulation test suite;
- Godot C# build with no warnings;
- localization key parity and hardcoded-prose review when UI is touched;
- `git diff --check` and per-file encoding/newline verification;
- headless Godot startup when composition, scenes, resources, or UI change.
