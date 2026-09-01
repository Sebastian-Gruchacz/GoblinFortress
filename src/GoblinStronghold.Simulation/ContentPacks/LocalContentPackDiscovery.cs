using System.Collections.ObjectModel;

namespace GoblinStronghold.Simulation.ContentPacks;

public sealed record ContentPackDiscoveryFailure(string FilePath, string Error);

public sealed record ContentPackDiscoveryResult(
    IReadOnlyList<ContentPack> Packs,
    IReadOnlyList<ContentPackDiscoveryFailure> Failures);

public static class LocalContentPackDiscovery
{
    private static readonly IReadOnlyDictionary<string, string?> SupportedExtensions =
        new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            [".goblang"] = "language",
            [".gobmod"] = "content",
            [".gobpack"] = null,
        };

    public static ContentPackDiscoveryResult Discover(
        string directoryPath,
        ContentPackLoadLimits? limits = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directoryPath);
        if (!Directory.Exists(directoryPath))
        {
            return EmptyResult();
        }

        var packs = new List<ContentPack>();
        var failures = new List<ContentPackDiscoveryFailure>();
        IEnumerable<string> candidates;
        try
        {
            candidates = Directory.EnumerateFiles(directoryPath, "*", SearchOption.TopDirectoryOnly)
                .Where(path => SupportedExtensions.ContainsKey(Path.GetExtension(path)))
                .Order(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            failures.Add(new ContentPackDiscoveryFailure(directoryPath, exception.Message));
            return ToResult(packs, failures);
        }

        foreach (var path in candidates)
        {
            try
            {
                using var stream = new FileStream(
                    path,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.Read);
                var pack = ContentPackArchiveLoader.Load(stream, path, limits);
                ValidateExtensionContract(path, pack);
                packs.Add(pack);
            }
            catch (Exception exception) when (
                exception is InvalidDataException or IOException or UnauthorizedAccessException)
            {
                failures.Add(new ContentPackDiscoveryFailure(path, exception.Message));
            }
        }

        return ToResult(packs, failures);
    }

    private static void ValidateExtensionContract(string path, ContentPack pack)
    {
        var extension = Path.GetExtension(path);
        var expectedType = SupportedExtensions[extension];
        if (expectedType is not null &&
            !string.Equals(pack.Manifest.Type, expectedType, StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                $"Package '{path}' uses extension '{extension}' but declares type " +
                $"'{pack.Manifest.Type}'.");
        }
        if (pack.Manifest.Type == "core")
        {
            throw new InvalidDataException(
                $"External package '{path}' cannot declare the reserved core type.");
        }
    }

    private static ContentPackDiscoveryResult EmptyResult() =>
        new(Array.Empty<ContentPack>(), Array.Empty<ContentPackDiscoveryFailure>());

    private static ContentPackDiscoveryResult ToResult(
        List<ContentPack> packs,
        List<ContentPackDiscoveryFailure> failures) =>
        new(
            new ReadOnlyCollection<ContentPack>(packs),
            new ReadOnlyCollection<ContentPackDiscoveryFailure>(failures));
}
