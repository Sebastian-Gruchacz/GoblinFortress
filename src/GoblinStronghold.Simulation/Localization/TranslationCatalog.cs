using System.Collections.ObjectModel;
using System.Text.Json;
using GoblinStronghold.Simulation.ContentPacks;

namespace GoblinStronghold.Simulation.Localization;

public static class TranslationCatalog
{
    public const string FallbackLocale = "en";
    private static readonly string[] CoreLocaleCodes = ["en", "pl"];
    private static readonly string[] SectionNames =
        ["interface", "materials", "recipes", "workshops"];
    private static CatalogState _state = Load([]);

    public static IReadOnlyList<string> SupportedLocales => CurrentState.Locales;
    public static IReadOnlyList<string> ConfiguredPackIds => CurrentState.PackIds;

    public static void ConfigurePacks(IEnumerable<ContentPack> packs)
    {
        ArgumentNullException.ThrowIfNull(packs);
        var next = Load(packs.ToArray());
        Interlocked.Exchange(ref _state, next);
    }

    public static void ResetToCorePack() => ConfigurePacks([]);

    public static string Get(
        string locale,
        string section,
        string subsection,
        string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(locale);
        ArgumentException.ThrowIfNullOrWhiteSpace(section);
        ArgumentException.ThrowIfNullOrWhiteSpace(subsection);
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        var state = CurrentState;
        var localeCode = NormalizeLocale(locale, state);
        return TryGet(state, localeCode, section, subsection, key, out var value) ||
               TryGet(state, FallbackLocale, section, subsection, key, out value)
            ? value
            : $"[{section}.{subsection}.{key}]";
    }

    public static bool TryGet(
        string locale,
        string section,
        string subsection,
        string key,
        out string value)
    {
        var state = CurrentState;
        return TryGet(
            state,
            NormalizeLocale(locale, state),
            section,
            subsection,
            key,
            out value);
    }

    public static string NormalizeLocale(string locale)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(locale);
        return NormalizeLocale(locale, CurrentState);
    }

    public static string GetLocaleDisplayName(string locale, string targetLocale)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(locale);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetLocale);
        var state = CurrentState;
        var normalizedLocale = NormalizeLocale(locale, state);
        var normalizedTarget = NormalizeLocale(targetLocale, state);
        if (TryGet(
                state,
                normalizedLocale,
                "interface",
                "language",
                normalizedTarget,
                out var value) ||
            TryGet(
                state,
                FallbackLocale,
                "interface",
                "language",
                normalizedTarget,
                out value))
        {
            return value;
        }

        return state.LocaleDisplayNames.GetValueOrDefault(normalizedTarget)
            ?? normalizedTarget;
    }

    private static CatalogState CurrentState => Volatile.Read(ref _state);

    private static CatalogState Load(IReadOnlyList<ContentPack> packs)
    {
        var duplicatePackId = packs
            .GroupBy(pack => pack.Manifest.Id, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicatePackId is not null)
        {
            throw new InvalidDataException(
                $"Content pack ID '{duplicatePackId.Key}' is loaded more than once.");
        }

        var entries = new Dictionary<TranslationPath, string>();
        foreach (var locale in CoreLocaleCodes)
        {
            foreach (var section in SectionNames)
            {
                var contentPath = $"localization/{locale}/{section}.json";
                AddDocument(
                    entries,
                    CoreContentPack.Pack,
                    contentPath,
                    locale,
                    section,
                    allowOverrides: false);
            }
        }

        var fallbackKeys = entries.Keys
            .Where(path => path.Locale == FallbackLocale)
            .Select(path => path with { Locale = string.Empty })
            .ToHashSet();
        foreach (var locale in CoreLocaleCodes.Where(code => code != FallbackLocale))
        {
            var localeKeys = entries.Keys
                .Where(path => path.Locale == locale)
                .Select(path => path with { Locale = string.Empty })
                .ToHashSet();
            if (!fallbackKeys.SetEquals(localeKeys))
            {
                throw new InvalidOperationException(
                    $"Locale '{locale}' does not define the same translation keys as English.");
            }
        }

        foreach (var pack in packs)
        {
            if (pack.Manifest.Type == "core")
            {
                throw new InvalidDataException(
                    $"External package '{pack.Manifest.Id}' cannot replace core-pack.");
            }
        }

        foreach (var pack in packs.Where(pack => pack.Manifest.Type == "content"))
        {
            var packEntries = LoadPackTranslations(pack);
            ValidateEnglishFallbacks(pack, packEntries);
            foreach (var (path, value) in packEntries)
            {
                if (!entries.TryAdd(path, value))
                {
                    throw new InvalidDataException(
                        $"Content pack '{pack.Manifest.Id}' conflicts with translation " +
                        $"'{path.Section}.{path.Subsection}.{path.Key}' in locale " +
                        $"'{path.Locale}'.");
                }
            }
        }

        var locales = CoreLocaleCodes.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var localeDisplayNames = new Dictionary<string, string>(
            StringComparer.OrdinalIgnoreCase);
        foreach (var pack in packs.Where(pack => pack.Manifest.Type == "language"))
        {
            var packEntries = LoadPackTranslations(pack);
            if (packEntries.Count == 0)
            {
                throw new InvalidDataException(
                    $"Language pack '{pack.Manifest.Id}' contains no translation catalogs.");
            }

            foreach (var (path, value) in packEntries)
            {
                var englishPath = path with { Locale = FallbackLocale };
                if (!entries.ContainsKey(englishPath))
                {
                    throw new InvalidDataException(
                        $"Language pack '{pack.Manifest.Id}' translates " +
                        $"'{path.Section}.{path.Subsection}.{path.Key}' without an English " +
                        "fallback in core-pack or an enabled content mod.");
                }
                entries[path] = value;
            }

            var locale = NormalizeLocaleCode(pack.Manifest.Locale!);
            locales.Add(locale);
            var displayName = string.IsNullOrWhiteSpace(pack.Manifest.LocaleDisplayName)
                ? pack.Manifest.Title
                : pack.Manifest.LocaleDisplayName;
            if (!string.IsNullOrWhiteSpace(displayName))
            {
                localeDisplayNames[locale] = displayName;
            }
        }

        var orderedLocales = CoreLocaleCodes
            .Concat(locales.Except(CoreLocaleCodes, StringComparer.OrdinalIgnoreCase)
                .Order(StringComparer.Ordinal))
            .ToArray();
        return new CatalogState(
            Array.AsReadOnly(orderedLocales),
            Array.AsReadOnly(packs.Select(pack => pack.Manifest.Id).ToArray()),
            new ReadOnlyDictionary<TranslationPath, string>(entries),
            new ReadOnlyDictionary<string, string>(localeDisplayNames));
    }

    private static Dictionary<TranslationPath, string> LoadPackTranslations(
        ContentPack pack)
    {
        var packEntries = new Dictionary<TranslationPath, string>();
        var translationFiles = pack.FilePaths
            .Where(path => path.StartsWith("localization/", StringComparison.OrdinalIgnoreCase) &&
                path.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            .Order(StringComparer.Ordinal)
            .ToArray();
        foreach (var contentPath in translationFiles)
        {
            var (locale, section) = ResolveExternalTranslationPath(pack, contentPath);
            AddDocument(
                packEntries,
                pack,
                contentPath,
                locale,
                section,
                allowOverrides: false);
        }
        return packEntries;
    }

    private static void ValidateEnglishFallbacks(
        ContentPack pack,
        IReadOnlyDictionary<TranslationPath, string> packEntries)
    {
        foreach (var path in packEntries.Keys.Where(path =>
                     path.Locale != FallbackLocale))
        {
            if (!packEntries.ContainsKey(path with { Locale = FallbackLocale }))
            {
                throw new InvalidDataException(
                    $"Content pack '{pack.Manifest.Id}' translates " +
                    $"'{path.Section}.{path.Subsection}.{path.Key}' without embedding its " +
                    "English fallback.");
            }
        }
    }

    private static void AddDocument(
        IDictionary<TranslationPath, string> entries,
        ContentPack pack,
        string contentPath,
        string expectedLocale,
        string expectedSection,
        bool allowOverrides)
    {
        using var stream = pack.OpenRead(contentPath);
        TranslationDocument document;
        try
        {
            document = JsonSerializer.Deserialize<TranslationDocument>(
                stream,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                ?? throw new InvalidDataException(
                    $"Translation catalog '{pack.Manifest.Id}/{contentPath}' is empty.");
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException(
                $"Translation catalog '{pack.Manifest.Id}/{contentPath}' is invalid JSON.",
                exception);
        }
        var locale = NormalizeLocaleCode(document.Locale);
        if (document.SchemaVersion != 1 ||
            !string.Equals(locale, NormalizeLocaleCode(expectedLocale),
                StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(document.Section, expectedSection,
                StringComparison.OrdinalIgnoreCase) ||
            document.Subsections.Count == 0)
        {
            throw new InvalidDataException(
                $"Translation catalog '{pack.Manifest.Id}/{contentPath}' has an invalid header.");
        }

        foreach (var (subsection, subsectionEntries) in document.Subsections)
        {
            if (string.IsNullOrWhiteSpace(subsection) || subsectionEntries.Count == 0)
            {
                throw new InvalidDataException(
                    $"Translation catalog '{pack.Manifest.Id}/{contentPath}' has an empty " +
                    "subsection.");
            }
            foreach (var (key, value) in subsectionEntries)
            {
                var path = new TranslationPath(
                    locale,
                    expectedSection,
                    subsection,
                    key);
                if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(value) ||
                    (!allowOverrides && entries.ContainsKey(path)))
                {
                    throw new InvalidDataException(
                        $"Translation catalog '{pack.Manifest.Id}/{contentPath}' has an " +
                        $"invalid or conflicting entry '{subsection}.{key}'.");
                }
                entries[path] = value;
            }
        }
    }

    private static (string Locale, string Section) ResolveExternalTranslationPath(
        ContentPack pack,
        string contentPath)
    {
        var segments = contentPath.Split('/');
        string locale;
        string sectionFile;
        if (segments.Length == 2 && pack.Manifest.Type == "language")
        {
            locale = pack.Manifest.Locale!;
            sectionFile = segments[1];
        }
        else if (segments.Length == 3)
        {
            locale = segments[1];
            sectionFile = segments[2];
            if (pack.Manifest.Type == "language" &&
                !string.Equals(
                    NormalizeLocaleCode(locale),
                    NormalizeLocaleCode(pack.Manifest.Locale!),
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    $"Language pack '{pack.Manifest.Id}' contains locale '{locale}' but " +
                    $"declares '{pack.Manifest.Locale}'.");
            }
        }
        else
        {
            throw new InvalidDataException(
                $"Package '{pack.Manifest.Id}' has unsupported translation path " +
                $"'{contentPath}'.");
        }

        var section = Path.GetFileNameWithoutExtension(sectionFile);
        if (string.IsNullOrWhiteSpace(section))
        {
            throw new InvalidDataException(
                $"Package '{pack.Manifest.Id}' has invalid translation path '{contentPath}'.");
        }
        return (NormalizeLocaleCode(locale), section);
    }

    private static bool TryGet(
        CatalogState state,
        string locale,
        string section,
        string subsection,
        string key,
        out string value) =>
        state.Entries.TryGetValue(
            new TranslationPath(locale, section, subsection, key),
            out value!);

    private static string NormalizeLocale(string locale, CatalogState state)
    {
        var normalized = NormalizeLocaleCode(locale);
        if (state.Locales.Contains(normalized, StringComparer.OrdinalIgnoreCase))
        {
            return normalized;
        }

        var separator = normalized.IndexOf('-');
        var language = separator < 0 ? normalized : normalized[..separator];
        return state.Locales.Contains(language, StringComparer.OrdinalIgnoreCase)
            ? language
            : FallbackLocale;
    }

    private static string NormalizeLocaleCode(string locale)
    {
        if (string.IsNullOrWhiteSpace(locale))
        {
            throw new InvalidDataException("Locale code cannot be empty.");
        }
        var normalized = locale.Trim().Replace('_', '-').ToLowerInvariant();
        if (normalized.StartsWith("-", StringComparison.Ordinal) ||
            normalized.EndsWith("-", StringComparison.Ordinal) ||
            normalized.Split('-').Any(segment =>
                segment.Length == 0 || segment.Any(character =>
                    !char.IsAsciiLetterOrDigit(character))))
        {
            throw new InvalidDataException($"Invalid locale code '{locale}'.");
        }
        return normalized;
    }

    private readonly record struct TranslationPath(
        string Locale,
        string Section,
        string Subsection,
        string Key);

    private sealed record CatalogState(
        IReadOnlyList<string> Locales,
        IReadOnlyList<string> PackIds,
        IReadOnlyDictionary<TranslationPath, string> Entries,
        IReadOnlyDictionary<string, string> LocaleDisplayNames);

    private sealed class TranslationDocument
    {
        public int SchemaVersion { get; init; }
        public string Locale { get; init; } = string.Empty;
        public string Section { get; init; } = string.Empty;
        public Dictionary<string, Dictionary<string, string>> Subsections { get; init; } = [];
    }
}
