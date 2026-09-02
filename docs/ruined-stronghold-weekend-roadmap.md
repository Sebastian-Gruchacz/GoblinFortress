# Ruined stronghold weekend roadmap

## Outcome

New tribes should begin inside the remains of an older stone stronghold that
goblins have repaired with reed mats, crooked timber, platforms, rope and fire.
The opening view should immediately echo the title illustration without turning
the pre-release build into a new survival-system project.

The weekend tester build prioritizes a convincing, playable 2D starting site.
It must keep deterministic generation, save compatibility, navigation, lighting,
English/Polish localization parity and current construction behavior intact.

## Current reusable foundations

The game already has most of the authoritative pieces needed for the first
version:

- stone and wooden walls, door frames, floors, ramps and walkways;
- wall torches with real light, occlusion and vertical propagation;
- a primitive workshop and data-driven workshop/recipe catalogs;
- goblin camps and huts with shelter capacity;
- deterministic initial fauna, including huntable surface prey;
- corpses, corpse-handling directives and corpse-origin goblin buds;
- wet-site reproduction, although it currently requires a goblin-hut floor;
- generated structures saved as ordinary world objects.

The missing foundation is a focused goblin start-site generator and a 2D ruin
dressing layer. Neither belongs as another structure-kind switch in
`SimulationEngine` or `WorldView`.

## Visual language

The title illustration suggests three rules for the in-game version:

1. Old masonry is the large, stable silhouette. Goblin work is thin, crooked,
   tied together and visibly opportunistic.
2. Cold swamp colors dominate the ground and ruins; small warm lights mark the
   inhabited pockets.
3. The site grows vertically in appearance even before true ladders exist:
   broken wall heights, short platforms, braces, hanging mats and scaffold
   shadows create layers without lying about traversable levels.

At 20 pixels per cell, readability is more important than literal detail. Use a
small atlas with strong silhouettes and several deterministic variants rather
than unique large illustrations.

## Weekend release scope

### P0: required for the tester build

#### 1. Deterministic starter-ruin plan

Add a focused `Map/Generation/GoblinStarterRuinPlanner` (name provisional) that
produces a validated plan around `GeneratedMap.GoblinSpawn`. The plan should:

- reserve roughly a 12 x 10 to 16 x 14 cell area, scaled down safely for small
  test maps;
- preserve a connected route from the spawn to open terrain and avoid rivers,
  roads, deep water, steep unsupported geometry and the human settlement;
- form two or three irregular stone-room fragments around a shared yard;
- use surviving stone walls and floors plus wooden walls, floors, ramps and
  walkways as goblin repairs;
- include one completed primitive workshop and two or three existing wall
  torches where their placement rules can be satisfied;
- keep enough empty cells for the starting goblins, loose supplies and player
  construction;
- derive every choice from the world seed and generator version.

`WorldMapState.CreateInitial` should compose the resulting objects with the
existing generated settlement objects. The planner owns placement and
validation; the world-state factory only composes results.

Existing saves continue to restore their serialized world objects unchanged.
If the initial-object layout participates in compatibility fingerprints or
replay expectations, increment the map generator version and keep version 15
loadable.

Acceptance:

- identical seed, profile, dimensions and generator version produce the same
  ruin and world fingerprint;
- all starting goblins have reachable open cells and can leave the ruin;
- the human settlement, surface routes and initial ecology never overlap the
  reserved footprint;
- generation succeeds across the supported minimum dimensions and a broad seed
  sample without silent rerolls.

#### 2. Ruin dressing atlas and dedicated renderer

Create a compact 2D atlas and a focused renderer under the extracted rendering
area. It may derive decoration placements from the immutable starter-ruin plan,
but simulation occupancy remains authoritative.

Minimum atlas set:

- cracked masonry edges, missing caps, loose stones and moss;
- reed-and-stick wall patches in several orientations;
- timber braces, lashed posts and short scaffold/platform details;
- reed sleeping mats, bedroll clutter and hanging reed screens;
- a primitive workshop skin that reads as a stump/slab, stone tools and lashings;
- cold hearth, active communal fire, cookpot/spit and two food-clutter variants;
- refuse/compost heap with food scraps, bones and a restrained corpse cue;
- freestanding torch basket plus rope, sacks and drying bundles.

Decorations must not create invisible collision or imply a usable second level.
Blocking reed patches are rendered only over authoritative wall objects.
Sleeping mats and hearths are decorative in P0 and must not advertise an action
the player cannot perform.

Acceptance:

- the initial camera view contains old stone, goblin repairs, one workshop,
  sleeping clutter, at least two warm light sources and one refuse area;
- four or more seeds have visibly different dressing without changing gameplay;
- zoomed-out silhouettes remain readable and zoomed-in tiles do not blur;
- 2D draw order keeps floors below actors, wall patches with walls, and hanging
  details above the correct structure edge;
- no new per-frame allocation or full-map redraw is introduced.

#### 3. Starter shelter without new sleeping simulation

The ruin should provide enough initial shelter for the starting tribe using a
small focused shelter policy or a generated shelter marker. Do not model beds,
bed ownership or sleep quality for this release. Existing rest behavior remains
unchanged; reed mats explain it visually.

The old `GoblinHut` remains buildable for expansion unless the final design
decision replaces it with modular rooms later.

Acceptance:

- the tribe does not begin over shelter capacity;
- dismantling the shelter-bearing parts updates capacity consistently;
- save/load produces the same capacity and does not rely on renderer metadata.

#### 4. Fauna tuning near the start

Keep the existing species and hunting behavior. Adjust only data-driven initial
surface population or add a ruin-clearance exclusion radius if tests show that
the reserved footprint removes too many valid habitat cells.

Acceptance:

- ordinary 96 x 96 starts retain multiple nearby huntable animals;
- no animal spawns inside a solid ruin cell or on a starting goblin;
- the ruin does not create an immediate unavoidable boar attack.

#### 5. Release validation

- focused planner, occupancy, reachability, shelter and rendering-policy tests;
- localization key parity and placeholder parity for every new inspector or UI
  string in English and Polish;
- full simulation test suite and Godot C# build;
- headless Godot startup with resource-loading checks;
- export the tester package, inspect the export log and launch-test the package;
- smoke-test new game, save/load, construction/dismantling, hunting, darkness and
  at least ten minutes of accelerated simulation on several seeds;
- `git diff --check` and a hardcoded-player-text review.

### P1: include only after P0 is green

#### Functional compost hollow

Turn the refuse area into a small buildable compost/reproduction site. Reuse
the current moist-site and corpse-origin bud rules behind a focused reproduction
site policy. Food scraps, refuse and corpses may contribute visual stages or
site fertility, but the first implementation should not add a separate waste
economy.

This requires an authoritative world object, placement rules, persistence,
dismantling, inspector text and EN/PL catalog entries. It must replace the
current hard requirement for a moist `GoblinHut` floor through a policy rather
than add another conditional to `SimulationEngine.Reproduction.cs`.

#### Primitive cooking fire

Enable the existing disabled cooking-fire slot only if it gains a complete
blueprint, fuel/input contract, work order and at least one useful recipe. Raw
meat already exists, but cooked food requires an explicit food identity,
nutrition values, hauling behavior, localization and save validation. A purely
decorative active hearth remains preferable to a misleading half-working tool.

#### Workshop progression split

Keep genuinely primitive recipes at the starting workshop: sling, primitive
axe and pickaxe, bone knife, fighting stick, stone club, hide/reed clothes and
waterskin. Candidate recipes for a later carpentry or fitted workshop are the
reinforced pickaxe, barrel, chest and bulk bin. The wooden bucket and simple box
can remain primitive unless playtesting shows that early storage is too easy.

Do not move recipes until the replacement workshop is constructible and the
player cannot be trapped without a required tool or container.

### P2: after the first tester weekend

#### Buildable watchtower

First ship a single-level 2 x 2 timber watch platform with a clear silhouette
and a modest vision bonus. Keep its visibility behavior behind a structure
observer policy. A truly walkable multi-level tower belongs with supported
platforms and vertical navigation.

#### Ladders and real vertical scaffolds

Ladders are not cosmetic ramps. They need a new vertical-passage contract,
pathfinding edges, reservations, construction order, occupancy, visibility,
save data, 2D/3D rendering and tests for blocked endpoints. Do not squeeze this
into the tester build unless the design explicitly accepts that full slice.

#### Modular inhabited ruins

Allow players to repair captured/generated masonry with material-aware wall
patches, roofs, room designation, comfort and decay. This is the long-term
replacement for treating a ruin as a disguised hut.

## Suggested implementation order

1. Characterize current new-game fingerprints, generated-object occupancy,
   initial reachability and shelter with tests.
2. Implement and validate the pure starter-ruin plan.
3. Materialize only existing authoritative structure kinds and prove save/load.
4. Add the 2D atlas, dedicated renderer and deterministic dressing variants.
5. Add minimal shelter adaptation and fauna exclusion/tuning if required.
6. Run the complete release gate and capture screenshots from several seeds.
7. Decide whether remaining time is spent on functional compost or cooking;
   do not start both before one is end-to-end complete.
8. Treat watchtowers, ladders and workshop progression as subsequent vertical
   slices unless P0 and the selected P1 slice are already release-clean.

## Explicit non-goals for the weekend build

- bed assignment, sleep quality or private rooms;
- a general waste/odor/disease economy;
- walkable upper stories or ladder pathfinding;
- procedural collapse, structural stability or ruin decay;
- replacing all existing construction art;
- mandatory 3D visual parity unless the tester build exposes the 3D view;
- moving recipes without a complete replacement production path.

## Decisions required before implementation

1. Is the tester build 2D-only, allowing the 3D view to use its current generic
   structure fallback for the new starting composition?
2. Should the compost hollow be functional in the first tester build, or should
   it begin as dressing while existing hut-based reproduction remains intact?
3. Should the starting ruin contain a ready-to-use primitive workshop and
   shelter at no construction cost, or should goblins start among ruins and
   spend the first minutes adapting them?

Recommended defaults are: 2D-first; decorative compost for this weekend; one
completed primitive workshop and enough inherited shelter for the starting
tribe. These choices maximize visible atmosphere while minimizing release risk.
