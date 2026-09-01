# Repository instructions

## Player-facing localization

- Add every new or changed player-facing string to the embedded localization catalogs in both English and Polish in the same change. English is the canonical fallback locale.
- Do not add player-facing prose directly to C# code, `.tscn` scenes, or other assets. Use `TranslationCatalog` and the locale selected through `LocaleSettings`. Stable internal identifiers, debug-only diagnostics, save-data keys, and proper names are exempt.
- Keep the key sets of `Localization/en/*.json` and `Localization/pl/*.json` identical. Prefer semantic, stable keys and format placeholders over concatenating translated fragments.
- Localize text, tooltips, window titles, status messages, confirmation/error dialogs, accessibility labels, key-binding descriptions, and title splashes. Check wrapping with both locales and with deliberately long fallback text.
- Embed locale-specific files in the main assembly. For filenames containing a locale suffix such as `.pl` or `.en`, set `WithCulture="false"` and verify the manifest resource name.
- Before finishing a UI change, run the localization tests, build the Godot project, and inspect the diff for newly hardcoded player-facing strings.
