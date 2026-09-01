using Godot;

namespace GoblinStronghold.GodotClient.Platform.Steam;

internal static class SteamPlayerIdentityProvider
{
    private static readonly StringName[] PersonaNameMethods =
    [
        "getPersonaName",
        "get_persona_name",
    ];

    internal static string? TryGetPersonaName()
    {
        if (!Engine.HasSingleton("Steam"))
        {
            return null;
        }

        var steam = Engine.GetSingleton("Steam");
        foreach (var method in PersonaNameMethods)
        {
            if (!steam.HasMethod(method))
            {
                continue;
            }

            var personaName = steam.Call(method).AsString();
            return string.IsNullOrWhiteSpace(personaName) ? null : personaName;
        }
        return null;
    }
}
