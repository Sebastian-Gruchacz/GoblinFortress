# Localization

English (`en`) and Polish (`pl`) are supported. English is the fallback for an unsupported platform/system locale or a missing entry. With no stored preference, startup first asks an available GodotSteam `Steam` singleton for the current per-game language, then falls back to the operating-system locale. The resolved automatic locale is not persisted. The player's explicit choice is stored in `user://settings/locale.json` and takes precedence on later launches.

Runtime catalogs live in `src/GoblinStronghold.Simulation/Localization/<locale>/`. Both locales must contain exactly the same section/subsection/key set; `TranslationCatalog` rejects an incomplete pair while loading. Title-screen splashes are separate embedded line-based resources in `src/GoblinStronghold.Godot/Content/` because they are independently randomized prose collections.

The language selector is in Options. A language change is saved immediately and applied after restarting the game. This avoids destroying an active simulation merely to rebuild the current UI tree.

## Adding text

1. Add a semantic key and its English value.
2. Add the same key and its Polish value in the matching catalog.
3. Retrieve it through `TranslationCatalog` using the current locale; use placeholders for dynamic values instead of concatenating translated fragments.
4. Check both languages at narrow window sizes. Labels containing prose should use smart word wrapping.
5. Run `dotnet test` and build the Godot project.

## Existing debt

The original prototype placed a large body of Polish messages directly in `Main.cs` and `Main.tscn`. The locale plumbing, persistence, title screen, main menu, HUD shell, common windows, options, keyboard shortcuts, management/build/work/statistics tiles, toolbar tooltips, build/work tool prompts, work-area preview/results, construction placement/validation/previews, storage placement, construction-site diagnostics, core work/construction/crafting/combat/movement/raid events, calendar, main status bar, world context menus, and the terrain/cave/basic-world-object portion of inspection have been migrated. Detailed actor, animal, corpse, village, job, raid-planning-window, report, and logistics descriptions still require migration before English can be called complete. Do not extend that baseline: touch a player-facing string only together with its EN/PL catalog entries.
