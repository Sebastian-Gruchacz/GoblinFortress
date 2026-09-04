# Civilizations and world generation roadmap

## Purpose

The game needs more inhabitants than the player tribe and the demo village, but
it must not turn every visitor, settlement, and species into another branch in
`SimulationEngine`. This roadmap separates reusable content definitions from
runtime political state and from the generated location.

The Early Access target remains a partly isolated frontier. Kingdoms and clans
exist beyond the playable map and project small groups into it; full cities and
large strategic maps are deliberately outside the initial scope.

## Domain vocabulary

These concepts are related but not interchangeable:

- **civilization type** is immutable content, for example player goblins,
  humans, cave dwarves, surface dwarves, or elves;
- **polity** is one runtime kingdom, clan, tribe, band, or other political
  organization using a civilization type;
- **settlement** is a village, fortress, camp, or outpost owned by a polity;
- **party** is a patrol, caravan, expedition, settler train, adventuring group,
  or bandit group temporarily acting on the local map;
- **relation** belongs between polities and may be affected by encounters,
  casualties, theft, trade, trespassing, and agreements;
- **legendary creature** is not a civilization. It is a rare actor with a
  dedicated lifecycle and may have followers, equipment, lairs, and diplomatic
  consequences.

This distinction allows several human kingdoms to share the same human content
without sharing hostility, memory, stock, or expedition state.

## Current compatibility mapping

The first core catalog retains the existing save and command contracts:

| Civilization ID | Current adapter | Runtime representation |
| --- | --- | --- |
| `core:goblin-tribe` | player goblins | actors and tribe-owned structures |
| `core:human-village` | demo human village | `HumanVillageState` |
| `core:cave-dwarf-clan` | `DarkDwarves` | `UndergroundFactionDirector` |

`CivilizationLegacyRole`, `UndergroundFactionKind`, and existing owner enums are
compatibility adapters. New moddable concepts should use stable content IDs.

The initial extraction moves cave-dwarf generation and strategic behavior into
`content/civilizations.json`: depth bands, occurrence, population, fighters,
stocks, fortification, resource targets, upkeep, relations, and conflict timing.
The director still owns deterministic runtime state and save compatibility.

## Civilization definition target

The catalog should grow in independently testable profiles instead of one very
wide record. Planned profiles include:

- species and culture IDs;
- controller/behavior strategy ID;
- name-generator ID, syllable sets, affixes, titles, and uniqueness policy;
- lifespan, maturity, aging, health, needs, recovery, and reproduction;
- attribute ranges, skill affinities, learning rates, and role preferences;
- equipment/loadout tables selected by role, wealth, escalation, and biome;
- social groups, settlement model, architecture, and preferred materials;
- diplomacy defaults, remembered offences, trade willingness, and retreat rules;
- party templates for patrols, caravans, expeditions, settlers, wanderers, and
  adventurers;
- visual renderer, atlas, palettes, and optional color-key mappings;
- localization keys for civilization, polity, role, and party descriptions.

English remains the embedded fallback for every player-facing key. Definitions
contain stable IDs, never localized prose.

Profiles should be referenced by ID and validated when catalogs are composed.
A mod may override a core definition or add a new one under its own namespace.
The active definitions and their package versions/hashes must eventually be
pinned in save metadata before externally defined civilizations can be saved.

## Runtime subsystem target

```text
Civilizations/
  CivilizationDefinition       immutable catalog data
  CivilizationCatalog          core plus ordered package overrides
  Polities/                     runtime kingdom, clan, tribe, band state
  Relations/                    diplomacy, memory, hostility and trade access
  Settlements/                  ownership and settlement lifecycle contracts
  Parties/                      templates, arrivals, objectives and departure
  Naming/                       deterministic pluggable name generators
  Directors/                    world-facing escalation and arrival scheduling
```

`SimulationEngine` remains the clock and composition boundary. It advances
directors and routes resulting commands/events, but party selection, escalation,
settlement founding, and diplomacy live in focused services.

## Location generation profile

`SwampMapGenerator` should become a compatibility facade over a location
generation pipeline. A generation request will select a stable profile ID plus
seed and dimensions. The profile should describe:

- climate zone and seasonal calendar profile;
- broad character such as marsh, forest frontier, rocky upland, or mixed valley;
- elevation range, ruggedness, cave density, and geology weights;
- hydrology: no river, one channel, branches, width, shallows, and crossings;
- routes: absent road, through-road, junction, endpoints, width, and bridge
  policy;
- ecology tags and abundance for trees, berries, mushrooms, roots, fish, and
  animals;
- remoteness and permitted initial settlement pressure;
- points of interest such as ruins, abandoned camps, old mines, and shrines;
- safe spawn constraints, map-edge entry regions, and minimum travel corridors.

The pipeline should have deterministic stages with explicit intermediate data:

```text
height/geology -> water -> roads/crossings -> caves -> ecology
               -> points of interest -> initial settlements -> validation
```

Later stages may reserve or modify earlier geometry, but must not silently reroll
the seed. Validation should report which constraint failed.

The current demo swamp becomes the first embedded location profile. Existing
`Generate(seed, width, height)` and save generator-version contracts remain as
adapters until profile IDs and versions are persisted.

### Multi-level cave macro-features

The level is a materialization and simulation boundary, not necessarily a
generation boundary. A deterministic cave-planning stage may reserve one
connected feature across several depths before any individual slice is written.
Examples include sloped complexes, tall chambers, lava chasms, and caverns that
later host underground settlements.

- [x] Introduce a pure macro-feature plan and materialization registry under
  `Map/Generation`. Plans use stable content definition IDs, reserve cells and
  vertical passages across contiguous levels, reject overlapping reservations,
  and remain pending until every slice has been materialized.
- [x] Add a deterministic core planner for the first code-native sloped cavern
  and lava-gallery layouts. It chooses reserved footprints, depth spans,
  natural ramps, and fluids before their levels are materialized.
- [ ] Move macro-feature definitions and placement constraints into the
  deep-geology profile, including settlement-safe reservations and weights.
- [x] Activate macro-features in generator v15 while retaining v14 geometry and
  save loading. Migration or regeneration remains explicit.
- [x] Let an ordinary hidden feature materialize one approached slice at a
  time, but materialize every outstanding slice of a feature marked
  `CompleteOnExposure`. The first lava gallery exercises this path and adds its
  passages only after both endpoint levels exist.
- [ ] Add unsupported open-volume geometry for true shafts, tall chambers, and
  chasms without a floor on every intermediate level. Do not represent these as
  ordinary cave-floor cells.
- [ ] Run ore veins, crystals, loose stone, fauna, and other per-level
  decorators after the reserved macro geometry. Decorators may fill eligible
  solid cells but must neither close passages nor overwrite fluids, structural
  footprints, or other reservations.
- [ ] Validate reachable ramps, supported floors, safe reveal boundaries,
  continuous vertical visibility, and light propagation across the completed
  feature. A failed validation reports the feature ID and seed domain instead
  of silently rerolling the map.

The deterministic plan is derived map data. Only mutable consequences need save
state, while generator version, profile ID, and fingerprint continue to pin the
original plan. If planning becomes too expensive to reproduce during load, its
compact reservations may be serialized without exposing renderer state to the
domain generator.

## Early Access delivery slices

### 0. Foundations

- [x] Add a core civilization catalog with stable IDs for the three current
  civilization roles.
- [x] Move cave-dwarf generation and strategic behavior parameters into the
  embedded core pack.
- [ ] Move goblin and human vital, need, age, name, skill, equipment, and
  behavior profiles out of `SimulationDefinitions` and `HumanVillageState`.
  Schema-8 civilization data now owns goblin and human maximum health, the
  goblin aging curve, goblin hunger, thirst, and fatigue, plus the human
  population's daily needs and fatigue profile. It also owns baseline melee
  damage and variance for both civilizations. The goblin actor-generation
  profile owns deterministic skill and trait pools, roll keys, starting
  equipment, and work-preference ranges. Equipment bonuses, crafted loadout
  progression, and human role loadouts remain separate. Day, night, and
  structure-assisted perception
  ranges are civilization data too, including human intruder detection. Spatial
  behavior now controls movement timing, the human village activity radius, and
  the goblin autonomous-explorer limit. Runtime and UI consume civilization
  values directly. Matching
  `SimulationDefinitions` values and the current skill, trait, and equipment
  enums remain compatibility adapters while stable moddable catalogs for those
  concepts and broader behavior await later vertical slices.
- [x] Introduce deterministic `INameGenerator` strategies; retain numbered
  names only as a save-compatible placeholder. The core registry now resolves
  stable generator IDs from civilization definitions. Goblin syllable sampling
  retains its existing random domain and keys, while the demo human sequence
  preserves its current names. Schema-2 `content/name-generators.json` owns the
  syllable and ordered-name tables, supports ordered package overrides, and is
  validated before a civilization can reference a generator.
- [x] Persist actor sex and let name generators provide female and male lists or
  syllable affixes with a neutral fallback. Humans receive deterministic sex at
  creation while starting and migrated goblins remain sexless.
- [ ] Add a tribe genetic-development choice that can make later goblins
  female or male. The actor field is mutable for that transition, but the
  unlock, reproduction consequences, presentation, and naming policy for
  already named goblins remain a separate gameplay slice.
- [x] Add stable polity IDs to snapshots, state hashes, and format-75 saves
  without changing existing ownership enums. The player tribe and human village
  have fixed core IDs; every generated cave-dwarf clan has its own ID derived
  from its persistent faction ID. Formats 70–74 migrate through the same chain.

### 1. Parameterized frontier map

- [x] Add a localized New Game setup shell with Tutorial reserved as an
  unavailable mode and Custom Map as the active mode. Seed and square map size
  are selectable now; unsupported settings remain visible and honestly disabled.
- [x] Extract the current surface, river, wetland, settlement-pad, relief, and
  dimension constants into the validated embedded `core:demo-swamp-frontier`
  location profile. `LocationGenerationRequest` now carries its stable profile
  ID and river mode into generation, snapshots, state hashes, and format-73
  saves; formats 70–72 migrate to the existing profile and single-channel
  river. Deep geology remains a separate future profile boundary.
- [ ] Turn climate into a profile selection once at least one additional
  climate controls ecology, seasons, terrain, and settlement constraints.
- [x] Add river modes (absent, single channel, and branching) to the generation
  request and persist the selected mode in save metadata.
  All three are selectable in New Game and persisted in format 73. Branching
  adds a deterministic narrower channel joined to the main river; crossing
  selection remains part of the later road-generation stage.
- [x] Add road modes, including an absent road, a through-road, and a junction;
  select crossings only after hydrology has been generated. The validated
  location profile owns route endpoints, meander, junction and width. Roads are
  a separate surface feature rather than a replacement terrain kind, generate
  shallow fords across water, remain clear of initial ecology and structures,
  render in the world and minimap, and are selectable in New Game. The generated
  map also retains ordered north-to-south and junction-branch logical paths with
  named endpoints and bounded approach-prefix access for future visitors.
  Route metadata remains deterministic derived data and participates in the map
  fingerprint instead of becoming mutable save state. Format-76 saves pin the
  mode; format-75 worlds migrate to no generated road and preserve their existing
  map fingerprint.
- [ ] Parameterize the number and composition of neighboring civilizations and
  keep the generated polity identities in the save.
- [ ] Make the initial human village optional and replace its rigid coordinates
  with constraint-based placement.
- [ ] Add ruggedness presets backed by bounded elevation and slope parameters.
- [ ] Add enemy-difficulty profiles that adjust content parameters and arrival
  pressure rather than scattering UI-specific multipliers through simulation.
- [ ] Implement the Tutorial as a separate scenario/profile with guided goals,
  fixed teaching constraints, and its own compatibility identifier.
- [ ] Increase the default EA map size only after profiling generation,
  navigation, visibility, rendering, and save size.
- [x] Add optional road generation with map-edge endpoints and junctions.
- [ ] Add road bridges selected after river generation.
- [x] Give generator-v18 rivers a real channel volume at level -1 and a deeper
  core reaching level -2; deeper pools use the same three-level geometry.
  Generator-v17 saves retain their original river bed and derive any breach
  flooding from their saved excavation topology.
- [ ] Generate underground rivers and lake outlets as connected multi-level
  features, with explicit springs plus map-edge river inflow and outflow acting
  as stable source and sink boundaries.
- [ ] Add finite volume, vertical falls, pressure, flow cadence, drainage,
  evaporation, pumps, gates, channels and other constructed hydraulic controls
  as one conservative mutable-fluid system rather than independent wet-cell
  effects.
- [ ] After Early Access, model bend-dependent sediment deposition: broader
  sand bars on the outside of selected meanders, matching inner-bank erosion,
  islands, and local channel migration. Keep this cosmetic/geological pass
  subordinate to deterministic crossings and settlement-access validation.
- [ ] Add weighted ecology by climate and location character.
- [ ] Add sparse ruins and reserve their footprints before vegetation and loose
  resources are placed.
- [ ] Enable each New Game control only when generation, validation, save/load,
  and localized presentation support the complete parameter contract.

### 2. External kingdoms and ordinary arrivals

- [ ] Create human, surface-dwarf, and elven kingdom polity templates.
- [ ] Add map-edge arrival/departure regions tied to generated roads and
  traversable wilderness edges. Ordered road paths and edge endpoint metadata
  are available; the arrival-region policy and lifecycle remain to implement.
- [ ] Implement wandering small groups and adventurers first; they need no
  persistent off-map economy.
- [ ] Add patrols with route, observation, trespass response, and retreat.
- [ ] Add caravans with guarded inventories, destination, departure, theft, and
  later trade interaction.

### 3. Escalation and expeditions

- [ ] Record offences and witnesses against the responsible polity.
- [ ] Schedule increasingly equipped punitive expeditions from hostility,
  casualties, stolen value, and elapsed time.
- [ ] Give expeditions explicit objectives: reconnaissance, rescue, recovery,
  destruction, capture, or occupation.
- [ ] Ensure every party can abandon an impossible objective and leave the map.

### 4. Settlers and local settlements

- [ ] Add settler wagons, supplies, candidate-site evaluation, and camp phase.
- [ ] Reuse the generalized settlement subsystem rather than cloning
  `HumanVillageState`.
- [ ] Grow camps into small villages from blueprint sets and available material.
- [ ] Support destruction, abandonment, occupation, and rebuilding.
- [ ] Keep cities and large permanent populations outside the initial EA map.

### 5. Bandits

- [ ] Model each band as a polity with broad hostility rather than a special
  combat flag.
- [ ] Let bands raid goblins, villages, settlers, and caravans.
- [ ] Scale group size, equipment, scouts, and objectives with world age and
  observed wealth.
- [ ] Allow some bands merely to cross the map or establish temporary camps.

### 6. Trade and diplomacy

- [ ] Add contact state, interpretable offers, valuation profiles, and protected
  trade inventories.
- [ ] Unlock trade for sufficiently organized goblin societies without making
  peaceful play mandatory.
- [ ] Persist treaties, debts, reputation, and known routes per polity.

### 7. Legendary threats

- [ ] Add a legendary-creature director gated by civilization development,
  wealth, depth, discoveries, or deliberate summoning.
- [ ] Reuse species definitions where possible, but give legendary actors their
  own identity, equipment, abilities, lair, followers, and objective strategy.
- [ ] Guarantee warnings and preparation time appropriate to the threat.
- [ ] Save the summon/arrival lifecycle deterministically and allow mods to add
  definitions without adding central switches.

### 8. Observation and defence

- [ ] Add guard posts and watchtowers with staffing and visibility rules.
- [ ] Expose detected parties, direction, estimated size, and affiliation in a
  localized warning flow.
- [ ] Connect roads, gates, patrol orders, and defensive zones without making a
  tower a global omniscience upgrade.

## Safety and compatibility rules

- Do not combine the first polity save migration with map profile migration.
- Keep deterministic random domains and sample keys stable when moving numeric
  parameters; changing values is a separate balance change.
- A missing required civilization, controller, name generator, equipment table,
  visual asset, or location profile must reject a package before simulation
  mutation.
- New parties must have bounded pathfinding budgets and a tested departure path.
- Increased map dimensions require profiling; a larger default is not accepted
  on visual impression alone.
- Every new player-facing civilization, polity, party, warning, and relation
  string must enter English and Polish catalogs together.

## Validation per slice

- catalog schema, ownership, reference, range, and override tests;
- deterministic generation and save/load state-hash tests;
- focused lifecycle tests for arrivals, retreat, founding, destruction, and
  departure;
- full thematic simulation suite;
- Godot build and localized presentation checks when snapshots reach the UI;
- profiling for map size, party count, and navigation changes.
