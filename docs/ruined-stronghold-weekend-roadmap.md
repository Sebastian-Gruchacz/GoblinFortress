# Ruined stronghold weekend roadmap

## Outcome

New tribes should begin inside the remains of an older stone stronghold that
goblins have repaired with reed mats, crooked timber, platforms, rope and fire.
The opening view should immediately echo the title illustration without turning
the pre-release build into a new survival-system project.

The weekend tester build prioritizes a convincing, playable 2D starting site.
It must keep deterministic generation, save compatibility, navigation, lighting,
English/Polish localization parity and current construction behavior intact.

## Implementation status — 2026-09-03

The selected weekend slice is implemented for generator v16: a new tribe starts
with 12 goblins in an adapted ruin, four usable reed sleeping mats, a completed
primitive workshop, two fueled wall torches, one freestanding fire basket, one
functional cooking fire and a functional compost hollow. The ruin is permanent shelter and
participates in rest, visibility, return-to-shelter and tribal-knowledge rules.
Fresh goblin corpses default to recovery at the nearest camp or compost and can
produce a corpse-origin bud there. The compost can be dismantled and rebuilt as
a two-reed primitive construction, so losing the generated site does not remove
that population path permanently. Surface hare and boar populations are raised
through the species catalog. Generator v15 retains its original hut layout.

The 3D prototype is intentionally unavailable in the tester UI and through F3.
The dedicated 2D ruin painter uses sharp procedural masonry and flat top-down
dressing for plank repairs, reed-and-stick wall patches, lashed
scaffolding, a cooking fire, a smaller ember fire, the workshop, compost and
torches. A first illustrated atlas was rejected because its three-quarter
perspective conflicted with the world view.
Exposed lower slices now use the same 20-pixel cell resolution as the active
top-down view. Ruins, compost, sleeping mats, watchtowers, fires, torches,
workshops and constructed ramps retain their detailed painters when seen from
above; loose items and corpses remain live overlays instead of being frozen or
omitted by the lower-level texture cache.
The first buildable wooden watchtower is also present as a 2×2 solid lookout:
it costs 8 wood and acts as a passive tribal observer with radius 7. It has no
walkable upper floor yet, so the current top-down ladder mark is visual only.
Sleeping mats are authoritative one-goblin resting places. A resting or
approaching goblin reserves one deterministically; when no free mat is reachable,
goblins retain the old shelter-floor fallback so old saves and compact maps
cannot deadlock. Players can build a mat for 2 reeds on any free reachable
cell. Covered mats are preferred, but exposed mats remain valid for the flat
starter ruin and other primitive camps.
The full starter ruin also contains one authoritative freestanding fire basket.
Players can build more standing torches for 1 wood on a free cell; they reuse
the wall torch's established light profile but illuminate every direction.
The generated cooking fire is a separate authoritative workshop. Players can
build another for 3 wood. The fire supports cooked meat, four fish/meat and
root/mushroom soups, dried fish-and-meat rations, and root-and-berry medicine.
When the player has not queued a specific order, each idle fire deterministically
chooses a random feasible recipe from stored ingredients; explicit and repeating
orders take priority. Soup remains on the cooking-site floor and cannot be packed
or stockpiled. Its light is active only while its work order has ingredients and
is being worked. Explored underground lichen can now be designated for gathering;
each deterministic patch yields two physical lichen units and remains depleted
across save/load. One lichen plus one mushroom brews one stored mana reagent at a
cooking fire, including automatic recipe selection. Mana has no consumer yet.
The first workshop-progression step is also playable: an 8-wood fitted workshop
requires a primitive axe and Building level 1, retains its delivered wood
identity and unlocks the reinforced pickaxe, barrel, chest and bulk bin. Real
ladders remain post-weekend work.

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

At 20 pixels per cell, readability is more important than literal detail. Use
sharp procedural marks with strong silhouettes and several deterministic
variants. Any future atlas must be orthographic top-down and match the existing
world view before it replaces those marks.

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

#### 2. Top-down ruin dressing and dedicated renderer

Create a focused painter under the extracted rendering area. It may derive
decoration placements from the immutable starter-ruin plan, but simulation
occupancy remains authoritative. The weekend version is procedural so every
mark stays sharp and orthographic in the existing top-down view.

Minimum dressing set:

- cracked masonry edges, missing caps, loose stones and moss;
- reed-and-stick wall patches in several orientations;
- timber braces, lashed posts and short scaffold/platform details;
- authoritative reed sleeping mats, bedroll clutter and hanging reed screens;
- a primitive workshop skin that reads as a stump/slab, stone tools and lashings;
- cold hearth, authoritative communal cooking fire, cookpot/spit and two
  food-clutter variants;
- refuse/compost heap with food scraps, bones and a restrained corpse cue;
- authoritative freestanding torch basket plus rope, sacks and drying bundles.

Decorations must not create invisible collision or imply a usable second level.
Blocking reed patches are rendered only over authoritative wall objects.
The small ember hearth remains decorative; the cooking fire and sleeping mats
are ordinary authoritative world objects with separate top-down painters.

Acceptance:

- the initial camera view contains old stone, goblin repairs, one workshop,
  sleeping clutter, at least two warm light sources and one refuse area;
- four or more seeds have visibly different dressing without changing gameplay;
- zoomed-out silhouettes remain readable and zoomed-in tiles do not blur;
- 2D draw order keeps floors below actors, wall patches with walls, and hanging
  details above the correct structure edge;
- no new per-frame allocation or full-map redraw is introduced.

#### 3. Starter shelter and primitive sleeping places

The ruin provides enough initial shelter for the starting tribe through the
focused shelter policy. Four generated reed mats seed a minimal sleeping-place
system: covered free mats are preferred over exposed free mats and reserved per
resting goblin, while other shelter floor remains a compatibility fallback.
There is no persistent bed ownership, outdoor-sleep penalty or sleep-quality
simulation in this release.

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

The deterministic simulation portion now has a permanent smoke test over three
64 x 64 seeds. Each seed runs the 12-goblin start for 6,000 unthrottled ticks
(ten minutes at normal simulation speed), reloads at the midpoint and verifies
identical continuation, valid living positions, intact starter objects and
unique sleeping-mat reservations. Visual darkness, hunting interaction and
manual construction/dismantling remain items for the human tester pass.

The 2026-09-03 automated gate passes 673 solution tests, the Godot Release C#
build and the content/localization checks. The previously refreshed Windows
tester package launched headlessly with exit code 0, but it predates the food
preservation slice. Godot completed that earlier packing run but its editor
process remained idle after its shutdown messages and had to be interrupted;
repeat the export on a workstation with the Godot executable available before
treating the new tester package as release-clean.

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

Implemented end to end. The enabled cooking-fire slot has a one-cell blueprint,
a 3-wood construction requirement, the shared workshop work-order flow and a
dedicated top-down painter. Its seven primitive recipes retain exact food
identity through hauling, workshop delivery, cancellation, save/load and state
hashing. Raw ingredients remain perishable even after delivery to a waiting
order. The light emitter uses the existing `WorkOrderInput` activation contract,
so an idle or unsupplied fire does not pretend to burn. EN/PL names, automatic
order labels and recipe text are catalog-driven.

Physical food now carries a saved expiry tick on the ground, in storage, while
being hauled, in personal provisions and in workshop buffers. Fish and raw meat
last 2 days, berries and mushrooms 4, roots 8, cooked food and medicine 30, and
dried rations 180 demo-calendar days. Spoiled portions disappear at a day
boundary and add one nutrient each to an existing tribal compost. Compost
nutrients may pay the direct substrate cost of a new bud but never replace the
tribe's required edible reserve. Wild berry and mushroom biomass is cleared at
the beginning of winter and restored at the beginning of summer.

#### Lichen and mana reagent

Implemented as a complete acquisition-to-stock slice. The dedicated cave-lichen
designation targets only visible generated lichen, ordinary foraging work removes
the selected patch, and the depleted-position set participates in world versioning,
format-79 persistence and deterministic hashes. Lichen and mana are concrete
`Materials` variants, so existing hauling and materials storage handle them without
introducing a second inventory system. The cooking fire converts one lichen and one
mushroom into one mana and may select the recipe automatically. Spell casting,
mana consumption and lichen regrowth are deliberately deferred.

#### Workshop progression split

Implemented as an attainable second production tier. The starting workshop
keeps genuinely primitive recipes: sling, primitive
axe and pickaxe, bone knife, fighting stick, stone club, hide/reed clothes and
waterskin, together with the wooden bucket and simple box. The fitted workshop
costs 8 suitable wood, requires Building level 1 and a primitive axe, and owns
the reinforced pickaxe, barrel, chest and bulk-bin recipes at workshop level 2.
It is available in the advanced-production menu, has its own top-down bench/tool
silhouette and survives save/load with the concrete delivered wood variant.

The progression cannot trap a new tribe: a ready primitive workshop is generated,
it can craft the axe required to build the fitted workshop, and the early bucket
and simple storage box remain primitive.

### P2: after the first tester weekend

#### Buildable watchtower

Implemented as a 2 x 2 timber lookout with a raised walkable platform and a
dedicated top-down painter. Up to two assigned guards return to the upper post
after satisfying personal needs and receive doubled personal vision and ranged
reach while on the platform. The tower owns two reserved sleeping places and a
small food store, but no water container. Assignment and amenities survive
save/load and are removed together with the tower. A built-in ladder uses the
ordinary vertical-navigation contract, so the post and its food store are
reachable immediately after construction.

#### Ladders and real vertical scaffolds

Ladders are not cosmetic ramps. The first directional ladder slice now uses a
real vertical-passage contract, pathfinding edges, construction occupancy,
visibility, save data and 2D/3D rendering, including access to raised watchtower
platforms and other player-made elevated surfaces. Watchtowers additionally
carry their own ladder as part of the completed structure.

#### Modular inhabited ruins

Allow players to repair captured/generated masonry with material-aware wall
patches, roofs, room designation, comfort and decay. This is the long-term
replacement for treating a ruin as a disguised hut.

## Suggested implementation order

1. Characterize current new-game fingerprints, generated-object occupancy,
   initial reachability and shelter with tests.
2. Implement and validate the pure starter-ruin plan.
3. Materialize only existing authoritative structure kinds and prove save/load.
4. Add the dedicated top-down painter and deterministic dressing variants.
5. Add minimal shelter adaptation and fauna exclusion/tuning if required.
6. Run the complete release gate and capture screenshots from several seeds.
7. Functional compost, primitive cooking, food preservation and the first
   lichen-to-mana reagent loop are now complete vertical slices.
8. The first workshop-progression and single-level watchtower slices are complete;
   treat ladders and further production tiers as subsequent vertical slices.

## Explicit non-goals for the weekend build

- bed assignment, sleep quality or private rooms;
- a general waste/odor/disease economy;
- walkable upper stories or ladder pathfinding;
- procedural collapse, structural stability or ruin decay;
- replacing all existing construction art;
- mandatory 3D visual parity unless the tester build exposes the 3D view;
- moving recipes without a complete replacement production path.

## Decisions selected for implementation

1. Is the tester build 2D-only, allowing the 3D view to use its current generic
   structure fallback for the new starting composition?
2. Should the compost hollow be functional in the first tester build, or should
   it begin as dressing while existing hut-based reproduction remains intact?
3. Should the starting ruin contain a ready-to-use primitive workshop and
   shelter at no construction cost, or should goblins start among ruins and
   spend the first minutes adapting them?

Selected answers are: hide 3D completely; ship functional corpse-fed compost;
start with a larger weak tribe; and provide a completed primitive workshop plus
enough inherited shelter. A harder adaptation-first opening remains a later
scenario or challenge mode.
