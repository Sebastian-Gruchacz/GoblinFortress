using Godot;
using GoblinStronghold.Simulation.Localization;
using System.Text.Json;

namespace GoblinStronghold.GodotClient;

internal sealed class LocaleSettings
{
    private readonly string _path;

    internal LocaleSettings(
        string path,
        Func<string?>? platformLocaleProvider = null,
        Func<string?>? systemLocaleProvider = null)
    {
        _path = path;
        Locale = DetectAutomaticLocale(
            platformLocaleProvider ?? SteamLocaleProvider.TryGetCurrentGameLanguage,
            systemLocaleProvider ?? OS.GetLocale);
        Load();
    }

    internal string Locale { get; private set; }

    internal void Set(string locale)
    {
        Locale = TranslationCatalog.NormalizeLocale(locale);
        Save();
    }

    private static string DetectAutomaticLocale(
        Func<string?> platformLocaleProvider,
        Func<string?> systemLocaleProvider)
    {
        var platformLocale = TryReadLocale(platformLocaleProvider);
        if (!string.IsNullOrWhiteSpace(platformLocale))
        {
            return platformLocale.Trim().ToLowerInvariant() switch
            {
                "english" => "en",
                "polish" => "pl",
                _ => TranslationCatalog.NormalizeLocale(platformLocale),
            };
        }

        var locale = TryReadLocale(systemLocaleProvider);
        return string.IsNullOrWhiteSpace(locale)
            ? TranslationCatalog.FallbackLocale
            : TranslationCatalog.NormalizeLocale(locale);
    }

    private static string? TryReadLocale(Func<string?> provider)
    {
        try
        {
            return provider();
        }
        catch (Exception exception)
        {
            GD.PushWarning($"Could not detect automatic language: {exception.Message}");
            return null;
        }
    }

    private void Load()
    {
        if (!File.Exists(_path))
        {
            return;
        }

        try
        {
            var stored = JsonSerializer.Deserialize<StoredLocale>(File.ReadAllText(_path));
            if (!string.IsNullOrWhiteSpace(stored?.Locale))
            {
                Locale = TranslationCatalog.NormalizeLocale(stored.Locale);
            }
        }
        catch (Exception exception)
        {
            GD.PushWarning($"Could not load language settings: {exception.Message}");
        }
    }

    private void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            File.WriteAllText(
                _path,
                JsonSerializer.Serialize(
                    new StoredLocale(Locale),
                    new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception exception)
        {
            GD.PushWarning($"Could not save language settings: {exception.Message}");
        }
    }

    private sealed record StoredLocale(string Locale);
}
