using System.Text.Json.Nodes;
using GoblinStronghold.Simulation.Map;
using Xunit;

namespace GoblinStronghold.Simulation.Tests;

public sealed class BloodCleaningTests
{
    [Fact]
    public void PersistedBleedingAddsBoundedBloodPulsesAndExpires()
    {
        var seed = new WorldSeed(0xB1EED1A6UL);
        var engine = SimulationEngine.Create(
            seed,
            SimulationDefinitions.Foundation,
            SwampMapGenerator.Generate(seed, width: 64, height: 64),
            initialGoblinCount: 1,
            initialFoodStock: 20);
        var save = JsonNode.Parse(engine.Save())!.AsObject();
        save["animals"] = new JsonArray();
        save["actors"]![0]!["bleedingTicksRemaining"] = 41;
        engine = SimulationEngine.Load(save.ToJsonString(), SimulationDefinitions.Foundation);

        engine.AdvanceTicks(20);

        Assert.Equal(1, engine.CreateSnapshot().BloodStains.Sum(stain => stain.Volume));
        Assert.Equal(21, Assert.Single(engine.CreateSnapshot().Actors).BleedingTicksRemaining);
        var restored = SimulationEngine.Load(engine.Save(), SimulationDefinitions.Foundation);
        Assert.Equal(engine.ComputeStateHash(), restored.ComputeStateHash());
        engine = restored;

        engine.AdvanceTicks(21);

        Assert.Equal(2, engine.CreateSnapshot().BloodStains.Sum(stain => stain.Volume));
        Assert.Equal(0, Assert.Single(engine.CreateSnapshot().Actors).BleedingTicksRemaining);
    }

    [Fact]
    public void ActorTracksThreeDiminishingFootprintsAndSavePreservesDirtyFeet()
    {
        var seed = new WorldSeed(0xF007B100DUL);
        var map = SwampMapGenerator.Generate(seed, width: 64, height: 64);
        var engine = SimulationEngine.Create(
            seed,
            SimulationDefinitions.Foundation,
            map,
            initialGoblinCount: 1,
            initialFoodStock: 20);
        var actor = Assert.Single(engine.CreateSnapshot().Actors);
        var route = Enumerable.Range(0, map.Height)
            .SelectMany(y => Enumerable.Range(0, map.Width)
                .Select(x => map.GetTerrainSurfacePosition(new GridPosition(x, y))))
            .Where(engine.World.IsTerrainTraversable)
            .Select(position => engine.Navigation.FindPath(actor.Position, position))
            .First(candidate => candidate is { Count: >= 3 } &&
                candidate.Take(3).All(position =>
                    map.GetColumnCell(position).Terrain is not
                        (TerrainKind.ShallowWater or TerrainKind.DeepWater)))
            ?? throw new InvalidOperationException("No dry three-step route was generated.");
        var destination = route[2];
        var save = JsonNode.Parse(engine.Save())!.AsObject();
        save["bloodStains"] = CreateBloodStains(
            actor.Position,
            engine.CurrentTick,
            volume: 16,
            surface: BloodSurfaceKind.AbsorbentGround);
        engine = SimulationEngine.Load(save.ToJsonString(), SimulationDefinitions.Foundation);
        engine.QueueCommand(SimulationCommand.Move(
            engine.CurrentTick.Next(),
            engine.NextAvailableCommandSequence,
            actor.Id,
            destination));

        engine.AdvanceTicks(SimulationDefinitions.Foundation.ActorMovementIntervalTicks);

        save = JsonNode.Parse(engine.Save())!.AsObject();
        Assert.Equal(2, save["actors"]![0]!["bloodFootprintSteps"]!.GetValue<int>());
        var restored = SimulationEngine.Load(save.ToJsonString(), SimulationDefinitions.Foundation);
        Assert.Equal(engine.ComputeStateHash(), restored.ComputeStateHash());
        engine = restored;

        engine.AdvanceTicks(2 * SimulationDefinitions.Foundation.ActorMovementIntervalTicks);

        var stains = engine.CreateSnapshot().BloodStains.ToDictionary(stain => stain.Position);
        Assert.Equal(15, stains[actor.Position].Volume);
        Assert.Equal(3, stains[route[0]].Volume);
        Assert.Equal(2, stains[route[1]].Volume);
        Assert.Equal(1, stains[route[2]].Volume);
        Assert.Equal(destination, Assert.Single(engine.CreateSnapshot().Actors).Position);
        Assert.Equal(
            0,
            JsonNode.Parse(engine.Save())!["actors"]![0]!["bloodFootprintSteps"]!.GetValue<int>());
    }

    [Fact]
    public void IdleGoblinPublishesNearbyHousekeepingWithoutPlayerDesignation()
    {
        var seed = new WorldSeed(0xC1EA4UL);
        var map = SwampMapGenerator.Generate(seed, width: 64, height: 64);
        var engine = SimulationEngine.Create(
            seed,
            SimulationDefinitions.Foundation,
            map,
            initialGoblinCount: 4,
            initialFoodStock: 20);
        engine.AdvanceTicks(1);
        var initial = engine.CreateSnapshot();
        var origin = initial.Actors[0].Position;
        var floor = initial.WorldObjects
            .Where(worldObject =>
                worldObject.Kind == WorldObjectKind.GoblinHut &&
                worldObject.Owner == WorldObjectOwner.GoblinTribe)
            .SelectMany(worldObject => worldObject.GetAbsoluteParts())
            .Where(part => part.Part.Kind == WorldObjectPartKind.Floor)
            .Select(part => part.Position)
            .Where(position =>
                initial.GetVisibility(position, map.Width).IsDiscovered() &&
                engine.World.IsTerrainTraversable(position) &&
                engine.Navigation.FindPath(origin, position) is not null)
            .OrderBy(position => Math.Abs(position.X - origin.X) +
                Math.Abs(position.Y - origin.Y))
            .First();
        var save = JsonNode.Parse(engine.Save())!.AsObject();
        save["bloodStains"] = CreateBloodStains(floor, engine.CurrentTick, volume: 16);
        foreach (var actor in save["actors"]!.AsArray())
        {
            actor!["knownTraits"] = (int)GoblinTrait.None;
        }
        engine = SimulationEngine.Load(save.ToJsonString(), SimulationDefinitions.Foundation);
        var actorIds = engine.CreateSnapshot().Actors.Select(actor => actor.Id).ToHashSet();

        for (var index = 0; index < 5_000 && engine.CreateSnapshot().BloodStains.Count > 0; index++)
        {
            engine.AdvanceTicks(1);
        }

        Assert.Empty(engine.CreateSnapshot().BloodStains);
        Assert.Contains(engine.DrainEvents(), simulationEvent =>
            simulationEvent.Kind == SimulationEventKind.WorkDesignationCreated &&
            actorIds.Contains(simulationEvent.Subject) &&
            simulationEvent.Amount == (int)WorkDesignationKind.CleanBlood);
    }

    [Theory]
    [InlineData(BloodSurfaceKind.ConstructedFloor)]
    [InlineData(BloodSurfaceKind.AbsorbentGround)]
    public void DesignatedStainIsCleanedInResumableDeterministicCycles(BloodSurfaceKind surface)
    {
        var seed = new WorldSeed(0xB100DUL);
        var map = SwampMapGenerator.Generate(seed, width: 64, height: 64);
        var engine = SimulationEngine.Create(
            seed,
            SimulationDefinitions.Foundation,
            map,
            initialGoblinCount: 4,
            initialFoodStock: 20);
        engine.AdvanceTicks(1);
        var initial = engine.CreateSnapshot();
        var origin = initial.Actors[0].Position;
        var floor = initial.WorldObjects
            .Where(worldObject => worldObject.Kind == WorldObjectKind.GoblinHut)
            .SelectMany(worldObject => worldObject.GetAbsoluteParts())
            .Where(part => part.Part.Kind == WorldObjectPartKind.Floor)
            .Select(part => part.Position)
            .Where(position =>
                initial.GetVisibility(position, map.Width).IsDiscovered() &&
                engine.World.IsTerrainTraversable(position) &&
                engine.Navigation.FindPath(origin, position) is not null)
            .OrderBy(position => Math.Abs(position.X - origin.X) +
                Math.Abs(position.Y - origin.Y))
            .First();
        var save = JsonNode.Parse(engine.Save())!.AsObject();
        save["bloodStains"] = CreateBloodStains(
            floor,
            engine.CurrentTick,
            volume: 32,
            surface: surface);
        engine = SimulationEngine.Load(save.ToJsonString(), SimulationDefinitions.Foundation);
        engine.QueueCommand(SimulationCommand.DesignateBloodCleaning(
            engine.CurrentTick.Next(),
            engine.NextAvailableCommandSequence,
            floor,
            floor));
        engine.AdvanceTicks(1);

        Assert.Contains(engine.CreateSnapshot().WorkDesignations, designation =>
            designation.Kind == WorkDesignationKind.CleanBlood && designation.Target == floor);

        var observedCleaning = false;
        for (var index = 0; index < 5_000 && !observedCleaning; index++)
        {
            engine.AdvanceTicks(1);
            observedCleaning |= engine.CreateSnapshot().Actors.Any(actor =>
                actor.Job.Kind == ActorJobKind.CleanBlood &&
                actor.Job.Phase == ActorJobPhase.Working);
        }

        Assert.True(observedCleaning);
        var restoredDuringWork = SimulationEngine.Load(
            engine.Save(),
            SimulationDefinitions.Foundation);
        Assert.Equal(engine.ComputeStateHash(), restoredDuringWork.ComputeStateHash());
        engine = restoredDuringWork;

        var observedPartialCleaning = false;
        for (var index = 0; index < 5_000 && engine.CreateSnapshot().BloodStains.Count > 0; index++)
        {
            engine.AdvanceTicks(1);
            observedPartialCleaning |= engine.CreateSnapshot().BloodStains.Any(stain =>
                stain.Position == floor && stain.Volume == 16);
        }

        Assert.True(observedPartialCleaning);
        Assert.Empty(engine.CreateSnapshot().BloodStains);
        Assert.DoesNotContain(engine.CreateSnapshot().WorkDesignations, designation =>
            designation.Kind == WorkDesignationKind.CleanBlood);

        var restored = SimulationEngine.Load(engine.Save(), SimulationDefinitions.Foundation);
        Assert.Equal(engine.ComputeStateHash(), restored.ComputeStateHash());
    }

    private static JsonArray CreateBloodStains(
        GridPosition position,
        SimulationTick currentTick,
        int volume,
        BloodSurfaceKind surface = BloodSurfaceKind.ConstructedFloor) =>
    [
        new JsonObject
        {
            ["x"] = position.X,
            ["y"] = position.Y,
            ["z"] = position.Z,
            ["volume"] = volume,
            ["surface"] = (int)surface,
            ["createdAtTick"] = currentTick.Value,
            ["lastChangedAtTick"] = currentTick.Value,
        },
    ];
}
