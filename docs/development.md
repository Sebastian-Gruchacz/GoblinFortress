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

Benchmark one complete demo day without rendering:

```powershell
dotnet run --project .\src\GoblinStronghold.Headless\GoblinStronghold.Headless.csproj --no-build --no-restore -c Release -- --benchmark-day
```

Build and open the Godot sandbox with the detected local editor:

```powershell
.\run-goblin-stronghold.ps1
```

The sandbox starts with one food stockpile at the goblin spawn and deterministic loose brushwood scattered across fertile land, including a small reachable starter supply. Left-click a visible goblin to select it and open its resizable, scrollable detail window. Left-click a stockpile on any discovered cell, including one currently under fog, to configure its target quantity, optionally reserve incoming deliveries for one named goblin and optionally select one compatible upstream stockpile. Without an explicit source, requested goods may come from loose stacks or surplus above any other stockpile's target. **Praca…** provides draggable gather-food, gather-brushwood, uproot-bush, tree-and-stump felling and clear-designation tools; food targets include berries, mushrooms, edible roots and fish shoals. Previously explored but currently unseen cells use the same interaction policy as visible cells for inspection, construction and work marking, while wholly unknown terrain is excluded. Harvested berry bushes remain visible without fruit and regrow berries in summer; mushrooms regrow in spring and autumn, roots outside winter and fish throughout the year. Uprooting permanently removes the bush and earns building experience. The felling selector retains only concrete trees and dead swamp stumps. One starting goblin has the tribe's wooden axe and provisional building competence, finds a reachable cardinal work cell beside each reserved target, and turns a living tree into a physical stump plus one sixteen-piece wood stack per trunk level. Harvesting a pre-existing or previously created stump removes it and yields a deterministic 8–16 pieces of wood. Satiated goblins do not gather wild food without a designation, while hunger may still create emergency foraging. Right-click no longer opens construction or issues a default movement order: a click cancels the active tool or clears selection, dragging pans the camera, and right-clicking inside a detail window closes that window. Future movement returns through explicit or contextual move, patrol, attack and occupy actions. Food and wood stockpiles each cost 2 wood. Wooden walls are laid as a dragged cardinal line, cost 2 wood per independently simulated segment and become solid navigation obstacles. A one-wood doorframe connects to neighboring walls, remains passable and can atomically replace an already completed wall segment. A separate one-wood door leaf can then be built in that frame; it starts closed and left-clicking it orders an open/close toggle. A wooden walkway costs 1 wood per cell, uses connected illustrated tiles and is laid by dragging from its first to its last cell; it may cross both land and water. Escape also cancels the active tool.

The game now opens on a full title menu rather than immediately entering the generated sandbox. It offers a new world, newest-compatible load and exit; after a session starts it also offers return to game. `Esc` opens the same paused menu when no tool gesture is active, while the compact in-game session panel retains **Menu** and **Zapisz**. **Nowa gra** first protects an existing session with an autosave and then creates a new random seed. **Zapisz** (`F5`) writes `quicksave.json`; **Wczytaj** (`F9`) loads the newest compatible file across the quicksave and three rotating autosave slots, falling back to an older slot if a newer JSON file is damaged or incompatible. Autosaves occur at the beginning of each simulated day and use atomic temporary-file replacement. Godot stores these files under its per-user `user://saves` directory rather than inside the repository. Replacing a session clears non-authoritative interpolated actor positions before applying the loaded snapshot, so a marker cannot animate from a later abandoned position after load.

`F3` or the **3D** button beside the session controls switches to an experimental renderer driven by the same authoritative map and simulation snapshot. It batches terrain into 16×16-cell meshes, gives ramps and cliff faces physical geometry, renders water as a separate translucent surface and batches the currently simple cutaway structure geometry without roofs. Goblins use lightweight 3D markers and remain selectable; camera pan, zoom and minimap navigation work in either renderer. The 3D probe starts with a strictly top-down orthographic camera. `F4` or the angle button compares it with the oblique view, while `Q`/`E` or the adjacent arrows rotate either camera mode in exact 90-degree steps. The stable 2D renderer remains the startup default and the button always permits an immediate return. This first probe intentionally renders the complete surface without fog, cave cutaways, textured materials, animated water or sprite-based furniture. Those are renderer concerns rather than a second simulation model; later digging can invalidate only affected terrain chunks, while furniture and foliage may use billboarded sprites or instancing above structural 3D walls. Isometric projection and rotation are deliberately not applied to the established 2D renderer until the experiment proves that their readability benefit justifies rotated assets, selection transforms and construction-preview changes.

World atlases now have a deterministic build-time baking pipeline. Run `tools/bake-assets.ps1` to execute its recipes sequentially through headless Godot. The connected-tile recipe `AssetRecipes/connected-walkways-v1.json` takes the six authored walkway shapes, expands them into all sixteen directional connection masks and produces bogwood, oak and pine material rows. The generated PNG is used directly by the 2D renderer, so it no longer rotates the reduced shape set while drawing. The geometry-aware `terrain-height-transitions-v1.json` recipe samples meadow, deciduous-floor, conifer-floor and bog textures, then produces all sixteen 4-bit high-edge combinations per material through deterministic displacement, directional shading and edge treatment. Current one-direction ramps use masks 1, 2, 4 and 8 immediately; corner, opposing-edge and multi-edge cells are already available for later terrain editing. Cliff faces remain a between-cell overlay rather than being incorrectly baked into one tile. The `cave-walls-v1.json` recipe interprets the same north/east/south/west bits as open cave neighbors. It rotates the authored frontal rock face toward each exposed edge and blends multiple orientations for corners, opposing faces, tees and isolated rock columns, producing sixteen masks for both sandstone and granite. The underground renderer selects these generated regions directly from cave topology. `connected-structure-walls-v1.json` applies the shared connection convention to human oak and goblin bogwood construction rows. Human buildings whose logical door faces south retain their stronger whole-room illustration; other orientations now render from authoritative floor, wall, door and roof parts, with separately drawn passable doorframes instead of rotating furniture and painted lighting. The existing walkway-derived master is an integration-quality placeholder: an artist can replace it with thinner plaster/timber and wattle source shapes without changing recipes, masks or renderer code. JSON manifests record every material/topology rectangle and a SHA-256 hash over the baker version, recipe and source atlas. Generated atlases are build/package inputs rather than per-user first-launch work: production packaging should bake and validate them before distribution. Player-built door leaves are separate interactive world objects rather than part of the frame texture; their current procedural rendering is ready to be replaced by authored open/closed sprites. Seasonal color, wetness, damage and dirt should remain shader or overlay parameters where possible instead of multiplying every baked combination.

Player-built wall rendering classifies connections, enclosed-room sides, sides covered by solid structures and final visible faces independently from atlas-mask selection. A cardinal flood fill starts at every unblocked map-edge cell; non-wall and non-solid cells it cannot reach form enclosed interiors. Room-facing sides take precedence. Without a room, a free-standing straight wall exposes both sides, an elbow exposes the inside of its angle, a tee exposes the missing-arm side and a cross retains only its neutral top. Solid material suppresses its adjacent face, leaving the empty side visible; this is the extension point for masonry built against cave rock. Doorframes participate in the enclosure boundary regardless of door state, so opening a door does not visually invert the room. The analysis is cached by the actual barrier and structural-solid position sets rather than the general world version. Cave rendering supplements the sixteen cardinal wall masks with diagonal inner-corner detection: when a rock cell touches open cave space only diagonally, the renderer draws the matching atlas corner quadrant instead of leaving a square dark notch.

New worlds also scatter deterministic small stone stacks across traversable land and place solid natural boulders. Small stones use an explicit area designation and the ordinary hauling pipeline. A designated boulder requires the provisional primitive pickaxe and yields 16–32 physical stone units when removed. The dedicated 64-unit stone stockpile costs 2 wood. One goblin receives a starter pickaxe for the playable prototype; capture of the corresponding tool from the human village remains the intended replacement once equipment loot exists.

The stockpile settings distinguish a local destination priority from the tribe-wide priority of the accepted resource. Editing the latter through one stockpile changes that resource policy for every hauling decision.

Goblin huts and field camps are stationary fog observers with a configurable radius of three cells. This is part of the vision settings rather than a universal building rule: stockpile zones, walkways and natural objects do not reveal terrain. Later watch posts, occupancy, light sources and per-structure day/night ranges can use the same observer pipeline.

The current `demo-temperate` climate profile gives every season 10 days. At `1×`, each of its days lasts 19 real minutes: 12 minutes of daylight followed by 7 minutes of night, with dawn at 05:00. These are demo data, not calendar invariants. Every climate season independently defines its day count, daylight and night tick lengths, dawn and dusk civil times. The calendar resolves variable day boundaries without a global modulo, while the centered annual HUD sizes its seasonal bands from the profile's actual time proportions. This is the extension point for future map climate zones and season-dependent photoperiods.

Goblin sight is reduced at night; human cohorts use short-range lantern light. Two carried food portions and two waterskin portions cover roughly one demo-temperate day. Untreated dehydration becomes fatal after about three such days and starvation after about ten. A typical goblin reaches forced sleep after roughly one and a half days awake; huts and field camps provide proper rest, while an exhausted goblin that cannot reach shelter collapses where it stands. Illness and accident consequences of exposed sleep remain a later simulation slice. Need rates remain simulation-definition data and will require climate-aware calibration when non-demo profiles become playable.

A field camp prepares at most five goblins for a raid. The selected party provisions, rests and rallies there before marching on the village; other tribe members remain available to the ordinary work dispatcher.

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
- Godot new-game, quick-save and newest-compatible-load controls plus three atomic daily rotating autosaves;
- pause and normal, 2x, 4x, 8x and unthrottled runner modes;
- lightweight execution metrics;
- deterministic regional terrain generation with coherent multi-octave value noise;
- a directed v4 layout: swamp pressure from the left and bottom, a meandering diagonal river, and a drier upper-right village region;
- physical forest trees with 3×3 overhead crowns around the village and sparse dead stumps in the swamp;
- moisture, fertility and traversal costs;
- surface coordinates that reserve a height component;
- explicit floor elevation per terrain column: shallow water has a surface-level floor, while generator v3 and later deep water exposes a submerged floor at `z=-1` and is impassable to default humans and goblins;
- validated goblin and human settlement sites;
- traversability queries and map fingerprints;
- generated-map ownership in snapshots, hashes and saves;
- versioned map generation recorded in snapshots, fingerprints, hashes and saves;
- a mutable runtime vegetation layer above the immutable generated baseline;
- deterministic habitat-driven berries, mushrooms, edible roots and fish shoals, with local biomass depletion and interval-based regrowth;
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
- a Debug-build-only visibility aid that lets living non-player map units reveal fog; Release builds disable it automatically;
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
- Godot selection outlines plus right-drag camera panning and right-click cancellation, deselection and window closing;
- standard `Alt+Enter` switching between windowed mode and fullscreen at the current monitor resolution;
- two-portion personal food and water capacities independent from carried work cargo;
- physical transfer of food from world stacks into personal provisions and recovery on death;
- shallow-water refill jobs for primitive personal containers;
- individual thirst, automatic drinking and dehydration damage;
- slower authoritative actor movement at one cell per second in normal speed;
- non-authoritative Godot interpolation that keeps `1×` movement continuous and scales with acceleration;
- a generated 4×6 transparent icon atlas shared by toolbar actions, tile submenus, selected-goblin needs and overhead intents, with distinct construction, uprooting and tree-felling symbols;
- a second transparent 4×4 atlas for physical resources, goblin equipment, human wooden tools, provisions and generic cargo;
- icon-only time, building, work and raid buttons with hover tooltips instead of permanent button captions;
- two-column tile menus for construction and work designations, including cost and gesture details in tooltips;
- overhead illustrated intent icons for forage, haul collection/delivery, rest, eating, exploration, ordered movement and food/water resupply;
- stable seeded goblin names, known skills, traits, work preferences and primitive personal equipment preserved in saves;
- a live resizable and scrollable selected-goblin detail window with icon-and-progress need meters, possessions, cargo and full job state;
- resizable overview windows for warehouse-only totals, known loose ground goods, the complete goblin roster and live simulation statistics, grouped under the final `Σ` tile menu on the bottom action bar;
- a shared one-pass resource inventory snapshot that separates stored, known loose and actor-carried quantities for both UI and dispatcher consumers; the work-designation-only `Vegetation` pseudo-resource is excluded, while food tooltips retain their concrete food-kind breakdown;
- consistent positive need meters: health, satiety, hydration and stamina are all worse toward the empty left edge;
- illustrated inventory slots for rag clothing, waterskin, bone knife, personal food/water portions and the currently carried work stack;
- one waterskin slot with an adjacent bottom-to-top fill meter instead of a duplicate water-stack icon;
- map item sprites selected from the authoritative resource kind and human-cohort tool icons selected from their role and current task;
- `Page Up` / `Page Down` inspection of generated vertical levels, including submerged floors and upper structure parts;
- persistent construction sites created without requiring current materials or an available builder;
- blueprint-derived wood demand, physical delivery from the nearest reachable loose or stored stack, and separate construction work;
- player-built single-cell food stockpiles and draggable multi-cell wooden walkways over land or water;
- dynamic walkway footprints stored as ordinary authoritative world objects and included in traversal, hashes and saves.
- deterministic generator-v6 cave volumes on `z=-1` and `z=-2`, with sandstone and granite, excavated chambers and corridors, a surface cave mouth and one natural descent;
- a generic 3D terrain-neighbor and route contract that crosses explicit vertical passages without allowing movement through solid rock or cliffs;
- an underground generator-preview renderer with separate rock floors and topology-aware generated sandstone/granite wall faces, plus surface cave-mouth marking and layer inspection;
- deterministic physical brushwood stacks scattered on traversable fertile ground, plus designated tree felling that produces physical wood stacks;
- dedicated wood stockpiles, after which autonomous haulers collect visible brushwood and deliver it physically;
- persistent experience and derived levels for foraging, hauling and building, shown in the goblin detail window;
- experience awarded only for completed gathering, delivery and prototype construction outcomes and preserved in hashes and saves.
- authoritative rectangular work designations for berry gathering and brushwood collection;
- dispatcher selection that keeps ordinary resource work player-directed while retaining hunger-driven emergency foraging;
- one-shot work areas that disappear when their matching physical resources are exhausted and can be cleared manually;
- per-stockpile delivery requests with an explicit on/off state, target quantity, local priority and optional assigned hauler;
- soft logistics duty that makes a feasible assigned delivery outrank construction, public hauling, exploration and gathering without creating an idle-only profession;
- tribe-wide priorities for each storable resource kind, composed above local stockpile priorities;
- concrete food stacks for dried rations, berries, mushrooms, edible roots and fish;
- per-kind satiety configured independently: berries 1,800, mushrooms 2,200, roots 2,800, raw fish 3,200 and dried rations 3,600;
- a small food stockpile with three food-kind slots of 32 portions each, rather than one fungible food counter;
- hauling candidates admitted only by a matching brushwood designation or a destination stockpile request;
- work designations, stockpile demand, global and local priorities and hauler assignment included in snapshots, deterministic hashes and saves;
- inter-stockpile transport that preserves each source stockpile's requested quantity and moves only its surplus;
- an optional explicit upstream stockpile link that excludes loose ground stacks and every unselected stockpile;
- a bounded point-route cache behind the navigation-service boundary, invalidated by authoritative topology changes and exposed through diagnostic metrics;
- live stockpile-delivery diagnostics that distinguish satisfied demand, cargo already in transit, missing allowed sources, protected source reserves, incompatible destination slots, unreachable cargo, missing workers and a busy assigned hauler.

Manual transport commands still resolve atomically after a path check. Autonomous foraging and hauling use explicit routes and action duration. The current hauler compares tribe-wide resource priority, local destination priority, total route length and stable identifiers in that order; it still respects filters, carry capacity and physical reservations. A stockpile may accept every free worker or only its assigned hauler. Feasible assigned deliveries form a soft duty above construction and public work, while survival needs and raid preparation remain higher; an assigned goblin returns to the public dispatcher when no such delivery can be performed. Changing an assignment or relevant demand wakes that goblin from background exploration. The optional upstream link accepts only surplus physically stored in that source. Construction demand otherwise competes ahead of ordinary stockpile pulling and may consume reachable loose or stored wood. The dispatcher does not yet implement priority inheritance for workshop orders, multi-source allowlists, strict professions or scheduled dedicated routes.

`GeneratedMap` is the immutable generated baseline owned by the simulation. Its generator version is included in snapshots, fingerprints, state hashes and saves; unsupported versions fail explicitly instead of silently regenerating another landscape. Generator v5 expands the default active region from 32×32 to 64×64 and combines designer-authored macro masks with seeded, coordinate-addressed multi-octave value noise. Elevation, terrain and moisture fields are sampled separately. Dry surface relief currently spans levels −1 through +2 around the river valley; a one-level boundary may expose an explicitly oriented traversable ramp, while every unpaired elevation edge is an impassable cliff. The guaranteed settlement route keeps its river crossing as shallow water rather than replacing the river with mud. Ramp rendering now uses a small directional edge highlight, downhill shadow and transverse contour instead of the former oval atlas overlay that resembled an isolated sinkhole at gameplay scale. Cliff shadows remain derived from neighboring heights. Initial human cottages, barn and well require one flat, ramp-free `SolidGround` footprint on ordinary maps, so their placement cannot spill into the river merely because shallow water is technically traversable. Generator v6 adds two materialized cave levels made from sandstone and granite. It carves connected rooms and corridors, reserves a visible cave mouth from surface structures and connects `z=0`, `z=-1` and `z=-2` through explicit vertical passages. A generic terrain-route query traverses those passages while rejecting solid rock; actor jobs still use the surface navigation service until underground movement orders and visibility exist. Underground layers are therefore shown as a generator preview rather than pretending that surface fog is valid below ground. This follows the useful separation described in [Making maps with noise functions](https://www.redblobgames.com/maps/terrain-from-noise/), while the river is placed as a primary regional feature rather than expected to emerge accidentally from thresholds. The river-first choice follows the structural lesson of hydrology-led terrain generation without attempting its full drainage-network simulation; see [Terrain Generation Using Procedural Models Based on Hydrology](https://www.cs.purdue.edu/cgvlab/www/resources/papers/Genevaux-ACM_Trans_Graph-2013-Terrain_Generation_Using_Procedural_Models_Based_on_Hydrology.pdf). Historical generator versions 1–5 remain callable for existing saves. `WorldMapState` owns the mutable ecology and natural-object overlay: berry bushes, mushroom clusters, edible roots, fish shoals, living trees and dead stumps. Trees can be placed on flat hill plateaus and remain stable multi-level spatial objects. Their deterministic trunks occupy one to three vertical cells and their 3×3 crowns begin only above the last trunk segment. Felling is now a concrete, reservable area job and changes topology by replacing the complete multi-level tree with its stump. The prototype scatters a sixteen-piece wood stack for every former trunk level in the direction away from the worker, falling back to the worker's cell where terrain blocks that line. The same designation can remove that remaining stump or a generated swamp stump for a seed-stable yield of 8–16 wood. A separately rendered fallen-trunk object, collision along its full fall path, raw-log hauling and an explicit splitting job remain future work. Fish require traversable shallows in a connected water body of at least twelve cells. Harvest and regrowth advance the world version and publish dirty-cell deltas without changing the baseline fingerprint.

The ecology prototype has several food niches but is currently locked to summer rather than running a complete calendar. Berry biomass represents fruit, not the bush itself: gathering may reduce it to zero while the bare bush remains and later fruits again. The separate uproot designation removes that persistent source permanently. Human field expansion materializes the equivalent clearance before occupying a tile, and initial fields are placed only on pre-cleared ground. The model still has no habitat spread, fish movement or population collapse from overharvesting. Mushrooms and fish currently recover biomass twice as quickly as berries and roots. Later world-mutation slices add coordinate-addressed region generation, ground and construction overlays, per-domain topology versions and larger dirty regions. Rest currently treats any reachable floor or doorway in a goblin hut as unlimited primitive sleeping space; beds, occupancy, comfort and shelter quality are deferred. Goblin health currently falls from starvation, dehydration and human-guard attacks; it has no natural recovery or healing job, so eating after a crisis stops further starvation damage but does not restore lost health. Fog uses a circular radius without line-of-sight or lighting occlusion, and the explorer uses authoritative topology to select a nearby frontier. Corpses, wounds, sickness, healing, stealth and threat-driven safety jobs remain later slices.

The first human village model is deliberately coarse and authoritative rather than a second full actor simulation. Its three moving map markers represent work cohorts, not twelve individually simulated humans. Daily food, wood and goods changes are fixed and do not yet react to seasons, weather, damaged facilities or shortages. The first combat probe treats the guards as one aggregate health pool: it has no weapons, armor, wounds, poison, surrender, capture, corpses, retreat or tactical orders yet. Hostility currently persists once raised and only living guards detect and pursue intruders. Observation-driven materialization of notable individuals and reports about knowledge carriers belong to the next slices.

The simulation still supports a resumable single-goblin move command, but the Godot client no longer binds it implicitly to right-click. Player movement needs an explicit or contextual action vocabulary—ordinary move, patrol, attack and occupy—before returning to the toolbar. It has no box selection, groups, waypoints, formations or path preview yet. Hunger, thirst or fatigue may suspend an issued movement command; after eating, drinking or rest the target is revalidated and a fresh route resumes it. A carried item remains physically attached and is delivered after the move when possible.

Personal food remains part of the physical tribal total even while packed by a goblin, and the primitive two-portion pouch retains its concrete food kind so eating uses the correct configured satiety. Personal water currently represents two abstract portions in a primitive container and is gathered from traversable shallow water without depleting the water body; each automatic drink consumes one portion. The UI presents those portions as a continuous vertical fill meter so the later model can replace them with volume without changing the inventory layout. Hunger and thirst now grow against the climate calendar, trigger eating and drinking well before damage, and cause much slower starvation or dehydration. The demo climate uses 7,200 daylight ticks and 4,200 night ticks at ten authoritative ticks per real second: light therefore lasts exactly twelve real minutes, darkness seven and a complete 24-hour cycle nineteen minutes at `1×`. Both phases use the same clock compression. With dawn at 05:00, their 12:7 proportion places dusk at approximately 20:09 and the next dawn at 05:00, rather than treating both phases as twelve simulated hours. The configured presentation cadence changes how real time maps to authoritative ticks without changing simulation rules or making individual updates progressively denser. `SimulationDefinitions` exposes grouped clock, actor-need, food-nutrition and storage settings while retaining the flat compatibility properties. Cooking recipes, herbal effects, freshness, spoilage, preservation, quality and contamination remain later layers built on concrete food identities. Container crafting, exact volume, body-size and trait modifiers, exertion, temperature, illness and boiling are also deferred. The initial containers begin filled. There is no cleaning job yet, so the intent-icon vocabulary covers only implemented work and will gain a cleaning symbol with the corresponding physical dirt/refuse system.

Godot interpolates actor markers between authoritative cells but never feeds presentation positions back into simulation, saves, visibility or combat. At `1×`, the current movement cadence is one cell per second. Higher speeds scale the visual catch-up rate and may still skip when the renderer cannot present every simulation step, which is intentional. `Page Up` and `Page Down` select the rendered z-level; non-surface views currently expose generated floors and structure parts but intentionally hide surface actors, work overlays and construction interaction.

Construction commands preserve the currently viewed level instead of silently projecting every click onto `z=0`. Food and wood stockpile blueprints may already be planned on an open generated cave floor and are rendered and persisted on that actual underground level. They deliberately remain unsupplied and unbuilt while actors and the job dispatcher are surface-only; the dispatcher treats such sites as unreachable without repeatedly searching an invalid surface route. Walkways and field camps remain surface blueprints, while rectangular gathering work remains disabled away from `z=0`. Excavation designations, underground visibility and generic actor navigation will turn the persisted cave plans into executable work.

Generated settlement structures currently validate the spatial contract but are static: they have no materials, construction progress, damage, fire or functional rooms yet. Player orders now create persistent construction sites with an authoritative footprint, blueprint material demand and remaining work. A site is valid even when no required material or worker currently exists. Goblins physically collect and deliver wood, then a capable builder performs the stored work; completed delivery and building award their respective experience. Hunger, thirst and fatigue can still displace either task, while delivered materials and site progress remain in the world. The current stockpile, walkway, wall, doorframe, door-leaf and field-camp blueprints are primitive and intentionally require no prior skill or tool, so every goblin can build them. A completed wooden wall occupies the solid spatial channel and invalidates cached navigation, while a doorframe uses the passable door part in that same channel. A wall-line order stores its complete footprint and total material/work demand, validates the whole line again at completion, then materializes one world object per segment. A doorframe order may target empty ground or replace one completed wooden wall while preserving that segment's object identity. A door leaf occupies its own fixture channel, persists open/closed state independently from the frame and changes navigation topology when toggled. Goblin path searches treat a closed tribal door as an openable transition, while physical and human-village searches continue to treat it as blocked. A traveling goblin marks the door as temporarily open before entering on a later movement interval. Temporarily opened doors close once no actor occupies the doorway or has it as the next route step; manually opened doors remain open until explicitly closed. Closing is rejected while an actor, approaching route, living human cohort, loose ground stack or stockpile occupies the doorway. Adjacent player-built barriers are rendered as one connected network even though each remains an independently damageable future object. Builders work from cardinal cells beside solid walls and closed-door sites, so completion cannot seal the builder inside its own segment. The capability schema already persists required skills, minimum building level and equipment for later advanced blueprints. The map renders each site with separate material and work progress, and saves/hashes preserve unfinished orders, door state and assigned jobs. Walkways and walls cannot share an occupied cell, and their current drag line is a deterministic cardinal staircase. Generated trees and stumps now occupy the same spatial system as buildings, and the felling job mutates or removes them. Renewable deadwood and connected stream tributaries are not implemented. Multiple material kinds, substitutions, staged parts, per-blueprint tools and specialist knowledge remain later construction slices.

Wall-mounted objects now share a deterministic side resolver based on the same enclosure analysis that renders wall faces. A completed doorframe stores its resolved north/east/south/west mounting side in the authoritative world object, and its later door leaf inherits that side; rendering no longer rotates either object when neighboring walls change. A single room-facing side wins, while a free-standing two-sided partition preserves a valid preferred side and otherwise uses a stable axis default. Corners and T-junctions still need an explicit arm-selection gesture before torches, shelves, trophies and similar fixtures can be placed there without ambiguity. Freestanding furniture is deliberately outside this contract and remains a lower top-down sprite that does not need wall-facing variants.

The dispatcher stores one active job plus one suspended resumable intent per goblin. Eating, drinking or rest can temporarily displace ordered movement, exploration, gathering or clearance; after the need is satisfied the target is revalidated and the route is rebuilt. Dynamic hauling releases its reservations and is selected again from the public work pool instead of preserving stale ownership. Each goblin now exposes a short, deterministic personal plan containing the current commitment, rising hunger/thirst/fatigue intentions and the work to resume. Need pressure grows from its planning threshold to a configured maximum; it interrupts only after exceeding the current job's configured commitment plus hysteresis. Ordered movement therefore resists mild hunger longer than background exploration, while critical reachable needs still take over. This plan is derived from authoritative state rather than persisted as stale reservations, so save/load reconstructs it exactly. Future public jobs are not yet pre-reserved in that queue. Each goblin also has deterministic, saved inclinations from strong dislike to strong enjoyment for foraging, hauling and building. Explicit public priority has a deliberately larger score step, so an urgent order always outranks an ordinary favorite; preference only separates work of comparable priority, and an infeasible candidate falls through without claiming a worker. Older saves derive the same inclinations from their world seed and actor identifiers. Expensive background exploration is staggered across movement intervals, and the designated explorer slot remains separate from preference scoring. Hauling rejects sources without a matching designation or requesting destination before running pathfinding. Point-to-point route results are shared through a bounded navigation cache until a structure changes traversability. Soft reservations for later actions and plan stabilization beyond interrupt hysteresis remain the next scheduling layer. One autonomous explorer still operates without a player work area. Stockpile requests consume loose ground stacks and surplus above other stockpiles' targets unless one compatible upstream stockpile is selected explicitly. Global resource priority outranks local stockpile priority, which outranks route length. Multi-source policies, dedicated carrier professions, workshop inheritance and timed or threshold-driven routes remain in the deliberate-logistics milestone.
