using System.Text.RegularExpressions;

namespace GoblinStronghold.Simulation.ContentPacks;

public readonly partial record struct ContentId
{
    public const string CoreNamespace = "core";

    private ContentId(string packageId, string localId)
    {
        PackageId = packageId;
        LocalId = localId;
    }

    public string PackageId { get; }
    public string LocalId { get; }
    public string Value => $"{PackageId}:{LocalId}";

    public static ContentId Parse(string value, string defaultPackageId = CoreNamespace)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        ArgumentException.ThrowIfNullOrWhiteSpace(defaultPackageId);

        var separator = value.IndexOf(':');
        var packageId = separator < 0 ? defaultPackageId : value[..separator];
        var localId = separator < 0 ? value : value[(separator + 1)..];
        if (!PackageIdPattern().IsMatch(packageId) || !LocalIdPattern().IsMatch(localId))
        {
            throw new FormatException(
                $"Content ID '{value}' must use the lowercase 'package:item' format.");
        }

        return new ContentId(packageId, localId);
    }

    public static bool TryParse(
        string? value,
        out ContentId contentId,
        string defaultPackageId = CoreNamespace)
    {
        try
        {
            contentId = Parse(value!, defaultPackageId);
            return true;
        }
        catch (Exception exception) when (
            exception is ArgumentException or FormatException)
        {
            contentId = default;
            return false;
        }
    }

    public override string ToString() => Value;

    [GeneratedRegex(
        "^[a-z0-9][a-z0-9._-]{0,127}$",
        RegexOptions.CultureInvariant)]
    private static partial Regex PackageIdPattern();

    [GeneratedRegex(
        "^[a-z0-9][a-z0-9._-]{0,127}$",
        RegexOptions.CultureInvariant)]
    private static partial Regex LocalIdPattern();
}
