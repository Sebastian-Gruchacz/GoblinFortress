using System.Collections.ObjectModel;
using System.Text.Json;

namespace GoblinStronghold.Simulation.ContentPacks;

public sealed record ContentPackPreference(string Id, bool Enabled);

public sealed class ContentPackUserPreferences
{
    private const int CurrentSchemaVersion = 1;
    private readonly List<ContentPackPreference> _entries;

    private ContentPackUserPreferences(IEnumerable<ContentPackPreference> entries)
    {
        _entries = entries.ToList();
        ValidateEntries(_entries);
    }

    public IReadOnlyList<ContentPackPreference> Entries =>
        new ReadOnlyCollection<ContentPackPreference>(_entries);

    public static ContentPackUserPreferences Empty() => new([]);

    public static ContentPackUserPreferences Load(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (!File.Exists(path))
        {
            return Empty();
        }

        try
        {
            var document = JsonSerializer.Deserialize<PreferenceDocument>(
                File.ReadAllText(path),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                ?? throw new InvalidDataException("Content pack preferences are empty.");
            if (document.SchemaVersion != CurrentSchemaVersion)
            {
                throw new InvalidDataException(
                    $"Unsupported content pack preference schema {document.SchemaVersion}.");
            }
            return new ContentPackUserPreferences(document.Packages);
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException(
                "Content pack preferences contain invalid JSON.",
                exception);
        }
    }

    public IReadOnlyList<ContentPack> Order(IEnumerable<ContentPack> packs)
    {
        ArgumentNullException.ThrowIfNull(packs);
        var positions = _entries
            .Select((entry, index) => (entry.Id, index))
            .ToDictionary(item => item.Id, item => item.index, StringComparer.OrdinalIgnoreCase);
        return Array.AsReadOnly(packs
            .OrderBy(pack => positions.GetValueOrDefault(pack.Manifest.Id, int.MaxValue))
            .ThenBy(pack => pack.SourceName, StringComparer.OrdinalIgnoreCase)
            .ToArray());
    }

    public bool IsEnabled(string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        return _entries.FirstOrDefault(entry =>
            string.Equals(entry.Id, id, StringComparison.OrdinalIgnoreCase))?.Enabled ?? true;
    }

    public void ReplaceVisible(IEnumerable<ContentPackPreference> visibleEntries)
    {
        ArgumentNullException.ThrowIfNull(visibleEntries);
        var visible = visibleEntries.ToArray();
        ValidateEntries(visible);
        var visibleIds = visible.Select(entry => entry.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var missing = _entries.Where(entry => !visibleIds.Contains(entry.Id)).ToArray();
        _entries.Clear();
        _entries.AddRange(visible);
        _entries.AddRange(missing);
    }

    public void Save(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var document = new PreferenceDocument
        {
            SchemaVersion = CurrentSchemaVersion,
            Packages = _entries.ToList(),
        };
        var temporaryPath = path + ".tmp";
        File.WriteAllText(
            temporaryPath,
            JsonSerializer.Serialize(
                document,
                new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temporaryPath, path, overwrite: true);
    }

    private static void ValidateEntries(IReadOnlyCollection<ContentPackPreference> entries)
    {
        if (entries.Count > 4096 ||
            entries.Any(entry => string.IsNullOrWhiteSpace(entry.Id) || entry.Id.Length > 128) ||
            entries.Select(entry => entry.Id)
                .Distinct(StringComparer.OrdinalIgnoreCase).Count() != entries.Count)
        {
            throw new InvalidDataException(
                "Content pack preferences contain invalid or duplicate package IDs.");
        }
    }

    private sealed class PreferenceDocument
    {
        public int SchemaVersion { get; init; }
        public List<ContentPackPreference> Packages { get; init; } = [];
    }
}
