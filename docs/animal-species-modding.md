# Animal species configuration

Animal species are defined by `content/animal-species.json` in `core-pack` and
optional content packs. The document uses schema version 1. A content pack may
provide only the species it replaces; definitions are merged by stable species
ID in configured load order. Each supplied species entry is a complete
replacement rather than a recursive partial JSON patch.

The current compatibility stage permits safe overrides of the five core species
and their existing `AnimalKind` adapters. Adding a completely new runtime
species is reserved for the save migration that will store stable species IDs in
animal state and snapshots instead of requiring an enum value.

## Definition areas

Each species definition contains:

- `id` and the temporary `legacyKind` save adapter;
- vital statistics and habitat constraints;
- a stable behavior-model ID, aggression level, perception and hunger
  thresholds, roaming cadence, and enemy selectors;
- generator mode, order, depth range, density and population scaling;
- ecology profile, attacks, harvest yields and byproducts;
- debug visibility radius;
- renderer, atlas and sprite IDs plus named color keys.

Enemy selectors use stable IDs and one of `Species`, `EntityType`, or `Group`.
The core definitions currently use the `core:goblins` group. The model is ready
for cross-species hostility, although combat between two animal species is not
implemented yet.

## Visual contract

Simulation data names visual resources but never contains Godot `res://` paths.
The Godot adapter resolves and validates those IDs before a candidate content
pack is activated. An unknown renderer, unsupported renderer/atlas/sprite
combination, missing atlas resource, or incomplete palette rejects that package
and leaves the previously active catalogs intact.

Current core renderers are:

- `core:procedural-hare`;
- `core:procedural-boar`;
- `core:atlas-sprite` with atlas `core:underground-fauna` and sprite
  `core:cave-spider`.

Procedural renderers consume semantic palette keys such as `body`, `accent`,
`eye`, and `tusk`. Atlas creatures use `edge`, `shadow`, `midtone`, and
`highlight`; the adapter remaps source luminance through those four colors and
caches the resulting texture per species. A separate `threat` key colors the
aggression indicator.

Pack-owned atlas registration and loading image bytes directly from `.gobmod`
archives is the next visual-asset stage. Until that registry is implemented, a
mod can safely recolor and rebalance core visual models but an unknown custom
atlas ID is deliberately rejected instead of producing an invisible creature.
