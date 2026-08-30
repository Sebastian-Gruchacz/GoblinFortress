using System.Text;
using System.Text.Json;

namespace GoblinStronghold.GodotClient;

internal sealed class GameSaveStore(string directoryPath, int supportedFormatVersion)
{
    private const int AutosaveSlotCount = 3;
    private static readonly UTF8Encoding Utf8WithoutBom = new(false);
    private readonly string _directoryPath = directoryPath;
    private readonly int _supportedFormatVersion = supportedFormatVersion > 0
        ? supportedFormatVersion
        : throw new ArgumentOutOfRangeException(nameof(supportedFormatVersion));

    public string QuickSavePath => Path.Combine(_directoryPath, "quicksave.json");

    public string QuickSaveBackupPath => Path.Combine(_directoryPath, "quicksave-backup.json");

    public string PreLoadRecoveryPath => Path.Combine(_directoryPath, "session-before-load.json");

    public string AlternatePreLoadRecoveryPath =>
        Path.Combine(_directoryPath, "session-before-load-backup.json");

    public bool HasAnySave => ReadCandidatesInRecoveryOrder().Count > 0;

    public GameSaveReceipt SaveQuick(string json)
    {
        var backupCreated = File.Exists(QuickSavePath);
        if (backupCreated)
        {
            CopyAtomic(QuickSavePath, QuickSaveBackupPath);
        }

        var receipt = WriteAtomic(QuickSavePath, json);
        return receipt with { BackupCreated = backupCreated };
    }

    public void SaveAuto(string json)
    {
        var paths = Enumerable.Range(1, AutosaveSlotCount)
            .Select(slot => Path.Combine(_directoryPath, $"autosave-{slot}.json"))
            .ToArray();
        var target = paths.FirstOrDefault(path => !File.Exists(path)) ??
            paths.OrderBy(File.GetLastWriteTimeUtc).First();
        WriteAtomic(target, json);
    }

    public GameSaveReceipt SaveBeforeLoad(string json, string? excludedPath = null)
    {
        var paths = new[] { PreLoadRecoveryPath, AlternatePreLoadRecoveryPath }
            .Where(path => !StringComparer.OrdinalIgnoreCase.Equals(path, excludedPath))
            .ToArray();
        var target = paths.FirstOrDefault(path => !File.Exists(path)) ??
            paths.OrderBy(File.GetLastWriteTimeUtc).First();
        return WriteAtomic(target, json);
    }

    public IReadOnlyList<(string Path, string Json)> LoadLatestProgressFirst()
        => ReadCandidatesInRecoveryOrder()
            .Select(candidate => (candidate.Path, candidate.Json))
            .ToArray();

    public IReadOnlyList<GameSaveSummary> InspectCandidates()
        => ReadCandidatesInRecoveryOrder()
            .Select(candidate => new GameSaveSummary(
                candidate.Path,
                candidate.LastWriteTimeUtc,
                candidate.WorldSeed,
                candidate.CurrentTick,
                candidate.LowestSavedZ))
            .ToArray();

    private IReadOnlyList<SaveCandidate> ReadCandidatesInRecoveryOrder()
    {
        if (!Directory.Exists(_directoryPath))
        {
            return [];
        }

        var candidates = new List<SaveCandidate>();
        foreach (var path in EnumerateSavePaths().Where(File.Exists))
        {
            try
            {
                var json = File.ReadAllText(path, Encoding.UTF8);
                var progress = TryReadProgress(json);
                if (progress is null)
                {
                    continue;
                }
                candidates.Add(new SaveCandidate(
                    path,
                    json,
                    File.GetLastWriteTimeUtc(path),
                    progress.Value.WorldSeed,
                    progress.Value.CurrentTick,
                    progress.Value.LowestSavedZ));
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // A damaged or temporarily unavailable slot must not hide older recovery points.
            }
        }

        var newestKnownWorld = candidates
            .Where(candidate => candidate.WorldSeed.HasValue)
            .OrderByDescending(candidate => candidate.LastWriteTimeUtc)
            .FirstOrDefault();
        if (newestKnownWorld is null)
        {
            return candidates
                .OrderByDescending(candidate => candidate.LastWriteTimeUtc)
                .ThenBy(candidate => IsQuickSave(candidate.Path) ? 0 : 1)
                .ToArray();
        }

        var activeWorldSeed = newestKnownWorld.WorldSeed!.Value;
        return candidates
            .OrderBy(candidate => candidate.WorldSeed == activeWorldSeed ? 0 : 1)
            .ThenByDescending(candidate => candidate.WorldSeed == activeWorldSeed
                ? candidate.CurrentTick
                : null)
            .ThenByDescending(candidate => candidate.LastWriteTimeUtc)
            .ThenBy(candidate => IsQuickSave(candidate.Path) ? 0 : 1)
            .ToArray();
    }

    private bool IsQuickSave(string path) =>
        StringComparer.OrdinalIgnoreCase.Equals(path, QuickSavePath);

    private SaveProgress? TryReadProgress(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.TryGetProperty("formatVersion", out var formatVersion) &&
                formatVersion.TryGetInt32(out var version) &&
                version == _supportedFormatVersion &&
                root.TryGetProperty("worldSeed", out var worldSeed) &&
                root.TryGetProperty("currentTick", out var currentTick) &&
                worldSeed.TryGetUInt64(out var seed) &&
                currentTick.TryGetInt64(out var tick))
            {
                var lowestSavedZ = 0;
                if (root.TryGetProperty("excavatedCaveCells", out var excavatedCells) &&
                    excavatedCells.ValueKind == JsonValueKind.Array)
                {
                    foreach (var cell in excavatedCells.EnumerateArray())
                    {
                        if (cell.TryGetProperty("z", out var z) && z.TryGetInt32(out var level))
                        {
                            lowestSavedZ = Math.Min(lowestSavedZ, level);
                        }
                    }
                }
                if (root.TryGetProperty(
                        "excavatedVerticalPassages",
                        out var excavatedPassages) &&
                    excavatedPassages.ValueKind == JsonValueKind.Array)
                {
                    foreach (var passage in excavatedPassages.EnumerateArray())
                    {
                        foreach (var property in new[] { "upperZ", "lowerZ" })
                        {
                            if (passage.TryGetProperty(property, out var z) &&
                                z.TryGetInt32(out var level))
                            {
                                lowestSavedZ = Math.Min(lowestSavedZ, level);
                            }
                        }
                    }
                }

                return new SaveProgress(seed, tick, lowestSavedZ);
            }
        }
        catch (JsonException)
        {
            // Damaged slots are ignored; a valid recovery point may still remain.
        }

        return null;
    }

    private IEnumerable<string> EnumerateSavePaths()
    {
        yield return QuickSavePath;
        yield return QuickSaveBackupPath;
        yield return PreLoadRecoveryPath;
        yield return AlternatePreLoadRecoveryPath;
        for (var slot = 0; slot < AutosaveSlotCount; slot++)
        {
            yield return Path.Combine(_directoryPath, $"autosave-{slot + 1}.json");
        }
    }

    private static GameSaveReceipt WriteAtomic(string path, string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        var directory = Path.GetDirectoryName(path)
            ?? throw new InvalidOperationException("A save path must have a directory.");
        Directory.CreateDirectory(directory);
        var temporaryPath = Path.Combine(directory, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllText(temporaryPath, json, Utf8WithoutBom);
            File.Move(temporaryPath, path, overwrite: true);
            File.SetLastWriteTimeUtc(path, DateTime.UtcNow);
            var verified = File.ReadAllText(path, Encoding.UTF8);
            if (!StringComparer.Ordinal.Equals(json, verified))
            {
                throw new IOException($"Save verification failed for {path}.");
            }

            return new GameSaveReceipt(
                path,
                Utf8WithoutBom.GetByteCount(verified),
                BackupCreated: false);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private static void CopyAtomic(string sourcePath, string destinationPath)
    {
        var directory = Path.GetDirectoryName(destinationPath)
            ?? throw new InvalidOperationException("A save path must have a directory.");
        Directory.CreateDirectory(directory);
        var temporaryPath = Path.Combine(
            directory,
            $".{Path.GetFileName(destinationPath)}.{Guid.NewGuid():N}.tmp");
        var sourceWriteTime = File.GetLastWriteTimeUtc(sourcePath);
        try
        {
            File.Copy(sourcePath, temporaryPath, overwrite: true);
            File.Move(temporaryPath, destinationPath, overwrite: true);
            File.SetLastWriteTimeUtc(destinationPath, sourceWriteTime);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private sealed record SaveCandidate(
        string Path,
        string Json,
        DateTime LastWriteTimeUtc,
        ulong? WorldSeed,
        long? CurrentTick,
        int? LowestSavedZ);

    private readonly record struct SaveProgress(
        ulong WorldSeed,
        long CurrentTick,
        int LowestSavedZ);
}

internal readonly record struct GameSaveReceipt(
    string Path,
    int ByteCount,
    bool BackupCreated);

internal readonly record struct GameSaveSummary(
    string Path,
    DateTime LastWriteTimeUtc,
    ulong? WorldSeed,
    long? CurrentTick,
    int? LowestSavedZ)
{
    public bool HasReadableHeader => WorldSeed.HasValue && CurrentTick.HasValue;
}
