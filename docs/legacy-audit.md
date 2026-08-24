# Goblin Stronghold — legacy archaeology audit

## Decision

The archived projects contain useful evidence of the original technical interests, but they are not a viable codebase for the new game.

**Recommendation:** preserve the archives as read-only historical material, carry selected ideas into the design, and reboot the active repository from a minimal modern foundation. Do not import archived source files into the new implementation.

## Material inspected

- `L:\_FOTO_BACKUP\BACKUP_MONSTER_LAPTOP\L_WORK\SDS\_Multigame2`
- `L:\_FOTO_BACKUP\BACKUP_MONSTER_LAPTOP\L_WORK\SDS\MultiGame`
- the current `GoblinFortress` repository
- the neighboring `OrcsStronghold` repository

The two backup roots contain several generations of unrelated or partially related work, external source drops, binaries, saved web pages and reference documents. File timestamps range from approximately 2008 to 2014; the backup copy dates are later and should not be treated as authorship dates.

## Recovered Goblin Stronghold pathfinding laboratory

The archived `Goblin Stronghold` directory is primarily a pathfinding research folder. Its own solution contains only:

- a generic priority queue;
- an incomplete A*-like `RouteFinder`;
- no game executable, map model or simulation;
- research material covering A* variants, D* Lite, HPA*, GHPA*, dynamic replanning and graph partitioning;
- a complete old Recast/METIS/SDL source bundle under `examples`.

The `Dynamic PathFinding algorithms.docx` file is a research clipping assembled in 2014. Its metadata names Sebastian Marek Gruchacz as author/editor, but most of its prose is copied from a web answer and academic references. It is useful as a record of the problem being investigated, not as an original game design document.

### RouteFinder quality

The archived route finder should not be ported:

- nodes contain mutable search state, so concurrent or overlapping searches are unsafe;
- state is not reset between searches;
- a better path to an already open or closed node is not correctly updated and re-enqueued;
- there are no tests;
- it implements neither dynamic replanning nor hierarchical pathfinding despite the surrounding research.

The historical priority queue is similarly unnecessary in modern .NET, which provides maintained priority queue implementations.

## Recovered Dwarfz proof of concept

`_Multigame2\Multigame\Dwarfz` contains the actual partially working prototype. A compiled `Dwarfz.exe` dated 2014 survives beside its content.

The source demonstrates:

- a generated 100 by 100 tile map;
- caves and mineral deposits;
- a starting base;
- approximately 29 autonomous `WildMiner` units;
- tiled XNA rendering;
- mouse scrolling and zoom;
- miners selecting short random targets, walking and turning dirt into empty space;
- placeholder building and unit categories including miners, engineers, mages, goblins, furnaces, barracks, living quarters, toolsmiths and storage.

This is valuable confirmation that a visible, running mining simulation existed. It is not a reusable simulation core.

### Important correction: it was not parallel

All units are updated sequentially inside the XNA main-thread `Update` loop. The prototype has no worker pool, task partitioning, simulation snapshots or thread-safe world ownership.

The separate `Multigame` shell contains fields named `AiThread` and `LoadingThread`, but:

- the thread objects are created but never started;
- their run flags remain false;
- the AI update body is effectively empty or commented out;
- the loading queue is accessed without synchronization;
- the structure and names were adapted directly from decompiled Magicka code stored in `_Multigame2\_M\Magicka`.

This code must not be treated as an implementation reference for the new project.

### Prototype defects that reinforce the rewrite decision

- Per-unit `Random` instances are created together and can receive identical seeds, producing correlated behavior.
- Movement can select `mapWidth` or `mapHeight` as a coordinate even though the last valid index is one less.
- World state, rendering state and behavior are tightly coupled to XNA classes.
- The map is two-dimensional and has no representation of stacked levels.
- There is no deterministic update contract, persistence contract or automated simulation test suite.

## MultiGame and d20 lineage

The older `MultiGame` tree is a general-purpose RPG/roguelike experiment rather than a fortress simulation. It contains:

- d20/SRD-derived abilities, classes, feats, conditions, carrying capacity and items;
- roguelike map and movement experiments;
- XML content and localization;
- WinForms editors;
- quest, faction, character and container data models;
- save/runtime separation experiments;
- official SRD/OGL reference material and other third-party documents.

The newer `Multigame` generation expands the authoring framework with stable identifiers, editable definitions, runtime wrappers, scripts and specialized editors. This demonstrates useful architectural instincts, but it is much broader than the game and heavily over-engineered for a first playable slice.

### d20 assessment

The d20 implementations should not be ported. They are incomplete, sparsely tested and encode large rule tables directly in code. Some implemented calculations contain apparent defects; for example, the encumbrance calculation computes a size-adjusted weight and then compares the unadjusted value using reversed-looking threshold conditions.

For a population simulation, heroic d20 rules would also create unnecessary per-agent state and sharp probability swings. The useful ideas are semantic rather than numerical:

- abilities and learned skills are distinct;
- temporary and permanent effects need explicit sources and stacking rules;
- randomness should be injected and reproducible;
- runtime state should be separate from authored definitions;
- actions should be represented as data or commands rather than direct UI calls.

These concepts should be redesigned for Goblin Stronghold and covered by deterministic tests.

## Provenance and clean-room boundary

Large parts of the backup are reference or third-party material, not original source suitable for import:

- `_Multigame2\_M` contains decompiled Magicka assemblies and game logic;
- the pathfinding example contains Recast, METIS, SDL and other third-party sources;
- `_DOC` contains books, saved web pages and copied articles;
- old `DOC\OfficialD20` contains SRD/OGL rules;
- tile and sprite collections come from external sets whose exact provenance would need to be re-established.

The new repository should not contain any of these files. If an old idea is reused, implement it from a new specification and current primary documentation, without copying decompiled or provenance-uncertain code.

## Ideas worth carrying forward

### Dynamic, hierarchical navigation

The old research correctly anticipated that a destructible multi-level settlement needs more than one naive A* search per unit.

The new design should reserve a path service boundary with:

- coarse routes between chunks, rooms or connected regions;
- local refinement near an agent;
- explicit invalidation when digging or construction changes connectivity;
- request batching and per-tick budgets;
- cached paths only while their relevant topology version remains valid;
- a fallback to a fresh search when incremental repair would touch too much of the graph;
- separate handling for many agents sharing a destination, where flow or field-based guidance may be cheaper.

No advanced algorithm needs to be implemented in the first slice. A correct deterministic local search behind this boundary is enough initially.

### Authored definitions versus runtime state

The old projects repeatedly separate definitions from runtime wrappers. The new project should preserve the principle in a much smaller form:

- immutable definitions for materials, knowledge, facilities and species traits;
- compact mutable state for goblins, cells, jobs and inventories;
- stable identifiers in saves and events;
- no editor framework until hand-authored data becomes a measured bottleneck.

### Time-sliced autonomous work

The `WildMiner` prototype already models actions with a remaining duration rather than moving on every render frame. That idea is worth retaining as explicit simulation work:

- jobs consume simulation time;
- movement and labor have costs;
- agents can be interrupted;
- the renderer only visualizes state transitions.

## Material to reject from the new codebase

- XNA and MonoGame project scaffolding;
- the old route finder and priority queue;
- all decompiled Magicka code;
- vendored Recast/METIS/SDL snapshots;
- the d20/SRD implementation and tables;
- WinForms content editors;
- old save formats and identifier managers;
- binaries, build outputs, upgrade logs and Visual Studio user files;
- provenance-uncertain art and copied reference documents.

## Proposed minimal reboot

Before removing tracked legacy files, preserve the current repository state under an explicit archival Git tag or branch. The active branch can then be reduced to:

- `README.md` — current pitch and development status;
- `LICENSE` — reviewed for the intended release model;
- `.gitignore` and optional `.editorconfig`;
- `docs/game-concept.md`;
- `docs/legacy-audit.md`;
- a minimal Godot 4 .NET project;
- a pure C# simulation project;
- a deterministic simulation test project.

The first implementation milestone should contain a headless deterministic world with generated terrain, basic goblin survival, one coarse human village and fog-of-war state. Combat and knowledge acquisition follow once the world provides valuable carriers and persistent consequences. Godot presentation should be added only after the relevant headless scenarios are testable.

## Reset gate

The destructive repository cleanup should happen only after:

1. the archive tag or branch is verified;
2. the desired license is confirmed;
3. the exact Godot and .NET versions are selected;
4. the first-playable knowledge-transfer rules are answered;
5. the user explicitly confirms the removal list.
