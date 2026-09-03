using System.Text.Json;
using GoblinStronghold.GodotClient;
using GoblinStronghold.GodotClient.Application.Profiles;
using GoblinStronghold.Simulation.Resources;
using Xunit;

namespace GoblinStronghold.Simulation.Tests;

public sealed class GameSessionPreferencesTests
{
    [Fact]
    public void ConstructionMaterialChoicesRoundTripInsideGameSave()
    {
        var preferences = new GameSessionPreferences("Lo 20260901-2245", visibleLevel: -2);
        preferences.SetConstructionMaterial("Wall", ResourceVariant.Granite);
        preferences.SetConstructionMaterial("Door", ResourceVariant.OakWood);

        var json = preferences.AddToSave("{\"formatVersion\":1,\"currentTick\":17}");
        var restored = GameSessionPreferences.FromSave(json);

        Assert.True(restored.TryGetConstructionMaterial("wall", out var wall));
        Assert.Equal(ResourceVariant.Granite, wall);
        Assert.True(restored.TryGetConstructionMaterial("Door", out var door));
        Assert.Equal(ResourceVariant.OakWood, door);
        Assert.Equal("Lo 20260901-2245", restored.ProfileName);
        Assert.Equal(-2, restored.VisibleLevel);
        using var document = JsonDocument.Parse(json);
        Assert.Equal(17, document.RootElement.GetProperty("currentTick").GetInt32());
    }

    [Fact]
    public void OldSaveWithoutClientPreferencesUsesEmptyDefaults()
    {
        var preferences = GameSessionPreferences.FromSave("{\"formatVersion\":1}");

        Assert.False(preferences.TryGetConstructionMaterial("Wall", out _));
        Assert.Empty(preferences.ProfileName);
        Assert.Equal(0, preferences.VisibleLevel);
    }

    [Fact]
    public void InvalidSavedVisibleLevelUsesSurfaceDefault()
    {
        var preferences = GameSessionPreferences.FromSave(
            "{\"clientPreferences\":{\"visibleLevel\":\"underground\"}}");

        Assert.Equal(0, preferences.VisibleLevel);
    }

    [Fact]
    public void DefaultProfileNamePrefersSteamAndIncludesLocalTimestamp()
    {
        var timestamp = new DateTimeOffset(2026, 9, 1, 22, 45, 0, TimeSpan.FromHours(2));

        Assert.Equal(
            "Steam Goblin 20260901-2245",
            GameProfileName.CreateDefault(" Steam Goblin ", "windows-user", "Player", timestamp));
        Assert.Equal(
            "windows-user 20260901-2245",
            GameProfileName.CreateDefault(null, "windows-user", "Player", timestamp));
    }

    [Fact]
    public void SimulationLoaderIgnoresClientPreferencesEnvelope()
    {
        var engine = SimulationEngine.Create(
            new WorldSeed(0x505245464552454EUL),
            SimulationDefinitions.Foundation,
            initialGoblinCount: 1,
            initialFoodStock: 0,
            initialWoodStock: 0);
        var preferences = new GameSessionPreferences("Simulation-independent profile");
        preferences.SetConstructionMaterial("Floor", ResourceVariant.Sandstone);

        var restored = SimulationEngine.Load(
            preferences.AddToSave(engine.Save()),
            SimulationDefinitions.Foundation);

        Assert.Equal(engine.ComputeStateHash(), restored.ComputeStateHash());
    }
}
