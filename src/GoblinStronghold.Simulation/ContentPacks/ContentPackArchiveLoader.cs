using System.IO.Compression;

namespace GoblinStronghold.Simulation.ContentPacks;

public sealed record ContentPackLoadLimits(
    int MaximumFileCount = 4096,
    long MaximumSingleFileBytes = 64 * 1024 * 1024,
    long MaximumTotalBytes = 256 * 1024 * 1024,
    long MaximumArchiveBytes = 128 * 1024 * 1024);

public static class ContentPackArchiveLoader
{
    public static ContentPack Load(
        Stream packageStream,
        string sourceName,
        ContentPackLoadLimits? limits = null)
    {
        ArgumentNullException.ThrowIfNull(packageStream);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceName);
        limits ??= new ContentPackLoadLimits();
        ValidateLimits(limits);
        if (!packageStream.CanSeek)
        {
            throw new ArgumentException(
                "Content pack streams must be seekable so archive size can be validated.",
                nameof(packageStream));
        }
        if (packageStream.Length - packageStream.Position > limits.MaximumArchiveBytes)
        {
            throw new InvalidDataException(
                $"Content pack '{sourceName}' exceeds the archive-size limit.");
        }

        using var archive = new ZipArchive(
            packageStream,
            ZipArchiveMode.Read,
            leaveOpen: true);
        var files = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        long totalBytes = 0;
        foreach (var entry in archive.Entries)
        {
            if (entry.FullName.EndsWith("/", StringComparison.Ordinal))
            {
                continue;
            }
            if (files.Count >= limits.MaximumFileCount)
            {
                throw new InvalidDataException(
                    $"Content pack '{sourceName}' exceeds the file-count limit.");
            }

            var path = ContentPack.ValidatePackagePath(entry.FullName);
            if (entry.Length > limits.MaximumSingleFileBytes ||
                totalBytes > limits.MaximumTotalBytes - entry.Length)
            {
                throw new InvalidDataException(
                    $"Content pack '{sourceName}' exceeds the expanded-size limit.");
            }

            using var entryStream = entry.Open();
            using var buffer = new MemoryStream((int)entry.Length);
            entryStream.CopyTo(buffer);
            if (buffer.Length != entry.Length || !files.TryAdd(path, buffer.ToArray()))
            {
                throw new InvalidDataException(
                    $"Content pack '{sourceName}' contains an invalid or duplicate file '{path}'.");
            }
            totalBytes += entry.Length;
        }

        return ContentPack.Create(sourceName, files);
    }

    private static void ValidateLimits(ContentPackLoadLimits limits)
    {
        if (limits.MaximumFileCount < 1 ||
            limits.MaximumSingleFileBytes < 1 ||
            limits.MaximumSingleFileBytes > int.MaxValue ||
            limits.MaximumTotalBytes < limits.MaximumSingleFileBytes ||
            limits.MaximumArchiveBytes < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(limits),
                "Content pack limits must be positive, internally consistent, and memory-safe.");
        }
    }
}
