using System.Text;
using GoblinStronghold.GodotClient;
using Xunit;

namespace GoblinStronghold.Simulation.Tests;

public sealed class GameSaveStoreTests
{
    [Fact]
    public void ObsoleteAndDamagedSlotsDoNotEnableContinue()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            $"goblin-stronghold-save-store-{Guid.NewGuid():N}");
        try
        {
            var store = CreateStore(directory);
            Directory.CreateDirectory(directory);
            File.WriteAllText(
                store.QuickSavePath,
                "{\"formatVersion\":61,\"worldSeed\":7,\"currentTick\":100}",
                Encoding.UTF8);
            File.WriteAllText(
                Path.Combine(directory, "autosave-1.json"),
                "{damaged",
                Encoding.UTF8);

            Assert.False(store.HasAnySave);
            Assert.Empty(store.LoadLatestProgressFirst());
            Assert.Empty(store.InspectCandidates());
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [Fact]
    public void HighestTickWithinMostRecentlyWrittenWorldWins()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            $"goblin-stronghold-save-store-{Guid.NewGuid():N}");
        try
        {
            var store = CreateStore(directory);
            store.SaveQuick(CreateSaveHeader(
                worldSeed: 7,
                currentTick: 100,
                lowestSavedZ: -2));
            store.SaveAuto(CreateSaveHeader(
                worldSeed: 7,
                currentTick: 150,
                lowestSavedZ: -3));
            var autosavePath = Path.Combine(directory, "autosave-1.json");
            var oldTime = new DateTime(2030, 1, 1, 12, 0, 0, DateTimeKind.Utc);
            var newTime = oldTime.AddMinutes(1);
            File.SetLastWriteTimeUtc(autosavePath, oldTime);
            File.SetLastWriteTimeUtc(store.QuickSavePath, newTime);

            var candidates = store.LoadLatestProgressFirst();

            Assert.Contains("150", candidates[0].Json);
            Assert.Contains("100", candidates[1].Json);
            var summaries = store.InspectCandidates();
            Assert.Equal(150, summaries[0].CurrentTick);
            Assert.Equal(-3, summaries[0].LowestSavedZ);
            Assert.Equal(7UL, summaries[0].WorldSeed);
            Assert.True(summaries[0].HasReadableHeader);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [Fact]
    public void MostRecentlyWrittenWorldWinsOverOlderWorldWithHigherTick()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            $"goblin-stronghold-save-store-{Guid.NewGuid():N}");
        try
        {
            var store = CreateStore(directory);
            store.SaveAuto(CreateSaveHeader(worldSeed: 7, currentTick: 900));
            store.SaveQuick(CreateSaveHeader(worldSeed: 8, currentTick: 5));
            var autosavePath = Path.Combine(directory, "autosave-1.json");
            var oldTime = new DateTime(2030, 1, 1, 12, 0, 0, DateTimeKind.Utc);
            File.SetLastWriteTimeUtc(autosavePath, oldTime);
            File.SetLastWriteTimeUtc(store.QuickSavePath, oldTime.AddMinutes(1));

            var candidates = store.LoadLatestProgressFirst();

            Assert.Contains("\"worldSeed\":8", candidates[0].Json);
            Assert.Contains("\"worldSeed\":7", candidates[1].Json);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [Fact]
    public void SecondManualSaveIsVerifiedAndPreservesPreviousQuickSave()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            $"goblin-stronghold-save-store-{Guid.NewGuid():N}");
        try
        {
            var store = CreateStore(directory);
            var firstJson = CreateSaveHeader(worldSeed: 7, currentTick: 100);
            var secondJson = CreateSaveHeader(worldSeed: 7, currentTick: 200);

            var first = store.SaveQuick(firstJson);
            var second = store.SaveQuick(secondJson);

            Assert.False(first.BackupCreated);
            Assert.True(second.BackupCreated);
            Assert.Equal(Encoding.UTF8.GetByteCount(secondJson), second.ByteCount);
            Assert.Equal(secondJson, File.ReadAllText(store.QuickSavePath, Encoding.UTF8));
            Assert.Equal(firstJson, File.ReadAllText(
                store.QuickSaveBackupPath,
                Encoding.UTF8));
            var candidates = store.LoadLatestProgressFirst();
            Assert.Equal(store.QuickSavePath, candidates[0].Path);
            Assert.Equal(store.QuickSaveBackupPath, candidates[1].Path);

            File.WriteAllText(store.QuickSavePath, "{damaged", Encoding.UTF8);
            candidates = store.LoadLatestProgressFirst();

            Assert.Equal(store.QuickSaveBackupPath, candidates[0].Path);
            Assert.Equal(firstJson, candidates[0].Json);
            var summaries = store.InspectCandidates();
            Assert.Equal(store.QuickSaveBackupPath, summaries[0].Path);
            Assert.True(summaries[0].HasReadableHeader);
            Assert.Single(summaries);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [Fact]
    public void PreLoadRecoveryIsVerifiedAndParticipatesInCandidateSelection()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            $"goblin-stronghold-save-store-{Guid.NewGuid():N}");
        try
        {
            var store = CreateStore(directory);
            var json = CreateSaveHeader(
                worldSeed: 17,
                currentTick: 321,
                lowestSavedZ: -4);

            var receipt = store.SaveBeforeLoad(json);

            Assert.Equal(store.PreLoadRecoveryPath, receipt.Path);
            Assert.False(receipt.BackupCreated);
            Assert.Equal(Encoding.UTF8.GetByteCount(json), receipt.ByteCount);
            Assert.Equal(json, File.ReadAllText(store.PreLoadRecoveryPath, Encoding.UTF8));
            var summary = Assert.Single(store.InspectCandidates());
            Assert.Equal(store.PreLoadRecoveryPath, summary.Path);
            Assert.Equal(321, summary.CurrentTick);
            Assert.Equal(-4, summary.LowestSavedZ);

            var nextJson = CreateSaveHeader(worldSeed: 17, currentTick: 400);
            var alternate = store.SaveBeforeLoad(
                nextJson,
                excludedPath: store.PreLoadRecoveryPath);

            Assert.Equal(store.AlternatePreLoadRecoveryPath, alternate.Path);
            Assert.Equal(json, File.ReadAllText(store.PreLoadRecoveryPath, Encoding.UTF8));
            Assert.Equal(nextJson, File.ReadAllText(
                store.AlternatePreLoadRecoveryPath,
                Encoding.UTF8));
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    private static string CreateSaveHeader(
        ulong worldSeed,
        long currentTick,
        int lowestSavedZ = 0) =>
        $$"""{"formatVersion":{{SimulationSaveFormat.CurrentVersion}},"worldSeed":{{worldSeed}},"currentTick":{{currentTick}},"excavatedCaveCells":[{"z":{{lowestSavedZ}}}],"excavatedVerticalPassages":[]}""";

    private static GameSaveStore CreateStore(string directory) => new(
        directory,
        SimulationSaveFormat.CurrentVersion);
}
