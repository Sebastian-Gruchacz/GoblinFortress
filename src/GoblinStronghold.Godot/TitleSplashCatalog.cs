using System.Reflection;
using GoblinStronghold.Simulation.Localization;

namespace GoblinStronghold.GodotClient;

internal static class TitleSplashCatalog
{
    private static readonly Dictionary<string, string[]> EntriesByLocale = new()
    {
        ["en"] = LoadEntries("en"),
        ["pl"] = LoadEntries("pl"),
    };

    public static string Pick(string locale, string fallback)
    {
        var normalized = TranslationCatalog.NormalizeLocale(locale);
        var entries = EntriesByLocale.GetValueOrDefault(normalized) ?? [];
        return entries.Length == 0
        ? fallback
        : entries[Random.Shared.Next(entries.Length)];
    }

    private static string[] LoadEntries(string locale)
    {
        var resourceName =
            $"GoblinStronghold.GodotClient.Content.title-splashes.{locale}.txt";
        using var stream = Assembly.GetExecutingAssembly()
            .GetManifestResourceStream(resourceName);
        if (stream is null)
        {
            return [];
        }

        using var reader = new StreamReader(stream);
        var entries = new List<string>();
        while (reader.ReadLine() is { } line)
        {
            var entry = line.Trim();
            if (entry.Length > 0 && !entry.StartsWith('#'))
            {
                entries.Add(entry);
            }
        }

        return entries.Distinct(StringComparer.Ordinal).ToArray();
    }
}
