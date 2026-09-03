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
  Civilizations/    definitions, polities, settlements, parties and relations
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
  Construction dismantling now reuses persistent work designations and has a
  focused `ConstructionDismantlingPolicy` for the legacy object-to-blueprint
  adapter and the 30-percent work duration. A focused target factory now owns
  world-object eligibility, storage-provider mapping, footprint normalization,
  duration, and access-cell discovery. Route planning, completion orchestration,
  and the legacy actor-job dispatch still remain at the composition boundary
  pending the registered-executor migration. The
  current Godot planner renders dismantling targets, tracks their active worker,
  and supports priority, suspension, resumption, focus, and cancellation without
  exposing the area-edit action used by ordinary region designations. Removing a
  constructed floor resolves unsupported goblins, carried or loose corpses, and
  loose item stacks onto the nearest traversable level below. Regression coverage
  also protects floors beneath structures and at the lower end of vertical ramps.
- Replace the central job-kind dispatch with registered executors keyed by a
  stable job ID and a legacy `ActorJobKind` adapter.
- Keep tick ordering and deterministic tie-breaking explicit and covered by
  tests.

### Stage C: simulation state ownership

- Give animals, crafting, storage, combat, construction, and raids dedicated
  stateful subsystems.
- Civilization foundations now have a stable-ID catalog in `Civilizations/`.
  The core pack defines player goblins, the human demo village, and cave-dwarf
  clans. Underground occurrence, depth bands, population, stocks,
  fortification, upkeep, relations, and conflict timing are data-driven while
  `UndergroundFactionKind` remains a save adapter. The wider polity, settlement,
  visitor-party, kingdom, bandit, legendary-threat, and location-profile stages
  are tracked in `docs/civilizations-and-world-generation-roadmap.md`. The
  current surface generator now reads geometry-driving river, wetland,
  settlement-pad, relief, and dimension values from the validated embedded
  `core:demo-swamp-frontier` profile; `SwampMapGenerator` remains the
  save-compatible facade over `LocationGenerationRequest`. Format-73 saves pin
  the stable profile ID and selected absent/single/branching river mode, while
  deep geology geometry is still code-native. Its first extraction is the pure
  `Map/Generation/CaveMacroFeaturePlan` contract and focused planner: stable-ID
  multi-level plans reserve cells and passages, reject overlaps, and declare
  either layer-by-layer or complete-on-exposure materialization. Generator v15
  activates a four-level sloped cavern and complete-on-exposure lava gallery;
  v14 remains loadable with its original deterministic geometry. True open
  shafts and data-driven feature definitions remain later stages. The
  subsequent route slice lives in the
  focused `Map/Generation/SurfaceRouteGenerator`: profile-driven absent,
  through-road and junction modes run after hydrology, create shallow fords,
  reserve their corridor from initial ecology and structures, and are pinned by
  format-76 saves without changing format-75 map fingerprints. Generated maps
  expose the ordered route centerlines and named endpoints as immutable derived
  data, so future party directors can consume road approaches without parsing
  rendered surface features or adding route state to saves.
- Generator v16 composes its goblin opening through the focused
  `Map/Generation/GoblinStarterRuinPlanner`. The planner owns deterministic
  placement and authoritative ruin, workshop, compost, sleeping-mat and torch objects while
  v15 retains its original hut composition. Permanent-shelter classification,
  floor selection and capacity now share `Shelter/GoblinShelterPolicy`; compost
  reproduction sites are selected by `Reproduction/GoblinReproductionSitePolicy`
  instead of adding another structure branch to the reproduction engine. Its
  rebuild path is an ordinary catalog-defined two-reed construction, including
  generic hauling, save/load and dismantling contracts. Primitive rest-place
  selection lives in `Shelter/GoblinSleepingPlacePolicy`: it partitions free
  mats into covered and exposed candidates from authoritative sky exposure,
  while engine orchestration owns pathfinding and deterministic per-job
  reservation. Placement is independent from structure classification, so a
  primitive exposed mat remains usable when no roof exists.
- Primitive food production now keeps feasibility selection in
  `Crafting/AutomaticCookingPolicy`, portability in `Resources/FoodUsePolicy`
  and shelf-life values in `Resources/FoodPreservationPolicy`. Engine
  orchestration assigns saved expiry ticks, applies daily spoilage to world,
  personal and workshop-buffer food, and transfers spoiled units into one
  tribe-wide compost nutrient pool. Manual and repeating crafting orders remain
  the authoritative override over automatic cooking. Cave lichen now has a
  dedicated designation and persistent depleted-patch state in `WorldMapState`;
  lichen and mana reuse concrete `Materials` variants, while the data-driven
  cooking catalog owns their first recipe. Mana consumers and flora regrowth
  remain deferred.
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
  The first New Game extraction now lives in `UI/MainMenu`: the focused window
  owns setup layout, validation, and localized control state, while `Main` only
  starts a session from the accepted seed and dimensions. Future generation
  parameters should extend that setup contract instead of rebuilding the window
  in `Main`.
- Replace direct snapshot formatting with localized presenters/view models.
  Current actor-job descriptions completed: one focused presenter now formats
  every travel, collection, delivery, work, need, terrain, and raid phase from
  matching English/Polish catalog entries for all UI consumers.
- Separate selection/input command construction from popup and layout code.
- Persistent player-profile window geometry now belongs to the focused
  `UI/Windows/WindowLayoutController` and `Application/Profiles` store. `Main`
  only activates the selected profile at the session boundary; window node names
  are stable persistence IDs, and saved rectangles are constrained to the
  current viewport before use.
  Global main-window mode and windowed resolution live beside that profile store
  but are restored only in release builds; minimized state is never restored.
- Replace the manual build/work button lists with a world-tool controller that
  composes construction, zone, terrain-modification, and general work catalogs
  into localized recursive menus.
  Menu composition completed for the current primary layout: Orders exposes four
  grouped rows in one second-level grid, while Basic constructions, Other
  constructions, and Digging and carving are flat toolbar-level grids.
  Construction content declares `basic/*`, `terrain/*`, or `advanced/*` paths.
  Future routes, terrain shaping, and the drying rack have localized disabled
  slots without speculative gameplay definitions. The cooking-fire slot is now
  enabled through the shared construction and crafting catalogs. Material-bearing families remember their
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
- [ ] Drying rack: add its construction blueprint, storage/input contract and
  work orders; define how its fuel-free preservation differs from the cooking
  fire's existing fish-and-meat ration recipe.
- [x] Cooking fire: its construction blueprint, seven food recipes, automatic
  feasible-recipe selection, exact food inputs, long-lived outputs and
  working-only light are data-driven through the shared construction, crafting
  and emitter catalogs.

The drying rack should use the existing `Crafting/` recipe catalog rather than
introduce structure-specific recipe switches. Its exact construction cost,
efficiency advantage, durations and skill requirements remain intentionally
undefined until it has a distinct role from primitive fire drying.

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
- The generated starter ruin uses the focused procedural
  `UI/WorldRendering/GoblinRuinPainter`, so masonry, repairs, fires
  and compost retain the established strict top-down perspective at 20 pixels
  per cell. `WorldView` retains only structure dispatch and active-torch
  composition until the general structure-renderer registry is extracted;
  decoration remains non-authoritative. Authoritative sleeping mats use the
  focused `ReedSleepingMatPainter` instead of being baked into ruin decoration.
  The freestanding fire basket likewise uses `StandingTorchPainter` and maps to
  the existing wall-torch light definition without inheriting its directional
  wall-facing occlusion.
- Passive settlement observation now routes through
  `Visibility/GoblinStructureObserverPolicy`. Existing shelters retain their
  civilization-defined radius, while the first wooden watchtower contributes a
  focused radius-7 observer without adding a structure-kind branch to
  `SimulationEngine.UpdateVisibility`.

#### Active slice lighting and lower-level presentation cache

The active level remains the only fully live render slice. Holes, ramps, cliffs,
and other vertical openings may expose lower levels, but those levels must be
presented through bounded cached slices rather than complete live world draws.
Keep simulation state authoritative and independent from these presentation
caches.

- [x] Define stable core light-emitter IDs and data-driven radius, intensity,
  color, flicker, and activation parameters outside `WorldView`.
- [x] Maintain a level-aware spatial emitter index. The first integration covers
  wall torches, lava, and bloomery/furnace/crucible fires while an eligible
  crafting order is actually being worked.
- [x] Apply ambient cave darkness and surface night darkness on the active level,
  with low-frequency flame flicker limited to visible indexed emitters. Ordinary
  human villagers no longer act as implicit light sources.
- [x] Separate cross-level discovery from active visibility. A currently visible
  end of a real cave mouth, natural ramp, or excavated passage records a one-cell
  explored margin on the adjacent layer without lighting it or cascading through
  covered coordinates. Underground observers always use limited dark vision;
  surface day and night now have a stronger data-driven contrast. The active
  light map reduces darkness over currently visible cells only on its own level;
  dark vision is not a light emitter and is never projected between levels.
- [x] Add hard cell-level terrain and structure light occlusion on active and
  cached lower slices. Solid rock, constructed walls, and closed door leaves
  block rays; open doors transmit them, diagonal corner leaks are rejected, and
  wall-mounted torches emit only toward their facing side. The reusable policy
  stays outside render-kind conditionals.
- [x] Add controlled upward light propagation through continuously exposed
  ramps, cave mouths, shafts, cliffs, and other open vertical columns. A source
  must reach the lower opening without crossing a blocker, then loses radius and
  intensity on every projected level; disconnected or covered planes do not
  receive a projected emitter.
- [x] Give the active slice a small multi-ray penumbra around blocker edges.
  Rays traverse full blocking cells instead of blurring the finished mask, so
  complete walls and closed corners remain opaque. Cached lower slices retain
  deliberately hard shadows to keep rebuilds inexpensive.
- [x] Extend light definitions with explicit world/actor attachment plus
  always-on, working, carried, and actor-trait activity requirements. Fuel is a
  separate contract covering work-order input, stored fuel, and portable charge.
  Existing lava and torches remain static; working furnaces now pass through the
  shared activation policy without changing current gameplay behavior.
- [ ] Connect stored fuel and portable charge to real cooking-fire, torch, and
  lantern inventory state once those systems exist. Do not treat the contract
  alone as fuel simulation.
- [ ] Register carried lanterns and luminous actor traits as dynamic emitter
  snapshots on active and exposed lower slices once actors can actually own
  those definitions.
- [x] Introduce a narrow `PresentationSliceRequest`/plan contract containing the
  active level, visible map rectangle, direct exposure columns, continuous
  passages, opening destinations, light passages, regions, chunks, and a stable
  workload summary. Godot consumes this plan instead of rebuilding slice rules.
- [ ] Move management UI and HUD summaries to separate snapshots instead of
  forcing world presentation to copy unrelated simulation collections.
- [x] Index connected lower-level exposure regions from lower terrain surfaces
  in the visible map rectangle and from vertical openings, then
  divide large regions into bounded chunks (start with 16x16 or 32x32 cells).
  Nearby openings that expose the same lower plane may share a larger cached
  region instead of producing many tiny textures. A region is dynamically
  presentable only while it has a continuous registered exposure chain to the
  active level; an opening hidden behind an intervening closed plane must not
  keep deeper presentation work active.
- [x] Cache exposed lower terrain and cave geometry as reduced-resolution color
  textures with a separate one-pixel-per-cell exposure mask. Rebuild only
  visible dirty chunks, retain hidden textures, and composite cached geometry
  from the deepest visible level upward before drawing the active level.
- [x] Increase lower-slice geometry to twenty pixels per cell and bake plants,
  effective-height structures, tree trunks, and segmented atlas crowns into the
  same cached image before applying one distance-based brightness degradation
  pass. Apply the same depth curve to the sky-exposed copy and to regions drawn
  through openings so neither path can restore full brightness. Plant harvest
  and regrowth invalidate only affected retained chunks; vegetation no longer
  uses a separate lower-level overlay. Keep spatial resolution fixed until
  playtesting justifies depth-dependent downsampling; deeper chunks already lose
  temporal fidelity through their linearly increasing refresh interval.
- [x] Extend cached geometry with simplified static structures and apply the
  exposure mask while composing openings in the active plane. Lower slices use
  deliberately reduced structure silhouettes instead of live structure draw
  calls.
- [x] Add a separate low-resolution prelit geometry texture for always-active
  lower-level sources such as wall torches and lava. It copies the terrain and
  structure pixels and raises their brightness behind the same occlusion policy
  instead of washing them with translucent colored circles. Cached light remains
  static and only active-level flames flicker; work-activated and mobile sources
  stay in the dynamic-light follow-up.
- [x] Retain chunk cache state when exposure or the camera moves away. Hidden
  dirty chunks are excluded from rebuild candidates and become candidates only
  after the camera descends or a continuous exposure chain makes them visible
  again.
- [x] Route observed topology, structure, contamination, and static-light
  changes into position-scoped chunk invalidation. Unknown topology changes
  retain a safe full-cache fallback, while unchanged snapshots do no work and
  dirty chunks still rebuild from the lowest affected level upward.
- [ ] Route future mutable-fluid events through the same invalidation contract.
  Current underground water and lava belong to immutable generated map
  geometry, so there is no runtime fluid mutation source to register yet.
- [x] Add a low-cadence, position-quantized overlay for lower-level moving
  actors. Render compact silhouettes without interpolation and only inside the
  exact currently exposed opening above them. Lower geometry, visibility
  signatures, and actor samples now synchronize on a persisted 0.25-2 second
  presentation cadence instead of every frame. Texture rebuilds occur only for
  dirty chunks and no more often than that base interval, multiplied linearly by
  depth below the active slice; positive and negative slices use the same path.
- [x] Make the lower-slice chunk size (8, 16, 24, or 32 cells) and refresh
  cadence player-configurable in the localized Performance section. Changing
  chunk size safely resets presentation-only indices and textures while keeping
  simulation and save contracts unchanged.
- [x] Gate active actor and animal interpolation to the current camera rectangle
  and currently visible cells. Off-screen, unexplored, and merely remembered
  movement stays position-dirty without scheduling animation redraws, then snaps
  to the current target when it becomes observable again.
- [x] Remove the duplicate live lower-detail and lower-actor passes from
  `WorldView`; static detail stays in cached chunk textures and lower actors are
  drawn only through openings. Hide job targets and work-designation overlays
  when a cell falls below six screen pixels.
- [x] Bound active-slice collection passes to the camera rectangle before
  issuing draw commands. Structures and their parts, floors, walkways, walls,
  doors, torches, plants, contamination, fields, storage, loose items, corpses,
  buds, jobs, construction, crafting, animals, and villagers no longer draw the
  rest of the level merely because another visible animation requested redraw.
- [x] Cache every visited minimap level as a static one-pixel-per-cell texture
  keyed by world and discovery state. Terrain and non-nature structures rebuild
  synchronously during snapshot refresh or a level switch, while actor markers
  and the camera rectangle remain lightweight overlays. Current actor vision no
  longer brightens the cached minimap surface.
- [ ] Attach mobile light snapshots to that overlay once carried lanterns,
  torches, or luminous actor traits have real content definitions. Until then,
  do not invent implicit light sources for ordinary actors.
- [x] Keep lower-level simulation free of renderer synchronization and frame
  deadlines. Presentation consumes immutable results at its own cadence, so
  independent lower-level algorithms remain eligible for parallel execution;
  only publication at the simulation boundary requires synchronization.
- [x] Add allocation-free-on-write counters and timings for presentation
  snapshot construction, emitter queries, active light-map builds, evaluated
  cells/emitters, dirty lower chunks, and geometry/static-light texture rebuilds.
  Full `WorldView._Draw` duration is included so retained-layer work can be
  distinguished from snapshot refresh cost. Metrics remain pull-based and do
  not log every frame.
- [x] Establish deterministic structural baselines for a single shaft and a
  broad Swiss-cheese exposure map. The workload summary reports direct columns,
  continuously exposed cells, regions, chunks, and light passages alongside
  runtime timings.
- [x] Add a bounded runtime spike recorder that correlates long frame intervals
  with main-thread work, simulation batches, snapshot construction, view refresh,
  autosaves, active lighting, and lower-slice rebuilds. Ordinary frames remain
  allocation-free and debug-build warnings are rate-limited independently from
  the bounded sample history.
- [ ] Capture live wall-clock samples from a representative long-running save
  for both workloads before increasing cache fidelity or rebuild budgets.
- [x] Split the active slice into independent retained static and dynamic
  `CanvasItem` passes. Terrain, structures, contamination, storage, items, and
  other slow state retain their Godot draw-command buffer while actors,
  villagers, animals, effects, previews, fog, and active light redraw without
  re-entering static C# drawing. Static snapshot changes are coalesced to at
  most once per second unless world or topology versions require an immediate
  refresh; lower-slice rebuilds still invalidate the retained pass directly.
- [x] Stop scheduling the retained static pass for actor-only snapshots. A
  focused presentation-change policy compares only slow visual state on the
  active level, while opaque dynamic fog masks cached static content in unknown
  cells and reveals it without rebuilding the static pass as exploration moves.
- [x] Pad the retained active static command buffer to chunk-aligned camera
  bounds. Camera movement reuses the buffered margin and queues a rebuild only
  after the viewport leaves it; the existing performance chunk-size setting
  controls this spatial hysteresis as well as lower-slice texture chunks.
- [x] Route positive active levels through the same lower-slice texture cache as
  surface and underground views. Empty positive cells inherit discovery from
  their known terrain column, and the highest constructed floor is included in
  the cached slice while blocking geometry beneath it. Positive slices no
  longer fall back to a separate terrain-only draw while a chunk is pending.
- [x] Keep rendering caches derived and outside the save contract. A persisted
  performance option chooses background warmup or waits behind a localized,
  weighted loading overlay until the initial retained world pass and visible
  lower-level chunks are ready; minimap construction remains part of view
  initialization. The client save envelope retains the last viewed level and
  restores it before that initial cache pass, while exposure/opening indices
  remain cheap topology-derived presentation state rather than duplicated save
  data.
- [x] Propagate current discovery through continuous open vertical sight lines
  in both directions. Newly observed cells seed the column pass, current visible
  seeds are reconsidered after topology changes, open air continues through
  every materialized level, and the first blocking floor or ceiling becomes
  explored without revealing geometry behind it.
- [ ] Partition the retained active static pass into independently invalidated
  spatial chunks. The current retained command buffer avoids repeated C# work
  between invalidations but a real static change still rebuilds the visible
  camera rectangle as one unit.

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
