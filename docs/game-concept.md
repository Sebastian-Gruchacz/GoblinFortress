# Goblin Stronghold — game concept

## Document status

This document is the canonical high-level design. It incorporates the recovered original concept and subsequent author decisions. Detailed implementation order and acceptance criteria live in [first-playable.md](first-playable.md); historical source findings live in [legacy-audit.md](legacy-audit.md).

The notation used here is:

- **Decision** — part of the intended game unless playtesting disproves it;
- **Working rule** — concrete enough to implement, but expected to be tuned;
- **Deferred question** — intentionally unresolved and not allowed to block the current milestone.

## High concept

Goblin Stronghold is a settlement and community simulation viewed from the side of traditional fantasy antagonists. The player guides a fungal goblin tribe living beside older, more capable societies.

Goblins are individually expendable, violent and initially limited. Their civilization does not advance primarily through abstract research. Goblins acquire practical knowledge from intelligent creatures they devour, but that knowledge initially survives only in living carriers. The tribe must locate valuable minds, seize them and preserve what it learns before its carriers die in battle, accident, illness, old age or internal conflict.

The central fantasy is not building a perfect fortress. It is dragging a fragile culture out of mud, hunger and violence by stealing civilization one mind at a time, then deciding what kind of civilization those stolen minds create.

## Design pillars

### Knowledge has a body

**Decision:** Skills and advanced capabilities are held by individual goblins. A master builder, farmer or spell carrier is therefore both a citizen and a vulnerable cultural asset.

Completed buildings, tools, plans and fields remain after the last knowledgeable carrier dies. They are not erased, but the tribe may no longer be able to operate, maintain or reproduce them. Loss changes the player's options instead of rolling the world backward.

The player should care about what is inside a goblin's head without turning every goblin into a conventional heroic RPG character.

### Progress is stolen, not researched

**Decision:** Contact with other species and societies is the main source of new cultural capabilities. Observation identifies valuable prey; capture, scavenging, diplomacy, trade and violence create access; devouring transfers only part of what the victim knew.

Ordinary practice improves skills the tribe already possesses. It cannot independently invent locked capabilities such as agriculture, advanced woodworking, metallurgy, writing or formal magic.

### Goblin society is productive chaos

**Decision:** Goblins have needs, dispositions and social pressure. They accept priorities and jobs rather than perfect direct control. Hunger, fear, aggression, loyalty and status can interfere with an efficient plan.

Internal disorder is a source of stories and strategic risk, not only a failure state. A succession struggle or impulsive fight matters more when the participants carry irreplaceable knowledge.

### Civilization reflects its diet

**Decision:** Consumed minds can influence more than technical competence. Later systems may transfer fragments of temperament, morality, loyalty, language and magical practice.

The player's choices of prey and preservation methods can therefore shape a brutal scavenger tribe, a disciplined imitation of a human town, an unusually lawful dwarf-like society or something less recognizable. This is systemic expression, not a fixed alignment selector.

### The world exists beyond the tribe

**Decision:** Nearby villages and later settlements, castles and cities continue producing, consuming and responding while outside the camera. The first implementation contains one human village; the architecture permits more societies without requiring every distant person to be simulated individually.

Destroying a settlement removes future crops, goods, travelers and knowledge sources. It can also create refugees, investigations and retaliation. Predation is immediately rewarding but can consume the ecosystem on which the tribe depends.

### The settlement grows in layers

**Decision:** The simulated world uses discrete height levels. Swamp surface, water, tree canopy, natural caves and excavated depths can eventually form one connected settlement.

Presentation remains clean top-down 2D. The simulation is vertically structured even though the renderer does not need fully three-dimensional art. Cutaways, shadows, layer separation and optional parallax may communicate height without making tile selection ambiguous.

Water occupies a column rather than replacing the elevation model. Puddles and shallow water have their bottom on the currently viewed level and can be waded through. Deep water means that the surface-level floor has dropped away: its known submerged bottom is at least one discrete level lower and may continue into not-yet-generated depth farther from shore. Goblins and humans do not swim by default. They require a walkway, bridge, boat or a later learned swimming capability to cross deep water.

### The map is living state

**Decision:** World generation creates initial conditions, not permanent scenery. Crops and trees grow and are harvested, animals establish or abandon habitats, structures are built, damaged, burned and demolished, and earthworks expose or remove terrain. Later mining creates excavated spaces, supports, collapses and connections between height levels.

The authoritative world separates several concerns rather than encoding every combination in one tile type:

- a versioned generated baseline, including elevation, geology and original water or soil conditions;
- mutable ground and hydrology, including excavation, fill, fertility, moisture and contamination;
- vegetation and habitat state with growth, depletion, succession and disturbance;
- constructed occupancy, condition, ownership and active hazards such as fire;
- loose resources, actors and jobs located in the world but owned by their respective systems;
- derived navigation, visibility, room, support and rendering data that can be invalidated and rebuilt.

Every mutation identifies affected cells or regions and advances the relevant topology or visual version. Systems consume compact change records instead of rescanning the whole map every tick. Rendering rebuilds dirty chunks, while navigation and visibility invalidate only caches whose assumptions changed.

Early wild food already follows habitat rather than a single generic forage roll: berries prefer fertile damp ground, mushrooms prefer very wet soil, edible roots prefer fertile land, and fish shoals occupy only the traversable shallows of sufficiently large connected water bodies. Each source has local biomass and capacity. Harvesting a berry bush removes its current fruit but leaves the perennial bush in place; fruit can regrow during its active growing season. The foundation sandbox is season-locked to summer, so its interval growth is always active. Uprooting is a distinct, deliberately destructive clearance job used by goblins and by human construction planning. This remains a renewable prototype rather than a final ecosystem; a full calendar, population spread, overharvesting and competing animal consumers come later.

Save data records the generator version and authoritative mutations or current overlays. Derived caches are never required to restore the world. A generator update must not reinterpret an existing save as a different landscape. Supported historical generator versions remain callable for unexplored regions in older saves; migration must materialize enough baseline data before support for one can be removed.

## Simulation resolution

The game uses explicit levels of simulation detail.

### Detailed local actors

Goblins and other characters who are present in an active local region have individual state, including needs, health, skills, inventory, current action and social disposition. The same applies to humans or animals whose individual behavior currently matters.

### Coarse remote populations

Distant societies are represented by households, cohorts, stocks, facilities, production and scheduled events. They still consume resources, reproduce, suffer shortages and make strategic decisions, but they do not pathfind every worker to every meal.

### Materialization boundary

When a caravan, patrol, worker group or notable person enters a detailed region, the simulation expands relevant coarse state into individual actors. When those actors leave and no unresolved local event depends on them, their results are reconciled into the authoritative coarse society state.

**Working rule:** Expansion and reconciliation use deterministic records, so changing simulation detail cannot duplicate people or resources and cannot reroll an already determined outcome.

This resolves the requirement that all represented characters have personal needs without requiring every inhabitant of a distant city to exist as a permanently updated individual.

### Randomness across simulation detail

**Decision:** No subsystem, tribe or map generator relies on one mutable random stream whose future depends on how many updates happened to consume it. Random samples are addressed by stable inputs such as world seed, domain, stable entity or region identity, logical interval and sample purpose.

A tribe identity may separate its samples from another tribe, but it does not own a sequential RNG cursor. Coarse and detailed simulation evaluate different representations of the same fixed logical intervals or scheduled events. Moving a society nearer to the camera may reveal already determined detail, but may not grant extra random opportunities or shift all its future results.

Map generation uses the world seed, generator version, spatial coordinates, feature domain and generation pass. Chunk or depth generation order is therefore irrelevant. Save data records which generator version applies and which regions have authoritative mutations; a global generation-progress counter is unnecessary except for genuinely incremental work whose partial output itself must be resumed.

## Goblin biology

### Fungal origin

**Decision:** Goblins are initially sexless fungal organisms. They reproduce through several forms of budding rather than ordinary mammalian pregnancy.

Initial population growth is limited primarily by food and then by suitable space. Moisture is a critical environmental condition for primitive survival, fungal beds, food gathering and safe reproduction, especially in a swamp settlement.

### Reproductive paths

The design supports three related paths:

1. **Voluntary budding** — a living goblin produces a descendant and is weakened for a significant time.
2. **Corpse bloom** — a dead goblin may release growth from which one or more descendants emerge, allowing fragments of its experience to survive.
3. **Carcass seeding** — suitable biological remains are deliberately used as substrate for new goblins. This is less reliable and may expose descendants to traits or contamination from the substrate.

**Working rule:** A descendant may receive incomplete skill familiarity from a goblin parent or goblin corpse. Inheritance never creates knowledge the source did not possess, is lossier for advanced skills and may leave fragments below the threshold required to operate a facility.

This leakage lets the tribe retain traces of progress after a disaster without making carriers immortal through reproduction. The exact amount and whether several weak fragments can be recombined are deferred balance questions.

### Genetic diversity and biological advancement

**Decision:** Consuming different species expands the tribe's genetic diversity pool. Biological advancement points can be invested in an evolutionary tree, while particular species, individuals or captured tribes can unlock specific branches.

Genetic acquisition and knowledge acquisition are distinct systems:

- biological tissue can contribute diversity even when the victim has no useful learned skill;
- an intelligent victim can provide skill or conceptual knowledge depending on condition and preparation;
- one act of consumption may contribute to both, but the results are evaluated separately.

The initial tribe remains sexless. Later evolutionary choices may introduce sexes, castes, polymorphic roles or other reproductive specializations. No such branch is needed for the first playable.

Conquering another goblin tribe may merge genetic pools, carriers and traditions rather than producing only corpses and loot.

## Skills, capabilities and knowledge

### Distinct concepts

The simulation distinguishes:

- **aptitude** — biological or temperamental capacity;
- **skill** — practiced personal competence such as foraging or woodworking;
- **capability** — permission to attempt a culturally gated activity such as agriculture or smithing;
- **concept** — abstract knowledge such as language, notation or magical theory;
- **recipe or procedure** — a known way to produce a specific result;
- **plan** — a persistent player-authored spatial or organizational blueprint;
- **infrastructure** — physical tools and facilities required to apply knowledge.

A goblin may possess only some of these requirements. Knowing how to forge does not create a furnace, and owning a furnace does not create a smith.

### Acquisition by devouring

**Decision:** The initial transfer action is physical consumption. A living victim offers the highest potential fidelity; a fresh corpse offers less; decomposition progressively damages recoverable knowledge. Rotting remains can still serve as biological substrate even when meaningful learned knowledge is gone.

Consumption never transfers everything automatically. Results depend on:

- what the victim actually knew;
- freshness and neurological integrity;
- the consuming goblin's biological and conceptual capacity;
- preparation, tools and later specialist facilities;
- the complexity and familiarity of the target skill;
- how the victim is divided among consumers.

Several goblins may consume one victim and receive different fragments. Advanced preparation can target a desired skill more reliably, making the identification and pursuit of a particular expert a strategic activity.

### Conceptual gates

**Decision:** Practical skills are the easiest knowledge to absorb. Languages, abstract theories and formal magic require biological or cultural prerequisites. A primitive goblin may acquire useful farming motions without understanding the human calendar, property system or written records that supported them.

This prevents one meal from granting an entire technology tree and gives biological advancement, education and preserved culture distinct roles.

### Preservation methods

The game is intended to support several competing preservation strategies rather than one mandatory progression:

- repeated consumption and redundant carriers;
- inheritance through living buds or corpse blooms;
- shamanic demonstration and oral teaching;
- apprenticeship and organized education;
- ritual, diagrams and mnemonic objects;
- writing, archives and libraries;
- a possible late collective fungal memory.

**Decision:** Primitive teaching is possible but slow, unreliable and constrained by low intelligence. A learned or noble class may emerge to preserve and control knowledge, but it is one viable social response rather than a predetermined upgrade.

The environment can favor different strategies. A dry migratory tribe may struggle to maintain fungal archives; an isolated cave tribe may depend on apprenticeship; a settled literate tribe may build libraries.

### Knowledge loss

When all capable carriers are lost:

- physical buildings, tools, fields, goods and blueprints remain;
- incomplete work pauses and explains its missing skill, specialist, tool or material;
- partial familiarity may survive in descendants or former pupils;
- the tribe cannot perform gated work until a carrier is recovered or trained above the required threshold;
- existing products can be consumed or used if their use does not itself require the lost knowledge.

Loss should create salvage, imitation and recovery stories rather than delete player effort.

## Needs and autonomous behavior

### Individual needs

Every detailed person-like actor initially tracks:

- hunger;
- fatigue;
- health, including wounds, poison and sickness;
- immediate safety;
- a compact disposition influencing aggression, risk, obedience and social conflict.

Needs change priorities and competence instead of acting only as hard permission checks. A hungry goblin may keep working, then work badly, abandon the job, steal food or take a reckless foraging route.

Old age, disease, wounds, famine and poison provide predictable pressure on valuable carriers. Death should arise from legible simulation state rather than arbitrary carrier deletion.

### Community pressures

Settlement-level needs are calculated from physical state:

- food reserve and expected consumption;
- sleeping and shelter capacity;
- storage capacity and exposed stock;
- access to water and moist fungal ground;
- labor demand versus healthy available workers;
- known threats and confidence in local safety;
- later, continuity risk for critical knowledge.

These pressures drive alerts, priorities, reproductive permission and coarse AI decisions. The UI must be able to explain which actors, stocks and facilities created each warning.

### Player control

The player sets zones, jobs, priorities, policies and blueprints. Goblins select and execute permitted work according to need, skill, distance, risk and disposition.

Direct selection remains useful for inspection and exceptional orders, but routine survival must not require manually walking every goblin to every berry.

## World, ecology and resources

The first region is a swamp containing terrain, water, fertility, moisture, plants, animals, a goblin starting area and one human village.

Primitive resources include:

- berries, roots, meat and fungi;
- reeds, wood, bone and stone;
- shallow and deep water;
- basic storage bundles and primitive tools.

Resources exist in locations, inventories, containers and stockpiles rather than only as global counters. Gathering, transport, spoilage and access therefore matter.

### Logistics and stockpile doctrine

Storage is an active logistics network rather than a set of passive containers. A productive settlement should move goods in anticipation of demand instead of waiting until a worker reaches an empty workshop.

The mature system distinguishes:

- tribe-wide policy, such as preserving food or prioritizing construction;
- local demand from a workshop, blueprint, kitchen, infirmary or threatened district;
- stockpile rules controlling accepted resources, target quantities and reserve floors;
- delivery priority, which may override the global priority for one consumer or area;
- reservations preventing several haulers or consumers from claiming the same goods;
- staging buffers holding the next useful inputs near recurring work;
- route cost, danger, congestion, spoilage and carrier suitability.

A local priority is not merely a faster hauling job. It expresses that this destination should win competition for a resource, subject to explicit protected reserves and emergency policies. Demand should include current orders and a bounded forecast of near-future consumption, so the system can refill useful buffers without moving the same stack back and forth.

The stockpile interface must support reusable policies, copying, multi-editing, clear inclusions and exclusions, target and minimum quantities, upstream sources and downstream consumers. Every shortage or stalled delivery must expose an explanation: what is requested, what is reserved, where candidates exist, which rule rejected them and what currently outranks the request.

This full system is a later milestone. The survival foundation establishes physical ownership, reachability, capacity and deterministic reservations without committing the UI to the initial single-cell storage prototype.

A primitive field camp is the first concrete staging buffer. It is a physical 2×2 shelter built near reachable shallow water, provides a place to rest and requests a food reserve sized for at least a small expedition. Haulers may act on the remembered location of player-owned or previously discovered stock after it leaves current sight; the presentation must still distinguish remembered quantities from live observations. Camp demand competes with other storage demand through the same reservations rather than conjuring expedition supplies.

Area tools for gathering and later cutting, mining or clearing are selectors. Confirming an area resolves it into stable object targets; the rectangle itself is discarded. Empty cells are not jobs, newly appearing objects are not implicitly added, and target sets of different kinds may overlap. Persistent overlays use thin category-specific outlines around the selected bushes, trees, loose items, rocks or deposits rather than painting every tile in the original rectangle.

The initial ecology is intentionally small. It must generate renewable and depletable opportunities without pretending to be a complete ecosystem. More species are valuable when they introduce a new behavior, resource, hazard or biological unlock.

## Observation and fog of war

Each map cell is unknown, remembered or currently visible to the player's tribe.

Terrain and structures can persist as stale memories. Actors, loose items and exact resource quantities normally require current visibility. Dense vegetation, darkness, elevation and later camouflage affect detection.

Goblins can observe another society from concealment. Reports about notable individuals include uncertainty based on distance, time, observer skill and exposure. A report may identify a likely farmer, carpenter, guard or scholar without revealing an exact numeric character sheet.

The player can mark a reported target, investigate notable figures or ignore reconnaissance and attack indiscriminately. Better information takes time and risks discovery.

## External societies and consequences

The first human village produces food, wood and basic goods over seasons. Its inhabitants include workers, guards, travelers and potentially valuable skill carriers.

Its coarse dispatcher plans fields from population, reserve targets and predicted yield rather than granting fixed daily production. A temperate baseline provisionally has a 240-day year and two long crop cycles; later climate regions alter sowing windows, growth, water demand, frost risk and harvest reliability. Residents normally stay close to the settlement, with shortage-driven gathering as a bounded exception. Spotting a goblin is not itself permission for the tribe to raid: goblin aggression against the village requires an explicit player order, while either side may still flee or defend itself after comparing local strength.

An explicit raid order is an expedition plan, not an immediate destination override. It selects a reachable field camp nearest the target, requests its provisions, lets members eat, refill personal food and water, rest and assemble there, and only then changes the tribe to marching state. This makes a closer camp materially useful while keeping each consumed ration, refill and journey physical and inspectable.

The tribe can eventually scavenge, steal, trade, ambush, capture, raid or destroy. These actions have different costs:

- quiet theft preserves production but raises suspicion;
- targeted capture removes a worker or expert and can provoke a search;
- repeated predation changes routes, guards and local behavior;
- destroying the village yields immediate bodies and goods but eliminates future output;
- survivors, relatives or neighboring powers may create delayed retaliation.

The world should make restraint strategically interesting without imposing a moral score that declares one correct play style.

## Construction and blueprints

**Decision:** The player can design and reuse blueprints for workshops, rooms and other functional sets instead of placing every component repeatedly.

Constructed and natural features are spatial objects rather than special tile values. An object has a stable identity, anchor, orientation and a footprint made of parts at relative three-dimensional coordinates. Parts claim separate occupancy channels such as surface, solid volume, overhead and subsurface, allowing a roof above a floor, a bridge above water or a canopy above a path without erasing the underlying terrain.

A building remains one object even when its floor, walls, doors and roof cover many cells and height levels. Damage and construction progress apply to individual parts while ownership, function and blueprint provenance belong to the whole. Rivers and roads may additionally use network identities joining many spatial segments; they are not required to masquerade as one enormous rectangular building.

Blueprint sources include:

- built-in starter examples that demonstrate the interface;
- player-authored plans created in a dedicated editor;
- a clone of an existing construction edited into a reusable template;
- later, patterns copied or inferred from other cultures.

A plan can specify:

- required and optional elements;
- positions or requirement-based zones;
- accepted materials and preferred substitutions;
- item or facility types;
- access constraints;
- priorities and allowed variations.

Blueprints are a player aid and persist independently of current tribal understanding. Execution still requires materials, tools, infrastructure and capable workers.

**Working rule:** When a requirement is missing, construction pauses instead of silently producing nonsense. The alert lists missing items, materials, tools, access and specialist skills. Improvisation may later be an explicit blueprint option or goblin trait rather than hidden random failure.

## Progression

Early settlements use mud, reeds, scavenged wood and bone in swamps, caves or tree canopies. Later stolen capabilities can include:

- agriculture;
- advanced woodworking;
- organized mining;
- pottery and durable storage;
- smelting and metalworking;
- advanced construction;
- literacy and administration;
- formal magic.

Progression is not a universal linear technology tree. Available prey, local materials, biological prerequisites, preservation choices and surviving carriers produce different civilizations.

Biological advancement and cultural knowledge are parallel axes. A tribe can become biologically specialized without becoming learned, or preserve a sophisticated culture in physically primitive bodies.

## Core loop

1. Sustain a primitive settlement and produce replaceable bodies.
2. Discover a need the tribe cannot currently satisfy.
3. Explore and identify a creature or culture possessing useful capability.
4. Decide whether to observe, trade, scavenge, steal, capture or raid.
5. Devour one or more suitable sources and acquire incomplete knowledge.
6. Exploit the temporary capability to build goods, facilities and social structures.
7. Spread, preserve or monopolize the knowledge before every carrier dies.
8. Live with the political, ecological and military consequences.
9. Use the tribe's new capability to reach more dangerous sources of knowledge.

Capturing the right mind can be more valuable than winning a battle. Consuming every available source can be an effective short-term strategy that leaves the tribe isolated and starving later.

## Conflict and rules direction

**Decision:** The game uses a small original simulation ruleset rather than importing the legacy d20 implementation.

The rules should remain familiar at the level of actor, skill, difficulty, modifiers and outcome, but population-scale work must be more stable and cheaper than heroic d20 combat.

Combat initially models:

- intent and target selection;
- position, reach and obstruction;
- attack competence and defense;
- weapon or natural attack properties;
- wounds, pain, poison and incapacitation;
- morale, fear and retreat;
- capture and restraint;
- bounded random variation with deterministic replay.

Adventuring parties and magic may later use richer character rules, but they must share the same world state, effects and time model rather than run as a disconnected minigame.

## Presentation

The default view is readable top-down 2D with discrete simulation levels.

### Visual style

**Decision:** The game does not use pixel art and does not render the settlement as a fully three-dimensional scene. Its target is modern, lightly cartoon-like 2D: clean shapes, restrained detail, expressive silhouettes and enough material variation to keep a dense settlement readable.

The style should support:

- recognizable goblin roles and carried goods at ordinary zoom;
- clear terrain, water, vegetation, paths and construction state;
- selective outlines and shadows instead of noisy borders around everything;
- broad color and shape coding that remains legible during fast simulation;
- layered or cutout animation with relatively few authored frames;
- sprite atlases, shared materials and reusable parts rather than unique expensive effects for every actor;
- lighting, weather and height cues that can be applied in batches.

The world may contain a great deal of information, but not every object receives equal visual emphasis. Current jobs, threats, selected carriers, blocked construction and unusual resources need stronger signals than decorative clutter.

At distant zoom, individual animation may simplify into icons, colored motion or aggregate activity. At close zoom, the same simulation state can drive more expressive poses and equipment. Visual level of detail never changes the authoritative simulation result.

### Interface readability

The interface is expected to be substantial because the player needs to understand:

- shortages and work queues;
- personal needs and causes of death;
- who carries which critical knowledge;
- stale versus current reconnaissance;
- why a blueprint is blocked;
- what a remote society is believed to produce;
- which consequences a raid has created.

Prototype art is welcome, but provenance must be clear. Assets recovered from old projects are not imported unless their license and authorship can be established.

## Technical direction

**Decision:** Godot 4 .NET with C# is the leading engine foundation. Godot owns rendering, input, interface, audio and asset integration; the simulation remains a plain C# domain independent of the scene tree.

### Target platform

**Decision:** Goblin Stronghold is a desktop PC game. Windows x64 is the primary development platform; Windows x64 and Linux x64 are intended release targets.

Linux ARM64, particularly a contemporary Raspberry Pi, is an experimental compatibility target. The architecture should remain portable and the headless simulation should be easy to benchmark there, but Raspberry Pi performance is not a release gate and receives no promised population size or speed multiplier until measured on real hardware.

The project does not target phones, tablets, web browsers or consoles. The interface can therefore assume mouse and keyboard, resizable desktop windows, hover interaction, dense information panels and ordinary local file access. Touch-first layout, mobile export constraints and console certification do not shape the simulation architecture.

The foundation uses:

- deterministic fixed ticks;
- stable identifiers instead of scene-node references;
- commands entering at tick boundaries;
- immutable definitions and compact mutable runtime state;
- reproducible, separated random streams;
- read-only or double-buffered presentation snapshots;
- no node per tile and no requirement for a node per simulated goblin;
- save data based on simulation identifiers and state rather than Godot objects.

### Real-time clock and speed controls

**Decision:** The game runs in real time with pause and several faster simulation modes. It is not turn-based even though its deterministic core advances through discrete fixed ticks.

**Working rule:** Initial controls expose pause, normal speed and successive multipliers such as 2x, 4x and 8x, plus an optional maximum-throughput mode. Exact multipliers and the normal-speed tick duration are tuned after representative scenarios can be profiled.

Changing speed alters how quickly ticks are requested, not their rules or duration in simulated time. Given the same seed and commands issued at the same simulation ticks, normal speed, accelerated speed and headless execution must produce the same result.

At higher speeds:

- several simulation ticks may run between rendered frames;
- presentation may discard intermediate snapshots but may not discard authoritative events;
- movement can interpolate at normal speed and simplify at high speed;
- alerts, floating text and audio are aggregated instead of replaying an unreadable burst;
- expensive panels update at a lower visual frequency while their source state remains current at snapshot boundaries;
- the UI indicates when the machine cannot sustain the requested multiplier.

The simulation never derives time from render-frame delta. Pausing freezes simulation ticks while leaving inspection, camera movement and interface interaction available.

### Rendering contract

Rendering consumes read-only snapshots and compact event streams. It does not own actors, inventories, jobs or map truth.

The renderer should be able to:

- draw terrain in chunks and rebuild only dirty visual regions;
- apply ordered world-change deltas between occasional complete chunk snapshots;
- cull undisplayed chunks and height levels;
- batch actors, plants, items and repeated effects by shared visual data;
- update fog of war from a compact visibility representation;
- pool temporary presentation objects;
- skip animation states safely when snapshots arrive faster than they can be displayed;
- use zoom-dependent detail without creating or deleting simulation entities.

The interface also follows this boundary. Large population and inventory views use summaries, filtering and virtualization instead of constructing one persistent control for every simulated object.

### Parallelism

**Decision:** Correct single-threaded execution is required first, but ownership and data flow are designed so coarse work can later run in parallel.

Candidate workloads include:

- path queries;
- visibility queries;
- environment propagation;
- independent remote society updates;
- planning batches;
- region-level ecology.

Systems exchange results at explicit phase barriers. Each mutable data set has one owner during a phase. Determinism and debugging must not depend on thread scheduling.

### Simulation detail and chunk materialization

**Decision:** Every society, military group, creature and construction inside a loaded active chunk is simulated as material world state. Community dispatchers may group planning, reservations and work assignment, but they do not replace local people with population counters. A local villager still has a position, needs, equipment, inventory, current task and physical access to the field, well, workshop or stockpile being used.

Buildings in active chunks always have spatial objects, footprints, construction state and physical contents. A dispatcher cannot create virtual storage capacity, food or production while goblins can walk into the same location. This applies equally to human, goblin and later external settlements.

Distant unloaded chunks may use cohorts, aggregate inventories, scheduled projects and lower-frequency ecology. Each remote society or traveling group owns a deterministic random stream and a coarse dispatcher. Crossing the materialization boundary converts those records into concrete actors, items, jobs and structures without rerolling outcomes. Leaving an active chunk folds eligible detail back into cohorts only after reconciling casualties, needs, inventories, project progress and terrain changes.

The eventual active area is substantially larger than the current presentation probe. Most local activity is expected to cluster around societies and a limited number of surface-adjacent Z levels, so the primary load is hundreds of detailed actors organized under a few dispatchers rather than hundreds of thousands of unrelated agents.

### Navigation

The first implementation uses correct deterministic local pathfinding behind a path-service boundary. Later versions may add region or room graphs, topology versions, cache invalidation, request budgets and shared guidance for groups moving toward one destination.

Digging, construction and changing water can invalidate connectivity. Advanced pathfinding is introduced only after profiling shows which workload dominates.

## Roadmap relationship

The implementation sequence is:

1. deterministic simulation shell;
2. generated living swamp, tribe survival, human village and fog of war;
3. observation, combat, capture and stolen agriculture or woodworking;
4. budding inheritance and primitive teaching;
5. deeper construction, preservation strategies and external societies.

The knowledge loop remains the game's central validation target. The survival foundation comes first only because knowledge needs valuable carriers, functioning prey and persistent consequences to matter.

## Resolved design tensions

### Is the game 2D or 3D?

The world has three-dimensional topology expressed as discrete levels; presentation is top-down 2D. Full 3D art is not required.

### Is the game real-time or tick-based?

It is real-time from the player's perspective and fixed-tick inside the simulation. Speed controls change tick throughput, while rendering interpolates or skips intermediate presentation states.

### Are all people simulated individually?

All locally materialized people have individual needs and state. Distant societies use coarse cohorts and expand individuals only when needed.

### Is a blueprint knowledge?

A blueprint is persistent player-authored intent. It can survive loss of tribal knowledge, but goblins cannot execute steps for which no capable carrier exists.

### Does eating provide genes or skills?

It can provide both, through separate systems. Tissue diversity drives biological options; recoverable mental structure drives learned knowledge.

### Can goblins learn without eating?

They can improve known skills by practice and imperfectly transmit them through budding or teaching. They cannot freely invent locked cultural capabilities.

### Does a dead carrier erase progress?

No. Physical results and plans remain; partial familiarity may remain; active capability can still be lost.

## Deferred questions and design risks

These questions are real but do not block the survival foundation:

1. **Inheritance strength:** If budding preserves too much, safe reproduction bypasses the carrier-risk loop. If it preserves too little, it feels irrelevant. The first knowledge prototype must measure this directly.
2. **Corpse substrate:** It remains to decide which species can host goblin growth and which biological traits or diseases can cross that boundary.
3. **Fragment recombination:** Several descendants may hold partial familiarity. Whether they can collectively reconstruct a capability requires a clear, non-exploitable rule.
4. **Personality transfer:** Technical knowledge, temperament, morality and loyalty may have different fidelity. The initial transfer system should store their provenance even if only skills affect play.
5. **Coarse-detail reconciliation:** Remote casualties, inventories and scheduled travel must not be rerolled when actors become visible.
6. **Water and verticality:** Dynamic water across height levels could dominate pathfinding and performance. The first swamp uses limited water behavior until the core simulation is measurable.
7. **Agriculture versus woodworking:** Both are desirable first targets. The conflict milestone should let the player choose between two valuable experts, but only one complete production chain is required for its minimum acceptance test.
8. **Knowledge visibility:** Reports must be useful without exposing exact hidden statistics. Uncertainty, staleness and misidentification require UI experiments.
9. **Social class:** A learned elite can emerge from carrier scarcity, but hard-coded castes are deferred until ordinary status and teaching create observable pressure.
10. **Evolutionary sex and castes:** These remain branches in the biological tree, not assumptions embedded in initial actor data.
11. **Performance target:** A promised population size and sustainable speed multiplier require a representative map, ecology and job workload. Tick, snapshot, visibility and pathfinding costs are measured from the foundation, then converted into an explicit performance budget before production optimization.

## Explicit non-goals for the foundation

- Reimplementing Dwarf Fortress or Gnomoria feature for feature.
- Porting the legacy d20 framework.
- Importing decompiled, provenance-uncertain or unverified archived assets.
- Simulating every distant person at local-map resolution.
- Implementing every historical idea in the first prototype.
- Making multithreading a prerequisite for correct gameplay.
- Supporting mobile, browser or console platforms.
- Building final art, final UI, metallurgy, magic or a complete blueprint editor before validating survival and knowledge transfer.
