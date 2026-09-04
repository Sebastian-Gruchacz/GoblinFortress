using GoblinStronghold.Simulation.Map;
using GoblinStronghold.Simulation.Resources;
using GoblinStronghold.Simulation.Terrain;
using GoblinStronghold.Simulation.Terrain.Jobs;
using Xunit;

namespace GoblinStronghold.Simulation.Tests;

public sealed class TerrainWorkExecutionServiceTests
{
    [Fact]
    public void MiningReturnsOneAtomicWorldAndYieldResult()
    {
        var engine = CreateEngine();
        var target = FindMiningTarget(engine.World);
        var actorPosition = engine.World.GetCardinalWorldNeighbors(target)
            .First(engine.World.IsTerrainTraversable);

        var result = TerrainWorkExecutionService.TryExecute(
            TerrainModificationCatalog.Get(WorkDesignationKind.MineRock),
            engine.World,
            target,
            actorPosition,
            engine.WorldSeed,
            new EntityId(7),
            new SimulationTick(11),
            new EntityId(19));

        Assert.NotNull(result);
        Assert.Equal(WorldChangeKind.RockExcavated, result.WorldChange.Kind);
        Assert.Equal(target, result.WorldChange.Position);
        Assert.Equal(target, result.OutputPosition);
        Assert.NotEmpty(result.Yield.Stacks);
        Assert.False(engine.World.IsSolidRock(target));
    }

    [Fact]
    public void CompletedTargetCannotBeExecutedTwice()
    {
        var engine = CreateEngine();
        var target = FindMiningTarget(engine.World);
        var definition = TerrainModificationCatalog.Get(WorkDesignationKind.MineRock);

        var first = TerrainWorkExecutionService.TryExecute(
            definition,
            engine.World,
            target,
            target,
            engine.WorldSeed,
            new EntityId(7),
            new SimulationTick(11),
            new EntityId(19));
        var second = TerrainWorkExecutionService.TryExecute(
            definition,
            engine.World,
            target,
            target,
            engine.WorldSeed,
            new EntityId(7),
            new SimulationTick(12),
            new EntityId(19));

        Assert.NotNull(first);
        Assert.Null(second);
    }

    [Fact]
    public void RampExecutionReturnsPassageChangeAndExcavatedMaterialAtTheOrigin()
    {
        var engine = CreateEngine();
        var target =
            (from y in Enumerable.Range(0, engine.Map.Height)
             from x in Enumerable.Range(0, engine.Map.Width)
             let candidate = new GridPosition(x, y, -1)
             where engine.World.CanCarveRampDown(candidate)
             select candidate).First();

        var result = TerrainWorkExecutionService.TryExecute(
            TerrainModificationCatalog.Get(WorkDesignationKind.CarveRampDown),
            engine.World,
            target,
            target,
            engine.WorldSeed,
            new EntityId(7),
            new SimulationTick(11),
            new EntityId(19));

        Assert.NotNull(result);
        Assert.Equal(WorldChangeKind.RampExcavated, result.WorldChange.Kind);
        Assert.Equal(target, result.OutputPosition);
        var stack = Assert.Single(result.Yield.Stacks);
        Assert.Contains(stack.Resource, new[]
        {
            ResourceKind.Stone,
            ResourceKind.Earth,
            ResourceKind.Sand,
        });
        Assert.Contains(engine.World.ExcavatedVerticalPassages, passage =>
            passage.Upper == target && passage.Lower == target with { Z = -2 });
    }

    private static SimulationEngine CreateEngine()
    {
        var seed = new WorldSeed(0x5445525241494E45UL);
        return SimulationEngine.Create(
            seed,
            SimulationDefinitions.Foundation,
            SwampMapGenerator.Generate(seed, width: 48, height: 48),
            initialGoblinCount: 1,
            initialFoodStock: 30);
    }

    private static GridPosition FindMiningTarget(WorldMapState world) =>
        (from level in Enumerable.Range(1, world.Baseline.CaveLevelCount)
         from y in Enumerable.Range(0, world.Baseline.Height)
         from x in Enumerable.Range(0, world.Baseline.Width)
         let candidate = new GridPosition(x, y, -level)
         where world.CanExcavateRock(candidate)
         select candidate).First();
}
