using System.Collections.ObjectModel;
using System.Text.Json;
using GoblinStronghold.Simulation.ContentPacks;

namespace GoblinStronghold.Simulation.Localization;

public static class TranslationCatalog
{
    public const string FallbackLocale = "en";
    private static readonly string[] SupportedLocaleCodes = ["en", "pl"];
    private static readonly string[] SectionNames =
        ["interface", "materials", "recipes", "workshops"];
    private static readonly Lazy<CatalogState> State = new(Load);

    public static IReadOnlyList<string> SupportedLocales => State.Value.Locales;

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
        var localeCode = NormalizeLocale(locale);
        return TryGet(localeCode, section, subsection, key, out var value) ||
               TryGet(FallbackLocale, section, subsection, key, out value)
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
        var path = new TranslationPath(
            NormalizeLocale(locale),
            section,
            subsection,
            key);
        return State.Value.Entries.TryGetValue(path, out value!);
    }

    public static string NormalizeLocale(string locale)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(locale);
        var separator = locale.IndexOfAny(['-', '_']);
        var normalized = (separator < 0 ? locale : locale[..separator]).ToLowerInvariant();
        return SupportedLocaleCodes.Contains(normalized, StringComparer.OrdinalIgnoreCase)
            ? normalized
            : FallbackLocale;
    }

    private static CatalogState Load()
    {
        var entries = new Dictionary<TranslationPath, string>();
        foreach (var locale in SupportedLocaleCodes)
        {
            foreach (var section in SectionNames)
            {
                var contentPath = $"localization/{locale}/{section}.json";
                using var stream = CoreContentPack.Pack.OpenRead(contentPath);
                var document = JsonSerializer.Deserialize<TranslationDocument>(
                    stream,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                    ?? throw new InvalidOperationException(
                        $"Translation catalog '{contentPath}' is empty.");
                if (document.SchemaVersion != 1 ||
                    !string.Equals(document.Locale, locale, StringComparison.OrdinalIgnoreCase) ||
                    !string.Equals(document.Section, section, StringComparison.OrdinalIgnoreCase) ||
                    document.Subsections.Count == 0)
                {
                    throw new InvalidOperationException(
                        $"Translation catalog '{contentPath}' has an invalid header.");
                }

                foreach (var (subsection, subsectionEntries) in document.Subsections)
                {
                    if (string.IsNullOrWhiteSpace(subsection) || subsectionEntries.Count == 0)
                    {
                        throw new InvalidOperationException(
                            $"Translation catalog '{contentPath}' has an empty subsection.");
                    }
                    foreach (var (key, value) in subsectionEntries)
                    {
                        var path = new TranslationPath(locale, section, subsection, key);
                        if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(value) ||
                            !entries.TryAdd(path, value))
                        {
                            throw new InvalidOperationException(
                                $"Translation catalog '{contentPath}' has an invalid entry.");
                        }
                    }
                }
            }
        }

        var fallbackKeys = entries.Keys
            .Where(path => path.Locale == FallbackLocale)
            .Select(path => path with { Locale = string.Empty })
            .ToHashSet();
        foreach (var locale in SupportedLocaleCodes.Where(code => code != FallbackLocale))
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

        return new CatalogState(
            Array.AsReadOnly(SupportedLocaleCodes.ToArray()),
            new ReadOnlyDictionary<TranslationPath, string>(entries));
    }

    private readonly record struct TranslationPath(
        string Locale,
        string Section,
        string Subsection,
        string Key);

    private sealed record CatalogState(
        IReadOnlyList<string> Locales,
        IReadOnlyDictionary<TranslationPath, string> Entries);

    private sealed class TranslationDocument
    {
        public int SchemaVersion { get; init; }
        public string Locale { get; init; } = string.Empty;
        public string Section { get; init; } = string.Empty;
        public Dictionary<string, Dictionary<string, string>> Subsections { get; init; } = [];
    }
}
