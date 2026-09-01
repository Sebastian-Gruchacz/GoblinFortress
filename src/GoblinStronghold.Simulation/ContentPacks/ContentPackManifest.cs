namespace GoblinStronghold.Simulation.ContentPacks;

public sealed class ContentPackManifest
{
    public string Format { get; init; } = string.Empty;
    public int SchemaVersion { get; init; }
    public string Id { get; init; } = string.Empty;
    public string Type { get; init; } = string.Empty;
    public string Version { get; init; } = string.Empty;
    public string? Title { get; init; }
    public string? Author { get; init; }
    public List<string> Authors { get; init; } = [];
    public string? ContactEmail { get; init; }
    public string? ReadmePath { get; init; }
    public string? Locale { get; init; }
    public string? LocaleDisplayName { get; init; }
    public string? MinimumGameVersion { get; init; }
    public int ContentSchemaVersion { get; init; }
    public List<string> Dependencies { get; init; } = [];
    public List<string> LoadAfter { get; init; } = [];
    public List<string> LoadBefore { get; init; } = [];
}
