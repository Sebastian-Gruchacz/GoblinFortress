using System.Text;

namespace GoblinStronghold.GodotClient;

internal sealed class GameSaveStore(string directoryPath)
{
    private const int AutosaveSlotCount = 3;
    private static readonly UTF8Encoding Utf8WithoutBom = new(false);
    private readonly string _directoryPath = directoryPath;

    public string QuickSavePath => Path.Combine(_directoryPath, "quicksave.json");

    public bool HasAnySave => EnumerateSavePaths().Any(File.Exists);

    public void SaveQuick(string json) => WriteAtomic(QuickSavePath, json);

    public void SaveAuto(string json)
    {
        var paths = Enumerable.Range(1, AutosaveSlotCount)
            .Select(slot => Path.Combine(_directoryPath, $"autosave-{slot}.json"))
            .ToArray();
        var target = paths.FirstOrDefault(path => !File.Exists(path)) ??
            paths.OrderBy(File.GetLastWriteTimeUtc).First();
        WriteAtomic(target, json);
    }

    public IReadOnlyList<(string Path, string Json)> LoadPreferredFirst()
    {
        if (!Directory.Exists(_directoryPath))
        {
            return [];
        }

        var candidates = EnumerateSavePaths()
            .Where(File.Exists)
            .OrderBy(path => StringComparer.OrdinalIgnoreCase.Equals(path, QuickSavePath) ? 0 : 1)
            .ThenByDescending(File.GetLastWriteTimeUtc)
            .ToArray();
        var saves = new List<(string Path, string Json)>();
        foreach (var candidate in candidates)
        {
            try
            {
                saves.Add((candidate, File.ReadAllText(candidate, Encoding.UTF8)));
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // A damaged or temporarily unavailable slot must not hide older recovery points.
            }
        }

        return saves;
    }

    private IEnumerable<string> EnumerateSavePaths()
    {
        yield return QuickSavePath;
        for (var slot = 0; slot < AutosaveSlotCount; slot++)
        {
            yield return Path.Combine(_directoryPath, $"autosave-{slot + 1}.json");
        }
    }

    private static void WriteAtomic(string path, string json)
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
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }
}
