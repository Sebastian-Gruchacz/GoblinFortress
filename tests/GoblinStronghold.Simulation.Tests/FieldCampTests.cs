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
        engine.QueueCommand(SimulationCommand.AttackHumanVillage(
            new SimulationTick(3), sequence: 3));
        engine.AdvanceTicks(2);

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
