using GoblinStronghold.Simulation.Map;
using GoblinStronghold.Simulation.Shelter;
using Xunit;

namespace GoblinStronghold.Simulation.Tests;

public sealed class SurvivalNeedTests
{
    [Fact]
    public void TiredGoblinChoosesAReachableHutAndRecovers()
    {
        var engine = CreateEngine();
        ActorSnapshot resting = default;
        for (var tick = 0; tick < 2_000; tick++)
        {
            engine.AdvanceTicks(1);
            var actor = Assert.Single(engine.CreateSnapshot().Actors);
            if (actor.Job.Kind == ActorJobKind.Rest && actor.Job.Phase == ActorJobPhase.Working)
            {
                resting = actor;
                break;
            }
        }

        Assert.NotEqual(EntityId.None, resting.Id);
        Assert.Contains(
            engine.World.GetWorldObjectsAt(resting.Job.Target),
            worldObject => worldObject.Kind is
                WorldObjectKind.GoblinHut or WorldObjectKind.GoblinRuin);
        var fatigueBeforeRest = resting.Fatigue;

        engine.AdvanceTicks(5);

        Assert.True(Assert.Single(engine.CreateSnapshot().Actors).Fatigue < fatigueBeforeRest);
    }

    [Fact]
    public void SaveLoadDuringRestPreservesFutureOutcome()
    {
        var engine = CreateEngine();
        for (var tick = 0; tick < 2_000; tick++)
        {
            engine.AdvanceTicks(1);
            if (Assert.Single(engine.CreateSnapshot().Actors).Job.Kind == ActorJobKind.Rest)
            {
                break;
            }
        }

        Assert.Equal(ActorJobKind.Rest, Assert.Single(engine.CreateSnapshot().Actors).Job.Kind);
        var restored = SimulationEngine.Load(engine.Save(), SimulationDefinitions.Foundation);
        Assert.Equal(engine.ComputeStateHash(), restored.ComputeStateHash());

        engine.AdvanceTicks(300);
        restored.AdvanceTicks(300);

        Assert.Equal(engine.ComputeStateHash(), restored.ComputeStateHash());
        Assert.Equal(engine.DrainEvents(), restored.DrainEvents());
        Assert.Equal(engine.DrainWorldChanges(), restored.DrainWorldChanges());
    }

    [Fact]
    public void TiredGoblinsReserveDifferentSleepingMats()
    {
        var engine = CreateEngine(initialGoblinCount: 2);
        var sleepingMats = engine.World.CreateWorldObjectSnapshot()
            .Where(worldObject => worldObject.Kind == WorldObjectKind.ReedSleepingMat)
            .Select(worldObject => worldObject.Anchor)
            .ToHashSet();

        for (var tick = 0; tick < 2_000; tick++)
        {
            engine.AdvanceTicks(1);
            if (engine.CreateSnapshot().Actors.All(actor =>
                    actor.Job.Kind == ActorJobKind.Rest))
            {
                break;
            }
        }

        var targets = engine.CreateSnapshot().Actors
            .Select(actor => actor.Job.Target)
            .ToArray();
        Assert.Equal(2, targets.Length);
        Assert.All(targets, target => Assert.Contains(target, sleepingMats));
        Assert.Equal(2, targets.Distinct().Count());
    }

    [Fact]
    public void SleepingPlacePolicyPrefersCoveredMatsButKeepsExposedMatsUsable()
    {
        var covered = new GridPosition(1, 1);
        var exposed = new GridPosition(2, 1);
        var fallback = new GridPosition(3, 1);
        var worldObjects = new[]
        {
            CreateSleepingMat(id: 1, covered),
            CreateSleepingMat(id: 2, exposed),
        };

        var options = GoblinSleepingPlacePolicy.CreateOptions(
            worldObjects,
            new HashSet<GridPosition>(),
            new HashSet<GridPosition> { covered, exposed, fallback },
            _ => true,
            position => position == exposed);

        Assert.Equal(new[] { covered }, options.CoveredSleepingMats);
        Assert.Equal(new[] { exposed }, options.ExposedSleepingMats);
        Assert.Equal(new[] { fallback }, options.ShelterFloorFallback);
    }

    private static WorldObjectSnapshot CreateSleepingMat(ulong id, GridPosition position) =>
        new(
            new WorldObjectId(id),
            WorldObjectKind.ReedSleepingMat,
            WorldObjectOwner.GoblinTribe,
            position,
            CardinalOrientation.North,
            [new(default, SpatialOccupancyChannel.Fixture,
                WorldObjectPartKind.SleepingMat)]);

    private static SimulationEngine CreateEngine(int initialGoblinCount = 1)
    {
        var seed = new WorldSeed(0x52455354UL);
        var map = SwampMapGenerator.Generate(seed, width: 32, height: 32);
        var engine = SimulationEngine.Create(
            seed,
            SimulationDefinitions.Foundation,
            map,
            initialGoblinCount,
            initialFoodStock: 0);
        var save = System.Text.Json.Nodes.JsonNode.Parse(engine.Save())?.AsObject()
            ?? throw new InvalidOperationException("The simulation produced invalid JSON.");
        foreach (var actor in save["actors"]!.AsArray())
        {
            actor!["fatigue"] = SimulationDefinitions.Foundation.RestThreshold;
        }
        return SimulationEngine.Load(save.ToJsonString(), SimulationDefinitions.Foundation);
    }
}
