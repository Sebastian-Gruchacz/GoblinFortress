# Goblin Stronghold — first playable roadmap

## Purpose

The first playable should establish a small, deterministic world that can survive and change without combat. It is the foundation for the distinctive knowledge-acquisition loop, not a separate colony game to be completed before that loop begins.

The implementation order is:

1. generated world, ecology, goblin survival, basic budding and a neighboring human village;
2. observation, conflict and combat;
3. devouring, knowledge transfer and loss;
4. imperfect preservation through budding and teaching.

## Rules model

The simulation will use a small original ruleset rather than porting d20.

All uncertain actions use the same shape:

- an actor has a relevant skill;
- the action has a difficulty derived from the target and circumstances;
- tools, health, weather, terrain and assistance provide modifiers;
- a seeded random sample adds bounded variation;
- the margin determines failure, partial success, success or exceptional success.

The exact numeric scale remains an implementation detail until a prototype can be measured. The important constraints are:

- ordinary competent work should be predictable;
- randomness should create variation, not routinely overturn overwhelming advantages;
- repeated population-scale work can be resolved in batches;
- every result must be reproducible from the world seed and command history;
- combat, gathering, crafting, teaching and knowledge extraction should use compatible concepts without sharing one enormous character sheet.

Routine work does not roll every tick. Predictable actions accumulate deterministic progress, and randomness is sampled only when a meaningful uncertain event is created or resolved. Population batches may use one reproducible distribution instead of hundreds of decorative rolls when individual outcomes are not observable.

## Milestone 0 — deterministic simulation shell

Build a pure C# simulation with no dependency on Godot.

It must provide:

- fixed simulation ticks and an in-game calendar;
- seeded and stream-separated random number generation;
- stable identifiers for cells, actors, groups, items and jobs;
- commands accepted only at tick boundaries;
- authored definitions separated from mutable runtime state;
- save and load sufficient to reproduce the next simulation result;
- compact events suitable for tests, logs and presentation;
- a read-only presentation snapshot;
- explicit simulation speed requests independent from render-frame timing;
- lightweight counters for tick cost, jobs, path requests, active actors and snapshot size;
- deterministic tests that run the same scenario twice and compare the outcomes.

The world should be partitionable into regions, but the initial implementation remains single-threaded. Parallel execution may later process independent regions or read-only queries behind explicit phase barriers.

Random streams must not depend on worker scheduling, incidental collection order, camera distance or simulation detail. A draw is associated with a stable subsystem, actor, tribe, region, logical interval or action identity and a named sample purpose. Stable identities separate samples; they do not select mutable per-entity RNG cursors. Simulation time, item quantities and discrete state use integer or explicitly quantized values where exact replay matters.

### Milestone 0 delivery slices

1. World clock, identifiers, commands and deterministic random streams.
2. Minimal world state plus immutable authored definitions.
3. Tick pipeline, events and state hashing.
4. Save, load and replay of a headless scenario.
5. Read-only snapshots, event delivery and speed-independent replay tests.
6. Lightweight performance counters suitable for headless runs and a future Godot client.

## Milestone 1 — living swamp

### Generated map

Generate a finite swamp region from a visible seed. The first version may render one surface level, but coordinates and data structures must reserve a discrete height component for later canopy, cave and excavation layers.

The generated map contains:

- solid ground, mud, shallow water and deep water;
- fertility, moisture and traversal cost;
- clearings, dense vegetation and natural obstacles;
- a goblin spawning area;
- one human village with usable access to water and local resources;
- enough connected traversable terrain for both communities to function;
- undiscovered territory governed by fog of war.

The initial settlements provide a deliberately small structural test set: two human cottages, one human barn and one well, plus two or three goblin huts. Every structure is one stable object with a multi-cell footprint. Cottage, barn and hut floors and walls occupy the surface layer, roofs occupy the level above it, and the well includes subsurface shaft parts. Settlement access cells remain reserved during atomic placement.

Generation must validate its own result. A seed is rejected or repaired when either settlement is trapped, essential resources cannot be reached or the two communities begin on top of one another.

Generation produces only the versioned baseline and initial ecological state. Each spatial sample is keyed by generator version, region or cell coordinates, feature domain and pass, so generating chunks or depths in a different order cannot change their contents. Runtime world state is mutable and layered: ground and water, vegetation and habitat, construction and damage, plus derived traversal and visibility data. Harvesting a plant, felling a tree or completing a structure changes authoritative world state and emits a compact dirty-region record.

The first milestone does not require full mining, structural collapse, spreading fire or dynamic multi-level water. Its data ownership must nevertheless allow later excavation, fill, burned and demolished structures, constructed height transitions and underground layers without replacing cell identity or invalidating existing saves.

Navigation, visibility and rendering track separate change versions. A visual-only change need not discard paths, while a felled tree, new wall or excavated passage invalidates the affected topology. Derived caches can be rebuilt and are not authoritative save data.

### Fog of war

Knowledge of each map cell has three states per player-controlled tribe:

- **unknown** — never observed;
- **remembered** — observed previously, showing possibly stale terrain and structures;
- **visible** — currently observed by at least one goblin or other tribal sensor.

Actors, loose items and resource quantities are shown only while visible. Remembered buildings and terrain retain the last observed state and may become outdated. Visibility uses line of sight, elevation and concealment, with darkness and dense vegetation added when those systems exist.

The human village is not identified merely because generation placed it. Goblins must find it, observe it or encounter its inhabitants.

### Plants and animals

The first ecology should be deliberately small:

- berries as renewable seasonal food;
- roots as slower but less exposed food;
- reeds as primitive construction and crafting material;
- trees as wood, cover and habitat;
- one harmless prey animal;
- one animal able to injure an isolated goblin.

Plants grow from local moisture, fertility and season. Animals need only forage, rest, flee, reproduce at an aggregate rate and die. Detailed genetics, food webs and individual animal skills are deferred.

The first implemented vegetation probe uses deterministic berry patches with local capacity. Gathering depletes the patch under the actor, exhausted patches reject further gathering, and bounded regrowth occurs at stable logical intervals. This validates mutable overlays, world versions, dirty-cell delivery and save/load before seasons or additional species are introduced.

### Resources and goods

Start with a short material chain:

- edible biomass: berries, roots, meat and fungi;
- raw materials: reeds, wood, bone and stone;
- basic goods: gathered food portions, building bundles and one primitive tool class;
- stocks owned by an actor, carried in a container or stored in a settlement zone.

Resources are physical quantities rather than abstract global counters. The settlement may expose totals and forecasts to the player, but jobs still require reachable items and transport.

Spoilage, quality and material substitutions belong in the data model but need not all affect the first simulation.

### Goblin tribe

Begin with a small sexless fungal tribe large enough to divide labor but small enough that every loss is legible. Goblins choose from player-authorized jobs rather than receiving direct movement orders.

Initial jobs are:

- explore;
- gather food;
- harvest reeds or wood;
- haul;
- build primitive shelter or storage;
- rest, eat and seek safety;
- tend a weakened goblin or a basic living bud.

The first skill set is:

- foraging;
- primitive woodcraft;
- survival;
- hauling or general labor;
- observation;
- primitive medicine.

Skills affect time, yield, waste, safety and product quality. Practice can improve a skill slowly, but ordinary practice alone cannot unlock unavailable cultural capabilities such as agriculture or advanced woodworking. Primitive woodcraft covers breaking, trimming, bundling and crude construction; advanced woodworking covers fitted components, joinery, reliable tools and human-style facilities.

The survival milestone includes only voluntary budding by a living goblin. Budding consumes food, requires suitable moist space and weakens the parent. The descendant begins with innate goblin aptitudes but no inherited learned skill. Skill leakage, corpse blooms and carcass seeding are added only after the knowledge model exists.

### Individual needs

Every detailed person-like actor initially tracks only needs that produce useful decisions:

- hunger;
- fatigue;
- health, including wounds and sickness;
- safety or immediate threat;
- a simple disposition influencing risk, aggression and obedience.

Needs create priorities and penalties rather than binary permission checks. A hungry goblin can continue hauling, but eventually abandons work, performs worse or takes dangerous food-seeking actions.

Moisture is initially an environmental requirement of fungal beds and budding rather than another constantly draining personal meter.

### Community needs

Community needs are derived pressures, not an invisible second creature with its own hunger bar:

- food reserve and expected consumption;
- safe sleeping capacity;
- storage capacity and exposed stock;
- access to suitable moist fungal ground;
- labor demand versus available healthy workers;
- known threats and confidence in local safety.

These values drive alerts, work priorities, budding permission and coarse decisions made by non-player settlements. They are calculated from physical state so the UI can explain every shortage.

### Human village

The human village initially runs as a coarse settlement simulation even though its persistent structures occupy the generated map:

- population by household or work cohort;
- food stock, wood stock and a small number of goods;
- seasonal gathering or farming output;
- consumption and storage;
- a few visible structures;
- travelers or workers materialized as individuals when observation, proximity or interaction makes their behavior relevant;
- reaction to discovery, theft and later violence.

It must produce value over time. Destroying it in a later milestone therefore removes future food, trade goods and potential knowledge while increasing the chance of retaliation.

The coarse state is authoritative. Detailed actors are a temporary expansion of part of that state, not a second simulation that can silently diverge from it.

## Milestone 1 completion criteria

The foundation is ready for combat when:

- a chosen seed always produces the same valid map;
- the same commands produce identical state hashes and event streams;
- goblins can explore under fog of war and discover the village;
- plants renew, animals move and both communities consume and produce resources;
- goblins gather, haul, eat, rest and construct basic shelter without direct micromanagement;
- goblins can produce and tend a basic living bud when the tribe has food, moist space and available care;
- individual shortages roll up into explainable tribal pressures;
- the human village survives and changes using its coarse simulation;
- save and load preserve the next deterministic outcome;
- a headless run can simulate at least one in-game season and report population, stocks, deaths and discovered territory;
- the same scheduled commands reach the same state at normal, accelerated and unthrottled execution speeds;
- dropping intermediate presentation snapshots does not lose simulation events or alter the result.

No requirement in this milestone depends on final graphics. After the headless scenarios are stable, a Godot presentation probe should display the map, fog, actors and stocks using the intended modern cartoon-like 2D direction rather than pixel-art placeholders that would force incompatible visual assumptions.

### Milestone 1 delivery slices

1. **Terrain:** seeded swamp generation, validation and traversal queries.
2. **World mutations:** layered cell state, plant depletion and renewal, construction occupancy, change versions and dirty regions.
3. **Stocks:** physical resources, inventories, hauling and storage zones.
4. **Survival:** goblin needs, autonomous jobs, shelter and basic budding.
5. **Ecology:** plant renewal, simple animals, habitats, seasons and deaths.
6. **Visibility:** exploration, line of sight and unknown/remembered/visible state.
7. **Neighbor:** human village stocks, cohorts, production and materialization records.
8. **Season run:** save/load, reports and a deterministic headless season.
9. **Presentation probe:** Godot camera, chunked world view, fog, actor inspection and pause/normal/accelerated controls.

Each slice must add a playable or inspectable behavior and its deterministic tests. Later slices may extend earlier data, but they should not require replacing its ownership model.

## Milestone 2 — conflict and stolen knowledge

The second milestone adds:

- concealment and observation of human individuals;
- reports about notable skills with uncertainty depending on the observer;
- wounds, poison, sickness and simple close combat;
- capture, killing and devouring;
- agriculture and advanced woodworking as the first unavailable human capabilities, allowing the player to value different experts;
- partial skill extraction depending on freshness, preparation and difficulty;
- several goblins sharing one victim's extractable knowledge;
- production and specialist facility operation requiring a living capable carrier, while finished goods remain usable unless their use is itself specialized;
- consequences for theft, raids and destruction of the village.

This is where Goblin Stronghold's central loop becomes playable. The first milestone exists to make the prey, risks and consequences real.

Only one of agriculture or advanced woodworking needs a complete production chain for the minimum playable test. Both must exist as authored capabilities so reconnaissance and target choice can demonstrate that different victims offer different futures.

### Milestone 2 completion criteria

The knowledge loop is validated when:

- a goblin can observe the village and produce a fallible report about a notable expert;
- the player can target, capture or kill that expert without being required to destroy the whole village;
- victim condition and preparation visibly affect what can be extracted;
- one victim can produce different partial results in several consumers without duplicating knowledge;
- at least one stolen capability enables a previously impossible production chain;
- losing every capable carrier pauses new specialist work but preserves structures, plans and finished goods;
- village stocks, population and hostility reflect theft, casualties or destruction;
- the complete scenario replays deterministically from its seed and command history.

## Milestone 3 — fungal inheritance and preservation

The third milestone extends basic living budding with:

- inheritance from deliberate budding that already weakens a living parent;
- spontaneous propagation from a dead goblin through a corpse bloom;
- deliberate use of suitable remains as substrate for carcass seeding;
- partial leakage of skills or experience to descendants;
- difficult shamanic teaching;
- loss, distortion or personality influence during transfer;
- biological advancement driven by genetic diversity acquired from consumed species;
- later evolutionary branches that may introduce differentiated biological roles.

Writing, diagrams, libraries, formal education and collective fungal memory remain possible later preservation strategies rather than mandatory steps on one linear technology tree.

### Milestone 3 completion criteria

Primitive preservation is validated when:

- a carrier can attempt at least two preservation strategies with different costs and risks;
- descendants never inherit knowledge absent from their source;
- inherited familiarity is lossy and cannot silently exceed the source's competence;
- death can leave a recoverable trace without guaranteeing recovery;
- teaching consumes time from both teacher and pupil and can fail partially;
- the UI identifies the source and confidence of every inherited or taught fragment;
- losing the final full carrier remains consequential even when fragments survive;
- deterministic scenarios cover successful preservation, degraded inheritance and complete cultural loss.

## Milestone 4 — deliberate logistics

The fourth milestone turns basic hauling and storage zones into a player-directed supply network. It adds:

- reusable stockpile policies with resource filters, capacity, target quantities and protected reserve floors;
- tribe-wide priorities combined with local destination and resource priorities;
- explicit demand generated by workshops, blueprints, consumption sites and emergencies;
- deterministic item and capacity reservations, including expiry and recovery from interrupted jobs;
- configurable input buffers that request bounded future supply before production stops;
- preferred sources, allowed destinations and optional hauling links without requiring every route to be wired manually;
- delivery scoring based on urgency, distance, danger, congestion, spoilage and carrier capacity;
- batch editing, policy copying and concise presets for common storage roles;
- an explanation view for shortages, rejected candidates, reservations and competing requests;
- throughput and wait-time diagnostics capable of distinguishing lack of goods, lack of carriers and a bad policy.

Local priority determines which destination wins competition for goods; global priority expresses the tribe's broader intent. Protected reserves and emergency overrides remain explicit constraints, preventing an urgent but low-value workshop from silently consuming the tribe's last food or medicine.

The system forecasts only bounded, explainable demand. It must avoid infinite prefetching, stock oscillation, circular hauling links and starvation of low-priority consumers. Advanced automation should reduce micromanagement without hiding the reason for any decision.

### Milestone 4 completion criteria

Deliberate logistics is validated when:

- a recurring workshop receives its next inputs before the current production cycle ends when supply and labor permit;
- two local consumers competing for one resource are served according to visible local and global policy;
- protected reserves survive ordinary high-priority demand and are consumed only by an allowed override;
- no physical item or destination capacity can be double-reserved;
- interrupted, dead or reassigned haulers release or transfer reservations deterministically;
- changing one reusable policy updates all linked zones without recreating their configuration;
- batch editing and copying can configure several zones without visiting every tile separately;
- the UI explains a delayed delivery without requiring inspection of raw simulation data;
- cyclic links, unreachable stocks and continuously changing demand do not create hauling oscillation;
- deterministic stress scenarios report delivery latency, hauling distance, idle time and unmet demand.

## Deferred deliberately

- multiple active height layers;
- final blueprint editor;
- metallurgy and magic;
- large cities and several detailed neighboring societies;
- full diplomacy and trading;
- individual simulation of every distant person;
- production multithreading;
- detailed animal genetics and ecosystems;
- final art, audio and complete interface.
