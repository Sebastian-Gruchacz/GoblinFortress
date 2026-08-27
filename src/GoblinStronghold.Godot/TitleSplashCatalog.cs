using System.Reflection;

namespace GoblinStronghold.GodotClient;

internal static class TitleSplashCatalog
{
    private const string ResourceName =
        "GoblinStronghold.GodotClient.Content.title-splashes.pl.txt";

    private static readonly string[] Entries = LoadEntries();

    public static string Pick(string fallback) => Entries.Length == 0
        ? fallback
        : Entries[Random.Shared.Next(Entries.Length)];

    private static string[] LoadEntries()
    {
        using var stream = Assembly.GetExecutingAssembly()
            .GetManifestResourceStream(ResourceName);
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
