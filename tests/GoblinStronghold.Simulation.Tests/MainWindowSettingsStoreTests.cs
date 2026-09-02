using GoblinStronghold.GodotClient.Application.Profiles;
using Xunit;

namespace GoblinStronghold.Simulation.Tests;

public sealed class MainWindowSettingsStoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        "GoblinStronghold-display-tests-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void SettingsRoundTripWithNamedModes()
    {
        foreach (var mode in Enum.GetValues<StoredMainWindowMode>())
        {
            var path = Path.Combine(_directory, $"display-{mode}.json");
            var store = new MainWindowSettingsStore(path);
            var expected = new StoredMainWindowSettings(mode, 1440, 900);

            store.Save(expected);

            Assert.Equal(expected, store.Load());
            Assert.Contains($"\"{mode}\"", File.ReadAllText(path));
        }
    }

    [Fact]
    public void MissingSettingsReturnNoOverride()
    {
        var store = new MainWindowSettingsStore(Path.Combine(_directory, "missing.json"));

        Assert.Null(store.Load());
    }

    [Fact]
    public void InvalidDimensionsAreRejectedBeforeWriting()
    {
        var store = new MainWindowSettingsStore(Path.Combine(_directory, "display.json"));

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            store.Save(new StoredMainWindowSettings(
                StoredMainWindowMode.Windowed,
                0,
                900)));
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }
}
