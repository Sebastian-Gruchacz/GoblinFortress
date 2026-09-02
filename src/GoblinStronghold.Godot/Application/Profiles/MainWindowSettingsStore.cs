using System.Text.Json;
using System.Text.Json.Serialization;

namespace GoblinStronghold.GodotClient.Application.Profiles;

internal enum StoredMainWindowMode
{
    Windowed,
    Maximized,
    Fullscreen,
    ExclusiveFullscreen,
}

internal readonly record struct StoredMainWindowSettings(
    StoredMainWindowMode Mode,
    int WindowedWidth,
    int WindowedHeight);

internal sealed class MainWindowSettingsStore
{
    private const int CurrentFormatVersion = 1;
    private readonly string _path;
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    internal MainWindowSettingsStore(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        _path = path;
    }

    internal StoredMainWindowSettings? Load()
    {
        if (!File.Exists(_path))
        {
            return null;
        }

        try
        {
            var document = JsonSerializer.Deserialize<StoredDocument>(
                File.ReadAllText(_path),
                _jsonOptions);
            return document is { FormatVersion: CurrentFormatVersion } &&
                IsValid(document.Settings)
                    ? document.Settings
                    : null;
        }
        catch (Exception exception) when (exception is IOException or
            UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    internal void Save(StoredMainWindowSettings settings)
    {
        if (!IsValid(settings))
        {
            throw new ArgumentOutOfRangeException(nameof(settings));
        }

        var directory = Path.GetDirectoryName(_path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }
        var temporaryPath = _path + ".tmp";
        File.WriteAllText(
            temporaryPath,
            JsonSerializer.Serialize(
                new StoredDocument(CurrentFormatVersion, settings),
                _jsonOptions));
        File.Move(temporaryPath, _path, overwrite: true);
    }

    private static bool IsValid(StoredMainWindowSettings settings) =>
        Enum.IsDefined(settings.Mode) &&
        settings.WindowedWidth is >= 320 and <= 16384 &&
        settings.WindowedHeight is >= 240 and <= 16384;

    private sealed record StoredDocument(
        int FormatVersion,
        StoredMainWindowSettings Settings);
}
