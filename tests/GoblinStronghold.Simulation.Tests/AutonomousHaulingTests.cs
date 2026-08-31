using System.Text.Json.Nodes;
using GoblinStronghold.Simulation.Map;
using GoblinStronghold.Simulation.Resources;
using Xunit;

namespace GoblinStronghold.Simulation.Tests;

public sealed class AutonomousHaulingTests
{
    [Fact]
    public void HaulersDoNotOverReserveItemsOrDestinationCapacity()
    {
        var engine = CreateEngine(goblinCount: 3, initialFood: 25, storageCapacity: 17);
        engine.AdvanceTicks(1);

        var snapshot = engine.CreateSnapshot();
        var zone = Assert.Single(snapshot.StorageZones);
        var haulers = snapshot.Actors
            .Where(actor => actor.Job.Kind == ActorJobKind.Haul)
            .ToArray();

        Assert.Equal(2, haulers.Length);
        Assert.All(haulers, actor =>
        {
            Assert.Equal(ActorJobStage.Collecting, actor.Job.Stage);
            Assert.Equal(zone.Id, actor.Job.DestinationZoneId);
            Assert.InRange(actor.Job.ReservedQuantity, 1, SimulationDefinitions.Foundation.ActorCarryCapacity);
        });
        Assert.Equal(zone.Capacity, haulers.Sum(actor => actor.Job.ReservedQuantity));
        Assert.Single(haulers.Select(actor => actor.Job.SourceStackId).Distinct());
    }

    [Fact]
    public void AutonomousHaulLoadsCarriesAndStoresPhysicalFood()
    {
        var engine = CreateEngine(goblinCount: 3, initialFood: 25, storageCapacity: 17);
        engine.AdvanceTicks(2 * SimulationDefinitions.Foundation.HaulHandlingTicks);

        var snapshot = engine.CreateSnapshot();
        var zone = Assert.Single(snapshot.StorageZones);
        var groundFood = Assert.Single(snapshot.ItemStacks, stack =>
            stack.Resource == ResourceKind.Food &&
            stack.Location.Kind == ItemLocationKind.Ground);

        Assert.Equal(17, zone.StoredQuantity);
        Assert.Equal(
            8,
            groundFood.Quantity + snapshot.Actors.Sum(actor => actor.PersonalFood));
        Assert.Equal(25, snapshot.FoodStock);
        Assert.Contains(
            engine.DrainEvents(),
            simulationEvent => simulationEvent.Kind == SimulationEventKind.ItemStored);
    }

    [Fact]
    public void SaveLoadPreservesInFlightDeliveryAndReservations()
    {
        var engine = CreateEngine(goblinCount: 2, initialFood: 12, storageCapacity: 12);
        engine.AdvanceTicks(10);

        Assert.All(
            engine.CreateSnapshot().Actors,
            actor =>
            {
                Assert.Equal(ActorJobKind.Haul, actor.Job.Kind);
                Assert.Equal(ActorJobStage.Delivering, actor.Job.Stage);
                Assert.NotEqual(EntityId.None, actor.CarriedStackId);
            });

        var restored = SimulationEngine.Load(engine.Save(), SimulationDefinitions.Foundation);
        Assert.Equal(engine.ComputeStateHash(), restored.ComputeStateHash());

        engine.AdvanceTicks(30);
        restored.AdvanceTicks(30);

        Assert.Equal(engine.ComputeStateHash(), restored.ComputeStateHash());
        Assert.Equal(engine.DrainEvents(), restored.DrainEvents());
        var restoredSnapshot = restored.CreateSnapshot();
        Assert.Equal(
            12,
            Assert.Single(restoredSnapshot.StorageZones).StoredQuantity +
            restoredSnapshot.Actors.Sum(actor => actor.PersonalFood));
    }

    [Fact]
    public void EatingFromCarriedFoodKeepsDeliverySaveConsistent()
    {
        var seed = new WorldSeed(0x474F424C494EUL);
        var map = SwampMapGenerator.Generate(seed, width: 64, height: 64);
        var engine = SimulationEngine.Create(
            seed,
            SimulationDefinitions.Foundation,
            map,
            initialGoblinCount: 1,
            initialFoodStock: 10,
            initialHunger: SimulationDefinitions.Foundation.EatThreshold -
                (SimulationDefinitions.Foundation.HungerPerTick * 200));
        engine.QueueCommand(SimulationCommand.CreateStorageZone(
            new SimulationTick(1),
            sequence: 1,
            map.HumanVillage,
            ResourceKind.Food,
            capacity: 10));

        engine.AdvanceTicks(200);

        var actor = Assert.Single(engine.CreateSnapshot().Actors);
        Assert.Equal(ActorJobStage.Delivering, actor.Job.Stage);
        var carried = Assert.Single(engine.CreateSnapshot().ItemStacks, stack =>
            stack.Location.Kind == ItemLocationKind.ActorInventory);
        Assert.Equal(carried.Quantity, actor.Job.ReservedQuantity);
        Assert.Equal(9, carried.Quantity);

        var restored = SimulationEngine.Load(engine.Save(), SimulationDefinitions.Foundation);
        Assert.Equal(engine.ComputeStateHash(), restored.ComputeStateHash());
    }

    [Fact]
    public void JuvenileHaulsOnlyAWeightLimitedFoodStackAndTiresTwiceAsFast()
    {
        var seed = new WorldSeed(0x4A5556454E494C45UL);
        var map = SwampMapGenerator.Generate(seed, width: 32, height: 32);
        var engine = SimulationEngine.Create(
            seed,
            SimulationDefinitions.Foundation,
            map,
            initialGoblinCount: 1,
            initialFoodStock: 10);
        engine = MakeOnlyActorJuvenile(engine);
        engine.QueueCommand(SimulationCommand.CreateStorageZone(
            engine.CurrentTick.Next(),
            sequence: 1,
            map.GoblinSpawn,
            ResourceKind.Food,
            capacity: 10));

        engine.AdvanceTicks(1);

        var juvenile = Assert.Single(engine.CreateSnapshot().Actors);
        Assert.True(juvenile.IsJuvenile);
        Assert.Equal(ActorJobKind.Haul, juvenile.Job.Kind);
        Assert.Equal(6, juvenile.Job.ReservedQuantity);
        Assert.Equal(
            2 * SimulationDefinitions.Foundation.FatiguePerTick,
            juvenile.Fatigue);
    }

    [Fact]
    public void JuvenileDoesNotAcceptHeavyStoneHauling()
    {
        var seed = new WorldSeed(0x4A555653544F4E45UL);
        var map = SwampMapGenerator.Generate(seed, width: 32, height: 32);
        var engine = SimulationEngine.Create(
            seed,
            SimulationDefinitions.Foundation,
            map,
            initialGoblinCount: 1,
            initialFoodStock: 0);
        engine = MakeOnlyActorJuvenile(engine);
        var save = JsonNode.Parse(engine.Save())!.AsObject();
        var stackId = save["nextEntityId"]!.GetValue<ulong>();
        save["nextEntityId"] = stackId + 1;
        save["itemStacks"]!.AsArray().Add(new JsonObject
        {
            ["id"] = stackId,
            ["resource"] = (int)ResourceKind.Stone,
            ["foodKind"] = (int)FoodKind.None,
            ["variant"] = (int)ResourceVariant.Sandstone,
            ["quantity"] = 4,
            ["locationKind"] = (int)ItemLocationKind.Ground,
            ["x"] = map.GoblinSpawn.X,
            ["y"] = map.GoblinSpawn.Y,
            ["z"] = map.GoblinSpawn.Z,
            ["ownerId"] = 0,
        });
        engine = SimulationEngine.Load(save.ToJsonString(), SimulationDefinitions.Foundation);
        engine.QueueCommand(SimulationCommand.CreateStorageZone(
            engine.CurrentTick.Next(),
            sequence: 1,
            map.GoblinSpawn,
            ResourceKind.Stone,
            capacity: 4));

        engine.AdvanceTicks(1);

        var juvenile = Assert.Single(engine.CreateSnapshot().Actors);
        Assert.True(juvenile.IsJuvenile);
        Assert.Equal(ActorJobKind.None, juvenile.Job.Kind);
    }

    private static SimulationEngine MakeOnlyActorJuvenile(SimulationEngine engine)
    {
        var save = JsonNode.Parse(engine.Save())!.AsObject();
        var actor = save["actors"]!.AsArray().Single()!.AsObject();
        actor["birthTick"] = engine.CurrentTick.Value;
        actor["maturesAtTick"] = checked(engine.CurrentTick.Value + 10_000);
        actor["ageOffsetTicks"] = 0;
        actor["fatigue"] = 0;
        return SimulationEngine.Load(save.ToJsonString(), SimulationDefinitions.Foundation);
    }

    private static SimulationEngine CreateEngine(
        int goblinCount,
        int initialFood,
        int storageCapacity)
    {
        var seed = new WorldSeed(0x4841554CUL);
        var map = SwampMapGenerator.Generate(seed, width: 32, height: 32);
        var engine = SimulationEngine.Create(
            seed,
            SimulationDefinitions.Foundation,
            map,
            initialGoblinCount: goblinCount,
            initialFoodStock: initialFood);
        engine.QueueCommand(SimulationCommand.CreateStorageZone(
            new SimulationTick(1),
            sequence: 1,
            map.GoblinSpawn,
            ResourceKind.Food,
            storageCapacity));
        return engine;
    }
}
