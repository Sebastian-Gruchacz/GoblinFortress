using GoblinStronghold.Simulation.Map;
using Xunit;

namespace GoblinStronghold.Simulation.Tests;

public sealed class RuinedStartSmokeTests
{
    private const int TenNormalMinutesInTicks = 10 * 60 * 10;

    [Theory]
    [InlineData(0x5255494E0001UL)]
    [InlineData(0x5255494E0002UL)]
    [InlineData(0x5255494E0003UL)]
    public void WeekendRuinedStartRunsAndReloadsDeterministically(ulong seedValue)
    {
        var seed = new WorldSeed(seedValue);
        var map = SwampMapGenerator.Generate(seed, width: 64, height: 64);
        var engine = SimulationEngine.Create(
            seed,
            SimulationDefinitions.Foundation,
            map,
            initialGoblinCount: 12,
            initialFoodStock: 32);

        AssertStarterComposition(engine.CreateSnapshot());
        engine.AdvanceTicks(TenNormalMinutesInTicks / 2);
        var restored = SimulationEngine.Load(engine.Save(), SimulationDefinitions.Foundation);
        Assert.Equal(engine.ComputeStateHash(), restored.ComputeStateHash());

        engine.AdvanceTicks(TenNormalMinutesInTicks / 2);
        restored.AdvanceTicks(TenNormalMinutesInTicks / 2);

        Assert.Equal(engine.ComputeStateHash(), restored.ComputeStateHash());
        Assert.Equal(engine.DrainEvents(), restored.DrainEvents());
        Assert.Equal(engine.DrainWorldChanges(), restored.DrainWorldChanges());
        AssertHealthyRuinedStartState(engine);
    }

    private static void AssertStarterComposition(SimulationSnapshot snapshot)
    {
        Assert.Equal(12, snapshot.Actors.Count);
        Assert.Single(snapshot.WorldObjects, worldObject =>
            worldObject.Kind == WorldObjectKind.GoblinRuin);
        Assert.Single(snapshot.WorldObjects, worldObject =>
            worldObject.Kind == WorldObjectKind.GoblinCompost);
        Assert.Equal(4, snapshot.WorldObjects.Count(worldObject =>
            worldObject.Kind == WorldObjectKind.ReedSleepingMat));
        Assert.Single(snapshot.WorldObjects, worldObject =>
            worldObject.Kind == WorldObjectKind.StandingTorch);
    }

    private static void AssertHealthyRuinedStartState(SimulationEngine engine)
    {
        var snapshot = engine.CreateSnapshot();
        Assert.NotEmpty(snapshot.Actors);
        Assert.All(snapshot.Actors, actor =>
        {
            Assert.True(actor.Health > 0);
            Assert.True(engine.World.IsTerrainTraversable(actor.Position));
        });

        var sleepingMats = snapshot.WorldObjects
            .Where(worldObject => worldObject.Kind == WorldObjectKind.ReedSleepingMat)
            .Select(worldObject => worldObject.Anchor)
            .ToHashSet();
        Assert.All(sleepingMats, position =>
            Assert.True(engine.World.IsTerrainTraversable(position)));
        var reservedMats = snapshot.Actors
            .Where(actor => actor.Job.Kind == ActorJobKind.Rest &&
                sleepingMats.Contains(actor.Job.Target))
            .Select(actor => actor.Job.Target)
            .ToArray();
        Assert.Equal(reservedMats.Length, reservedMats.Distinct().Count());
    }
}
