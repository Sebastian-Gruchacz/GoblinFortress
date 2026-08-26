using System.Text.Json.Nodes;
using GoblinStronghold.Simulation.Map;
using GoblinStronghold.Simulation.Resources;
using Xunit;

namespace GoblinStronghold.Simulation.Tests;

public sealed class FieldCampTests
{
    [Fact]
    public void FieldCampIsPhysicalRestShelterWithProvisionDemand()
    {
        var engine = CreateEngine();
        var position = FindCampPosition(engine);
        engine.QueueCommand(SimulationCommand.BuildGoblinFieldCamp(
            new SimulationTick(1), sequence: 1, position));

        engine.AdvanceTicks(1);

        var site = Assert.Single(engine.CreateSnapshot().ConstructionSites);
        Assert.Equal(ConstructionKind.GoblinFieldCamp, site.Kind);
        SimulationTestSteps.AdvanceUntilConstructionCompletes(engine);

        var snapshot = engine.CreateSnapshot();
        var camp = Assert.Single(snapshot.WorldObjects,
            item => item.Kind == WorldObjectKind.GoblinFieldCamp);
        Assert.Equal(position, camp.Anchor);
        Assert.Equal(4, camp.Parts.Count(item => item.Kind == WorldObjectPartKind.Floor));
        Assert.Equal(4, camp.Parts.Count(item => item.Kind == WorldObjectPartKind.Roof));
        var provisions = Assert.Single(snapshot.StorageZones, item => item.Position == position);
        Assert.Equal(ResourceKind.Food, provisions.AcceptedResource);
        Assert.Equal(48, provisions.Capacity);
        Assert.Equal(24, provisions.DesiredQuantity);
        Assert.Equal(4, snapshot.ItemStacks.Where(item => item.Resource == ResourceKind.Wood)
            .Sum(item => item.Quantity));
    }

    [Fact]
    public void RaidRequiresCampThenPreparesBeforeDepartureAndSurvivesSaveLoad()
    {
        var engine = CreateEngine();
        engine.QueueCommand(SimulationCommand.AttackHumanVillage(
            new SimulationTick(1), sequence: 1));
        engine.AdvanceTicks(1);
        Assert.Equal(GoblinRaidPhase.None, engine.CreateSnapshot().RaidPhase);
        Assert.Contains(engine.DrainEvents(), item =>
            item.Kind == SimulationEventKind.CommandRejected &&
            item.Amount == (int)SimulationCommandKind.AttackHumanVillage);

        var position = FindCampPosition(engine);
        engine.QueueCommand(SimulationCommand.BuildGoblinFieldCamp(
            new SimulationTick(2), sequence: 2, position));
        engine.AdvanceTicks(1);
        SimulationTestSteps.AdvanceUntilConstructionCompletes(engine);
        engine.QueueCommand(SimulationCommand.AttackHumanVillage(
            engine.CurrentTick.Next(), sequence: 3));
        engine.AdvanceTicks(1);

        var preparing = engine.CreateSnapshot();
        Assert.Equal(GoblinRaidPhase.Preparing, preparing.RaidPhase);
        Assert.Equal(position, preparing.RaidRallyPoint);
        Assert.False(preparing.HumanVillage.GoblinAttackOrdered);
        var restored = SimulationEngine.Load(engine.Save(), SimulationDefinitions.Foundation);
        Assert.Equal(engine.ComputeStateHash(), restored.ComputeStateHash());

        engine.AdvanceTicks(300);
        restored.AdvanceTicks(300);

        Assert.Equal(engine.ComputeStateHash(), restored.ComputeStateHash());
        var departed = engine.CreateSnapshot();
        Assert.True(departed.RaidPhase == GoblinRaidPhase.Marching,
            $"Raid phase: {departed.RaidPhase}; rally: {departed.RaidRallyPoint}; actor: {Assert.Single(departed.Actors)}");
        Assert.True(departed.HumanVillage.GoblinAttackOrdered);
        Assert.Contains(engine.DrainEvents(), item => item.Kind == SimulationEventKind.RaidDeparted);
    }

    [Fact]
    public void ExplicitRaidPartyIsUsedAndSurvivesSaveLoad()
    {
        var engine = SimulationEngine.Create(
            new WorldSeed(0x5041525459UL),
            SimulationDefinitions.Foundation,
            initialGoblinCount: 3,
            initialFoodStock: 40,
            initialWoodStock: 10);
        var campPosition = FindCampPosition(engine);
        engine.QueueCommand(SimulationCommand.BuildGoblinFieldCamp(
            new SimulationTick(1), sequence: 1, campPosition));
        engine.AdvanceTicks(1);
        SimulationTestSteps.AdvanceUntilConstructionCompletes(engine);

        var actors = engine.CreateSnapshot().Actors.OrderBy(actor => actor.Id).ToArray();
        var executeAt = engine.CurrentTick.Next();
        engine.QueueCommand(SimulationCommand.ConfigureRaidMember(
            executeAt, sequence: 2, actors[1].Id, selected: true));
        engine.QueueCommand(SimulationCommand.ConfigureRaidMember(
            executeAt, sequence: 3, actors[2].Id, selected: true));
        engine.QueueCommand(SimulationCommand.AttackHumanVillage(executeAt, sequence: 4));
        engine.AdvanceTicks(1);

        var snapshot = engine.CreateSnapshot();
        Assert.Equal(GoblinRaidPhase.Preparing, snapshot.RaidPhase);
        Assert.Equal([actors[1].Id, actors[2].Id], snapshot.RaidPartyIds);
        Assert.DoesNotContain(actors[0].Id, snapshot.RaidPartyIds);

        var restored = SimulationEngine.Load(engine.Save(), SimulationDefinitions.Foundation);
        Assert.Equal(engine.ComputeStateHash(), restored.ComputeStateHash());
        Assert.Equal(snapshot.RaidPartyIds, restored.CreateSnapshot().RaidPartyIds);
    }

    [Fact]
    public void RaidMemberCanPackDifferentFoodKindFromCamp()
    {
        var engine = CreateEngine();
        var campPosition = FindCampPosition(engine);
        engine.QueueCommand(SimulationCommand.BuildGoblinFieldCamp(
            new SimulationTick(1), sequence: 1, campPosition));
        engine.AdvanceTicks(1);
        SimulationTestSteps.AdvanceUntilConstructionCompletes(engine);

        var snapshot = engine.CreateSnapshot();
        var actor = Assert.Single(snapshot.Actors);
        var campStorage = Assert.Single(snapshot.StorageZones, zone => zone.Position == campPosition);
        var foodStack = Assert.Single(snapshot.ItemStacks, stack => stack.Resource == ResourceKind.Food);
        var save = JsonNode.Parse(engine.Save())?.AsObject()
            ?? throw new InvalidOperationException("The simulation produced invalid JSON.");
        var actorModel = save["actors"]![0]!.AsObject();
        actorModel["personalFood"] = 1;
        actorModel["personalFoodKind"] = (int)FoodKind.DriedRations;
        actorModel.Remove("personalFoodKinds");
        var stackModel = save["itemStacks"]!.AsArray()
            .Single(item => item!["id"]!.GetValue<ulong>() == foodStack.Id.Value)!.AsObject();
        stackModel["foodKind"] = (int)FoodKind.EdibleRoots;
        stackModel["quantity"] = 10;
        stackModel["locationKind"] = (int)ItemLocationKind.StorageZone;
        stackModel["x"] = campPosition.X;
        stackModel["y"] = campPosition.Y;
        stackModel["z"] = campPosition.Z;
        stackModel["ownerId"] = campStorage.Id.Value;
        engine = SimulationEngine.Load(save.ToJsonString(), SimulationDefinitions.Foundation);

        var executeAt = engine.CurrentTick.Next();
        engine.QueueCommand(SimulationCommand.ConfigureRaidMember(
            executeAt, sequence: 2, actor.Id, selected: true));
        engine.QueueCommand(SimulationCommand.AttackHumanVillage(executeAt, sequence: 3));
        engine.AdvanceTicks(1);
        for (var tick = 0; tick < 2_000; tick++)
        {
            engine.AdvanceTicks(1);
            if (Assert.Single(engine.CreateSnapshot().Actors).PersonalFood ==
                engine.Definitions.PersonalFoodCapacity)
            {
                break;
            }
        }

        var provisioned = Assert.Single(engine.CreateSnapshot().Actors);
        Assert.Equal(
            [FoodKind.DriedRations, FoodKind.EdibleRoots],
            provisioned.PersonalFoodKinds.ToArray());
        var restored = SimulationEngine.Load(engine.Save(), SimulationDefinitions.Foundation);
        Assert.Equal(engine.ComputeStateHash(), restored.ComputeStateHash());
        Assert.Equal(
            provisioned.PersonalFoodKinds.ToArray(),
            Assert.Single(restored.CreateSnapshot().Actors).PersonalFoodKinds.ToArray());
    }

    private static SimulationEngine CreateEngine() => SimulationEngine.Create(
        new WorldSeed(0x43414D50UL),
        SimulationDefinitions.Foundation,
        initialGoblinCount: 1,
        initialFoodStock: 20,
        initialWoodStock: 10);

    private static GridPosition FindCampPosition(SimulationEngine engine) =>
        Enumerable.Range(0, engine.Map.Height)
            .SelectMany(y => Enumerable.Range(0, engine.Map.Width)
                .Select(x => new GridPosition(x, y)))
            .Where(engine.World.CanBuildGoblinFieldCamp)
            .OrderBy(position => Math.Abs(position.X - engine.Map.GoblinSpawn.X) +
                Math.Abs(position.Y - engine.Map.GoblinSpawn.Y))
            .ThenBy(position => position.Y)
            .ThenBy(position => position.X)
            .First();
}
