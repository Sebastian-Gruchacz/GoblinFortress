# Repository instructions

## Player-facing localization

- Add every new or changed player-facing string to the embedded localization catalogs in both English and Polish in the same change. English is the canonical fallback locale.
- Do not add player-facing prose directly to C# code, `.tscn` scenes, or other assets. Use `TranslationCatalog` and the locale selected through `LocaleSettings`. Stable internal identifiers, debug-only diagnostics, save-data keys, and proper names are exempt.
- Keep the key sets of `Localization/en/*.json` and `Localization/pl/*.json` identical. Prefer semantic, stable keys and format placeholders over concatenating translated fragments.
- Localize text, tooltips, window titles, status messages, confirmation/error dialogs, accessibility labels, key-binding descriptions, and title splashes. Check wrapping with both locales and with deliberately long fallback text.
- Embed locale-specific files in the main assembly. For filenames containing a locale suffix such as `.pl` or `.en`, set `WithCulture="false"` and verify the manifest resource name.
- Before finishing a UI change, run the localization tests, build the Godot project, and inspect the diff for newly hardcoded player-facing strings.

## Architecture and refactoring

- Do not add another content-type switch or substantial subsystem behavior to `SimulationEngine`, `Main`, or `WorldView` when it can live behind a focused catalog, policy, service, presenter, or controller.
- Treat `partial` files as a temporary navigation aid, not the target architecture. New subsystem logic belongs in a dedicated subdirectory and namespace with a small explicit API.
- Use stable content IDs for open/moddable concepts and retain enums only as compatibility adapters for closed engine concepts and legacy saves.
- Refactor in behavior-preserving vertical slices: characterize the current behavior with tests, extract one responsibility, keep save and command contracts stable, then run the full simulation tests and Godot build.
- Keep orchestration at the composition boundary. Domain classes must not depend on Godot nodes, localized UI prose, `user://` paths, Steam APIs, or ZIP discovery.
- Follow the staged structure and dependency rules in `docs/refactoring-roadmap.md`; update that document when a migration changes a boundary or completes a stage.
