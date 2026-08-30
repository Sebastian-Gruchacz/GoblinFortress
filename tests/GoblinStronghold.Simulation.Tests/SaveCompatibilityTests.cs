using System.Text.Json.Nodes;
using GoblinStronghold.Simulation.Map;
using Xunit;

namespace GoblinStronghold.Simulation.Tests;

public sealed class SaveCompatibilityTests
{
    [Fact]
    public void CurrentBaselineRoundTripsWithoutMigration()
    {
        var source = CreateEngine();

        var restored = SimulationEngine.Load(
            source.Save(),
            SimulationDefinitions.Foundation);

        Assert.Equal(source.ComputeStateHash(), restored.ComputeStateHash());
        Assert.Equal(
            SimulationSaveFormat.CurrentVersion,
            JsonNode.Parse(restored.Save())!["formatVersion"]!.GetValue<int>());
    }

    [Theory]
    [InlineData(61)]
    [InlineData(63)]
    public void NonBaselineFormatIsRejected(int formatVersion)
    {
        var save = JsonNode.Parse(CreateEngine().Save())!.AsObject();
        save["formatVersion"] = formatVersion;

        var exception = Assert.Throws<InvalidDataException>(() =>
            SimulationEngine.Load(
                save.ToJsonString(),
                SimulationDefinitions.Foundation));

        Assert.Contains("obsolete or incompatible", exception.Message);
        Assert.Contains(SimulationSaveFormat.CurrentVersion.ToString(), exception.Message);
    }

    [Fact]
    public void CurrentSaveProjectsLegacySurfaceGroundStackOntoMaterialSurface()
    {
        var seed = new WorldSeed(0x47524F554E445AUL);
        var map = SwampMapGenerator.Generate(seed, width: 48, height: 48);
        var source = SimulationEngine.Create(
            seed,
            SimulationDefinitions.Foundation,
            map,
            initialGoblinCount: 1,
            initialFoodStock: 1);
        var materialSurface = Enumerable.Range(0, map.Height)
            .SelectMany(y => Enumerable.Range(0, map.Width)
                .Select(x => map.GetTerrainSurfacePosition(new GridPosition(x, y, 0))))
            .First(position => position.Z != 0 &&
                map.TryGetInitialGeometry(position, out var geometry) &&
                geometry.Support == CellSupportKind.NaturalFlat &&
                geometry.FluidDepthLevels == 0);
        var save = JsonNode.Parse(source.Save())!.AsObject();
        var stack = save["itemStacks"]!.AsArray().Single()!.AsObject();
        stack["x"] = materialSurface.X;
        stack["y"] = materialSurface.Y;
        stack["z"] = 0;

        var restored = SimulationEngine.Load(
            save.ToJsonString(),
            SimulationDefinitions.Foundation);

        Assert.Equal(
            materialSurface,
            Assert.Single(restored.CreateSnapshot().ItemStacks).Location.Position);
    }

    private static SimulationEngine CreateEngine() => SimulationEngine.Create(
        new WorldSeed(0x5341564542415345UL),
        SimulationDefinitions.Foundation,
        initialGoblinCount: 4,
        initialFoodStock: 40);
}
