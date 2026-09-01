using System.Globalization;

namespace GoblinStronghold.GodotClient.Application.Profiles;

internal static class GameProfileName
{
    internal const int MaximumLength = 64;

    internal static string CreateDefault(
        string? steamPlayerName,
        string? operatingSystemAccountName,
        string fallbackOwnerName,
        DateTimeOffset localNow)
    {
        var ownerName = FirstUsable(
            steamPlayerName,
            operatingSystemAccountName,
            fallbackOwnerName);
        var suffix = localNow.ToString("yyyyMMdd-HHmm", CultureInfo.InvariantCulture);
        var maximumOwnerLength = MaximumLength - suffix.Length - 1;
        if (ownerName.Length > maximumOwnerLength)
        {
            ownerName = ownerName[..maximumOwnerLength].TrimEnd();
        }
        return $"{ownerName} {suffix}";
    }

    internal static bool TryNormalize(string? value, out string normalized)
    {
        normalized = string.Concat((value ?? string.Empty).Where(character =>
            !char.IsControl(character))).Trim();
        if (normalized.Length is 0 or > MaximumLength)
        {
            normalized = string.Empty;
            return false;
        }
        return true;
    }

    private static string FirstUsable(params string?[] candidates)
    {
        foreach (var candidate in candidates)
        {
            var normalized = string.Concat((candidate ?? string.Empty).Where(character =>
                !char.IsControl(character))).Trim();
            if (normalized.Length > 0)
            {
                return normalized;
            }
        }
        throw new ArgumentException("At least one usable profile owner name is required.");
    }
}
