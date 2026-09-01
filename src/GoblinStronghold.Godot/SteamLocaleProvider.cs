using Godot;

namespace GoblinStronghold.GodotClient;

internal static class SteamLocaleProvider
{
    private static readonly StringName[] LanguageMethods =
    [
        "getCurrentGameLanguage",
        "get_current_game_language",
    ];

    internal static string? TryGetCurrentGameLanguage()
    {
        if (!Engine.HasSingleton("Steam"))
        {
            return null;
        }

        var steam = Engine.GetSingleton("Steam");
        foreach (var method in LanguageMethods)
        {
            if (!steam.HasMethod(method))
            {
                continue;
            }

            var language = steam.Call(method).AsString();
            return string.IsNullOrWhiteSpace(language) ? null : language;
        }

        return null;
    }
}
