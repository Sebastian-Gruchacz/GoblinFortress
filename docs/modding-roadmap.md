# Modding and Steam Workshop roadmap

## Direction

Goblin Stronghold treats its built-in data as `core-pack`. New first-party
materials, biomes, tools, skills, recipes, translations, and similar content go
through the same logical content paths that external packs will use.

During development, `core-pack` is embedded in the game assemblies. Release
tooling may additionally produce a ZIP-compatible `.gobpack` artifact from the
same sources. Community packs use ZIP containers with dedicated extensions:

- `.goblang` for translation packs;
- `.gobmod` for content packs;
- `.gobpack` for first-party or general-purpose packages.

The game reads packages directly into memory. It does not extract them into the
installation directory.

## Package contract

Every package contains `manifest.json` at its root and uses normalized,
forward-slash paths. The initial manifest records:

- package format and schema version;
- stable, namespaced package ID;
- package type and version;
- title, authors, optional contact email, and optional README path;
- optional locale;
- compatible game and content-schema versions;
- dependencies and deterministic load-order hints.

Content identities use stable namespaced strings such as `core:iron` and
`marshes:bog_iron`. Enums remain only for closed engine concepts. Existing enum
values receive compatibility mappings while save data is migrated.

## Load pipeline

1. Load embedded `core-pack`.
2. Discover local packages under `user://mods`.
3. Discover subscribed Steam Workshop item folders.
4. Apply the active profile's enabled-package list and load order.
5. Validate manifests, dependencies, content IDs, references, and conflicts.
6. Freeze immutable runtime catalogs.

The Steam adapter supplies package locations only. Package parsing and
validation remain platform-independent, so local and Workshop installations
behave identically.

Packages are untrusted input. The loader rejects unsafe paths, duplicates,
unsupported schemas, excessive file counts, and excessive expanded sizes.
Initially, mods are data-only: no arbitrary C# assemblies or scripts.

## Localization layering

Translation composition always starts with a complete canonical English
baseline (`en`). Locale identifiers such as `en-EN`, `en-US`, and `en_GB`
normalize to that baseline when no more specific installed locale exists.

1. Load English from `core-pack`.
2. Add the embedded English resources from every enabled content mod.
3. Add any translations bundled by core and content mods.
4. Apply external language packs for their declared locale in deterministic
   load order.
5. Fall back per missing key to the assembled English baseline, then to the
   existing visible missing-key marker.

Every translation supplied by a content mod must have an English counterpart
inside that same mod. External language packs may be partial, but may only
override keys that exist in English in `core-pack` or an enabled mod. Ordinary
content mods add keys in their own namespace; language packs may translate both
core and mod namespaces. First-party player-facing changes continue to ship in
both English and Polish.

## Profiles and saves

Each profile owns its enabled package list and deterministic load order. Save
metadata records package IDs, versions, and content hashes. Loading checks this
manifest before mutating simulation state and reports missing or incompatible
content. Unknown IDs are never silently remapped to unrelated core content.

Steam Cloud synchronizes profiles and saves. Workshop remains responsible for
subscribed package downloads; Workshop files are not duplicated into Cloud.

## Delivery stages

Current implementation status: Stages 1 and 2 are complete, and Stage 4 has
started at the compatibility boundary. Local packages are discovered and
layered, and the title menu exposes persistent enable/load-order controls,
package metadata, README viewing, and load-error diagnostics. The runtime now
publishes one immutable, ordered active-package registry (always beginning with
embedded `core-pack`). Material and crafting-recipe IDs accept their legacy
core spelling while also exposing the canonical `core:item` form. This does
not yet permit mods to add arbitrary simulation materials or recipes: their
enum/save adapters must land before such definitions can be used safely.

### Stage 1: package foundation

- Define manifest and in-memory package models.
- Add bounded ZIP loading without extraction.
- Expose current embedded catalogs as `core-pack` paths.
- Cover traversal, duplicates, size limits, and embedded resources with tests.

### Stage 2: local language packs

- Discover `.goblang` files in `user://mods`.
- Compose translation layers and locale discovery.
- Add diagnostics and an in-game package list.
- Verify English fallback and deliberately long translations.

### Stage 3: profiles and save contracts

- Add multiple profiles and per-profile package configuration.
- Persist package version/hash metadata with every save series.
- Add missing-mod and incompatible-version load gates.

### Stage 4: extensible content IDs

- Introduce stable IDs and registries for materials and recipes first. (In
  progress: shared active-package registry and core material/recipe ID
  compatibility are implemented.)
- Animal definitions now load from embedded package JSON and content packs may
  override existing species by stable ID. AI, hostility selectors, spawning,
  loot, visual resource IDs, and palettes are validated before activation.
  New species await stable-ID animal save state; pack-owned atlas byte loading
  remains the next visual-resource step.
- Preserve adapters for existing enum-based simulation and legacy saves.
- Continue with tools, skills, biomes, workshops, and other catalogs.
- Validate cross-pack references after deterministic merge.

### Stage 5: Steam integration

- Isolate the Steam binding behind platform service interfaces.
- Read subscribed/install-ready Workshop folders through `ISteamUGC`.
- Handle download/update completion, dependencies, and load order.
- Keep local package loading available in non-Steam builds.

### Stage 6: authoring and publishing

- Add schema documentation, examples, and validation CLI.
- Generate deterministic packages and content hashes.
- Add Workshop upload/update tooling and legal-agreement handling.
- Add compatibility reporting for game and schema upgrades.
