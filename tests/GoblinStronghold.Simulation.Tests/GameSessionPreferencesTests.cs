using System.Text.Json;
using GoblinStronghold.GodotClient;
using GoblinStronghold.Simulation.Resources;
using Xunit;

namespace GoblinStronghold.Simulation.Tests;

public sealed class GameSessionPreferencesTests
{
    [Fact]
    public void ConstructionMaterialChoicesRoundTripInsideGameSave()
    {
        var preferences = new GameSessionPreferences();
        preferences.SetConstructionMaterial("Wall", ResourceVariant.Granite);
        preferences.SetConstructionMaterial("Door", ResourceVariant.OakWood);

        var json = preferences.AddToSave("{\"formatVersion\":1,\"currentTick\":17}");
        var restored = GameSessionPreferences.FromSave(json);

        Assert.True(restored.TryGetConstructionMaterial("wall", out var wall));
        Assert.Equal(ResourceVariant.Granite, wall);
        Assert.True(restored.TryGetConstructionMaterial("Door", out var door));
        Assert.Equal(ResourceVariant.OakWood, door);
        using var document = JsonDocument.Parse(json);
        Assert.Equal(17, document.RootElement.GetProperty("currentTick").GetInt32());
    }

    [Fact]
    public void OldSaveWithoutClientPreferencesUsesEmptyDefaults()
    {
        var preferences = GameSessionPreferences.FromSave("{\"formatVersion\":1}");

        Assert.False(preferences.TryGetConstructionMaterial("Wall", out _));
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
        var preferences = new GameSessionPreferences();
        preferences.SetConstructionMaterial("Floor", ResourceVariant.Sandstone);

        var restored = SimulationEngine.Load(
            preferences.AddToSave(engine.Save()),
            SimulationDefinitions.Foundation);

        Assert.Equal(engine.ComputeStateHash(), restored.ComputeStateHash());
    }
}
