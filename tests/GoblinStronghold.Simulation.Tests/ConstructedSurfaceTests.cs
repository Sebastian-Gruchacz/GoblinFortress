using GoblinStronghold.Simulation.Map;
using GoblinStronghold.Simulation.Resources;
using Xunit;

namespace GoblinStronghold.Simulation.Tests;

public sealed class ConstructedSurfaceTests
{
    [Fact]
    public void FloorBuiltOverVoidBecomesTraversableSurface()
    {
        var engine = CreateEngine(initialWoodStock: 0);
        var position = FindBuildableVoidBesideTerrain(engine);

        Assert.False(engine.World.IsTerrainTraversable(position));
        engine.World.BuildFloor(
            position,
            new SimulationTick(1),
            stone: false,
            ResourceVariant.OakWood);

        Assert.True(engine.World.IsTerrainTraversable(position));
        Assert.Contains(engine.World.GetWorldObjectsAt(position), worldObject =>
            worldObject.Kind == WorldObjectKind.WoodenFloor);
        Assert.Contains(engine.World.GetTerrainNeighbors(position), neighbor =>
            engine.World.IsTerrainTraversable(neighbor));
    }

    [Fact]
    public void ConstructedRampConnectsLowerAndUpperLevelsAndSurvivesSaveLoad()
    {
        var engine = CreateEngine(initialWoodStock: 0);
        var (lower, upper) = FindRampPlacement(engine);

        engine.World.BuildRamp(
            lower,
            upper,
            new SimulationTick(1),
            stone: false,
            ResourceVariant.OakWood);

        Assert.Contains(upper, engine.World.GetTerrainNeighbors(lower));
        Assert.Contains(lower, engine.World.GetTerrainNeighbors(upper));
        var ramp = Assert.Single(engine.World.GetWorldObjectsAt(lower), worldObject =>
            worldObject.Kind == WorldObjectKind.WoodenRamp);
        Assert.Equal(ResourceVariant.OakWood, ramp.MaterialVariant);

        var restored = SimulationEngine.Load(
            engine.Save(),
            SimulationDefinitions.Foundation);
        Assert.Contains(upper, restored.World.GetTerrainNeighbors(lower));
        Assert.Contains(lower, restored.World.GetTerrainNeighbors(upper));
    }

    [Fact]
    public void RampConstructionCommandKeepsInferredEndpointAndMaterial()
    {
        var engine = CreateEngine(initialWoodStock: 2);
        var (lower, upper) = FindRampPlacement(engine);
        engine.QueueCommand(SimulationCommand.BuildWoodenRamp(
            new SimulationTick(1),
            sequence: 1,
            lower,
            upper,
            ResourceVariant.OakWood));

        engine.AdvanceTicks(1);

        var site = Assert.Single(engine.CreateSnapshot().ConstructionSites);
        Assert.Equal(ConstructionKind.WoodenRamp, site.Kind);
        Assert.Equal(lower, site.Anchor);
        Assert.Equal(upper, site.End);
        var material = Assert.Single(site.Materials);
        Assert.Equal(ResourceKind.Wood, material.Resource);
        Assert.Equal(ResourceVariant.OakWood, material.Variant);
        Assert.Equal(2, material.RequiredQuantity);
    }

    private static SimulationEngine CreateEngine(int initialWoodStock) =>
        SimulationEngine.Create(
            new WorldSeed(0x5355524641434553UL),
            SimulationDefinitions.Foundation,
            initialGoblinCount: 1,
            initialFoodStock: 0,
            initialWoodStock: initialWoodStock);

    private static GridPosition FindBuildableVoidBesideTerrain(SimulationEngine engine) =>
        EnumerateWorldPositions(engine)
            .First(position =>
                engine.Map.TryGetInitialGeometry(position, out var geometry) &&
                !geometry.IsSolid &&
                geometry.Support == CellSupportKind.None &&
                geometry.Fluid == CellFluidKind.None &&
                engine.World.CanBuildFloors([position]) &&
                engine.World.GetCardinalWorldNeighbors(position)
                    .Any(engine.World.IsTerrainTraversable));

    private static (GridPosition Lower, GridPosition Upper) FindRampPlacement(
        SimulationEngine engine)
    {
        foreach (var lower in EnumerateWorldPositions(engine))
        {
            if (engine.World.TryInferBuildRamp(lower, out var upper))
            {
                return (lower, upper);
            }
        }

        throw new InvalidOperationException("No inferred ramp placement was found.");
    }

    private static IEnumerable<GridPosition> EnumerateWorldPositions(SimulationEngine engine) =>
        from z in Enumerable.Range(
            engine.Map.MinimumWorldLevel,
            engine.Map.MaximumWorldLevel - engine.Map.MinimumWorldLevel + 1)
        from y in Enumerable.Range(0, engine.Map.Height)
        from x in Enumerable.Range(0, engine.Map.Width)
        select new GridPosition(x, y, z);
}
