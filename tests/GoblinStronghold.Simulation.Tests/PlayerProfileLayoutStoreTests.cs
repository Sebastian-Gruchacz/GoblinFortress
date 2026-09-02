using GoblinStronghold.GodotClient.Application.Profiles;
using Xunit;

namespace GoblinStronghold.Simulation.Tests;

public sealed class PlayerProfileLayoutStoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        "GoblinStronghold-layout-tests-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void LayoutRoundTripsIndependentlyForEachProfile()
    {
        var store = new PlayerProfileLayoutStore(_directory);
        store.Save("First tribe", new Dictionary<string, StoredWindowLayout>
        {
            ["PlannerWindow"] = new(12, 34, 640, 480),
        });
        store.Save("Second tribe", new Dictionary<string, StoredWindowLayout>
        {
            ["PlannerWindow"] = new(56, 78, 800, 600),
        });

        Assert.Equal(
            new StoredWindowLayout(12, 34, 640, 480),
            store.Load("First tribe")["PlannerWindow"]);
        Assert.Equal(
            new StoredWindowLayout(56, 78, 800, 600),
            store.Load("Second tribe")["PlannerWindow"]);
    }

    [Fact]
    public void ProfileFileNameIsAStableHashRatherThanPlayerFacingText()
    {
        var store = new PlayerProfileLayoutStore(_directory);

        var first = store.GetProfilePath("My Goblin Profile");
        var second = store.GetProfilePath("My Goblin Profile");

        Assert.Equal(first, second);
        Assert.DoesNotContain("My Goblin Profile", first, StringComparison.OrdinalIgnoreCase);
        Assert.Matches("^[0-9a-f]{64}\\.json$", Path.GetFileName(first));
    }

    [Fact]
    public void InvalidStoredWindowEntriesAreIgnored()
    {
        var store = new PlayerProfileLayoutStore(_directory);
        store.Save("Profile", new Dictionary<string, StoredWindowLayout>
        {
            ["ValidWindow"] = new(1, 2, 300, 200),
            ["InvalidWindow"] = new(1, 2, 0, 200),
        });

        var layouts = store.Load("Profile");

        Assert.Single(layouts);
        Assert.True(layouts.ContainsKey("ValidWindow"));
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }
}
