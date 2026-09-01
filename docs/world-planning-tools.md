# World planning tools

## Purpose

Player actions that alter the world need a shared menu and input framework, but
they are not all constructions. The framework must preserve these separate
domains instead of growing another enum with unrelated special cases.

## Domains

```text
world planning
  construction
    simple placement     doors, containers, torches, built ramps
    building blueprints  huts, camps, workshops and future multi-stage buildings
    cell designations    walls, floors and walkways
  zones                  storage areas and future rooms, fields or restricted areas
  terrain                mining, carved ramps and future smoothing or engraving
  work orders            gathering, felling, hunting, cleaning and scouting
```

Construction creates a material-consuming construction site and eventually a
built result. Zone designation changes an area policy without pretending that
the zone is a building. Terrain modification changes the map through work and
must retain geology, discovery and hazard rules. General work orders target
world entities or cells without belonging to any of the other domains.

Wooden and stone ramps are constructions. Carving a ramp into rock is a terrain
modification even when both tools use a similar directional placement gesture.

## Menu composition

Content uses stable, non-localized path segments such as `basic/storage`,
`terrain/routes`, and `advanced/production`. A presenter turns those paths into
flat primary menus. Orders are arranged as four rows in one second-level grid:
gathering, destructive, combat, and miscellaneous actions. Recursive submenus
remain available for future branches that become too broad for a readable grid.
Labels, tooltips and accessibility text come from the normal English and Polish
localization catalogs; icons come from validated presentation metadata.

A menu node may contain both child nodes and actions. The catalog preserves
declared order, while package composition may later add an explicit ordering
hint. Missing localization, icons, referenced content, or action handlers must
invalidate the contributing package before the active menu is replaced.

The simulation construction catalog exposes planning mode, placement mode and
recursive menu queries. Godot now uses focused tile-menu controllers for Basic
constructions, Other constructions, terrain work, and grouped orders. Built
walkways and ramps appear beside terrain-shaping tools while remaining
construction-domain actions. Icon/action binding and `BuildMode` dispatch remain
compatibility composition until stable world-tool action IDs replace them.

Terrain modification now has its own embedded catalog and `terrain/excavation`
branch. Its toolbar popup directly exposes mining and carved ramps, followed by
routes and shaping tools in separate rows. Unimplemented paths, roads, raising,
and leveling remain visible but disabled so the intended layout is stable.
Mining uses an area gesture; carved ramps use a single applicable cell. The existing
`WorkDesignationKind` values and work execution remain compatibility adapters
until terrain designations gain stable IDs in saves.

Material-bearing construction families remember the last selected variant per
game session. With no remembered choice, the client selects the compatible
variant with the largest stored quantity. The picker marks that default and
activates it even if the popup is dismissed. These presentation preferences are
stored under `clientPreferences` in the game save; the simulation loader ignores
the client envelope and old saves remain valid.

## Placement gestures

- `Point`: one selected cell.
- `Line`: an axis-aligned sequence between start and end.
- `Area`: a rectangular designation.
- `FixedFootprint`: a blueprint-owned footprint anchored on one cell.
- `InferredConnection`: one selected endpoint plus a connection inferred from
  discovered world geometry, currently used by built ramps.

Gesture handling belongs to the input/controller layer. Placement validation and
results remain in their owning simulation subsystem. This lets zones, terrain
tools and constructions reuse pointer behavior without sharing domain state.

## Compatibility migration

1. Keep `ConstructionKind`, `WorkDesignationKind`, commands and save DTOs stable.
2. Replace Godot menu switches with stable world-tool definitions and handlers.
3. Move zone actions behind a zone-planning controller.
4. Move mining and carving behind a terrain-modification controller and catalog.
   Catalog and menu separation completed; command execution and persisted
   designations still use the legacy work contracts.
5. Add stable tool IDs to saves only when active designations can survive missing
   or reordered packages safely.
