using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace GoblinStronghold.GodotClient.Application.Profiles;

internal readonly record struct StoredWindowLayout(
    int X,
    int Y,
    int Width,
    int Height);

internal sealed class PlayerProfileLayoutStore
{
    private const int CurrentFormatVersion = 1;
    private readonly string _directory;
    private readonly JsonSerializerOptions _jsonOptions = new() { WriteIndented = true };

    internal PlayerProfileLayoutStore(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        _directory = directory;
    }

    internal IReadOnlyDictionary<string, StoredWindowLayout> Load(string profileName)
    {
        var path = GetProfilePath(profileName);
        if (!File.Exists(path))
        {
            return new Dictionary<string, StoredWindowLayout>(StringComparer.Ordinal);
        }

        try
        {
            var document = JsonSerializer.Deserialize<StoredProfileLayout>(
                File.ReadAllText(path),
                _jsonOptions);
            if (document is null ||
                document.FormatVersion != CurrentFormatVersion ||
                document.Windows is null)
            {
                return new Dictionary<string, StoredWindowLayout>(StringComparer.Ordinal);
            }

            return document.Windows
                .Where(item => IsValid(item.Key, item.Value))
                .ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal);
        }
        catch (Exception exception) when (exception is IOException or
            UnauthorizedAccessException or JsonException)
        {
            return new Dictionary<string, StoredWindowLayout>(StringComparer.Ordinal);
        }
    }

    internal void Save(
        string profileName,
        IReadOnlyDictionary<string, StoredWindowLayout> windows)
    {
        ArgumentNullException.ThrowIfNull(windows);
        var path = GetProfilePath(profileName);
        var temporaryPath = path + ".tmp";
        Directory.CreateDirectory(_directory);

        var validWindows = windows
            .Where(item => IsValid(item.Key, item.Value))
            .OrderBy(item => item.Key, StringComparer.Ordinal)
            .ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal);
        var json = JsonSerializer.Serialize(
            new StoredProfileLayout(CurrentFormatVersion, validWindows),
            _jsonOptions);
        File.WriteAllText(temporaryPath, json);
        File.Move(temporaryPath, path, overwrite: true);
    }

    internal string GetProfilePath(string profileName)
    {
        if (!GameProfileName.TryNormalize(profileName, out var normalized))
        {
            throw new ArgumentException("A valid profile name is required.", nameof(profileName));
        }

        var profileKey = Convert.ToHexStringLower(
            SHA256.HashData(Encoding.UTF8.GetBytes(normalized)));
        return Path.Combine(_directory, profileKey + ".json");
    }

    private static bool IsValid(string windowId, StoredWindowLayout layout) =>
        !string.IsNullOrWhiteSpace(windowId) &&
        layout.Width > 0 &&
        layout.Height > 0;

    private sealed record StoredProfileLayout(
        int FormatVersion,
        Dictionary<string, StoredWindowLayout>? Windows);
}
