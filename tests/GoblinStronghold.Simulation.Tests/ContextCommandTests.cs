using System.Text.Json.Nodes;
using GoblinStronghold.Simulation.Map;
using GoblinStronghold.Simulation.Resources;
using Xunit;

namespace GoblinStronghold.Simulation.Tests;

public sealed class ContextCommandTests
{
    [Fact]
    public void DispatcherSuspensionLastsOneGameHourAndSurvivesSaveLoad()
    {
        var engine = CreateEngine();
        var actor = Assert.Single(engine.CreateSnapshot().Actors);

        engine.QueueCommand(SimulationCommand.SuspendActorDispatcher(
            engine.CurrentTick.Next(), sequence: 1, actor.Id));
        engine.AdvanceTicks(1);

        var suspended = Assert.Single(engine.CreateSnapshot().Actors);
        var calendar = SimulationCalendar.At(engine.CurrentTick, engine.Definitions.Clock);
        var ticksPerDay = engine.Definitions.Clock.Climate.GetSeason(calendar.Season).TicksPerDay;
        var expectedGameHourTicks = Math.Max(1, (ticksPerDay + 23) / 24);
        Assert.Equal(
            engine.CurrentTick.Value + expectedGameHourTicks,
            suspended.DispatcherSuspendedUntilTick);
        Assert.Contains(engine.DrainEvents(), item =>
            item.Kind == SimulationEventKind.ActorDispatcherSuspended &&
            item.Subject == actor.Id);

        var restored = SimulationEngine.Load(engine.Save(), SimulationDefinitions.Foundation);
        Assert.Equal(engine.ComputeStateHash(), restored.ComputeStateHash());
        Assert.Equal(
            suspended.DispatcherSuspendedUntilTick,
            Assert.Single(restored.CreateSnapshot().Actors).DispatcherSuspendedUntilTick);
    }

    [Fact]
    public void ExplicitEquipReplacesWeaponAndReturnsPreviousOneToSource()
    {
        var engine = CreateEngine();
        var save = JsonNode.Parse(engine.Save())!.AsObject();
        var actorModel = save["actors"]!.AsArray().Single()!.AsObject();
        var actorId = actorModel["id"]!.GetValue<ulong>();
        actorModel["equipment"] = (int)PersonalEquipment.FightingStick;
        actorModel["personalStoneAmmo"] = 0;
        var stackId = save["nextEntityId"]!.GetValue<ulong>();
        save["nextEntityId"] = stackId + 1;
        save["itemStacks"]!.AsArray().Add(new JsonObject
        {
            ["id"] = stackId,
            ["resource"] = (int)ResourceKind.Equipment,
            ["foodKind"] = (int)FoodKind.None,
            ["variant"] = (int)ResourceVariant.EquipmentStoneClub,
            ["quantity"] = 1,
            ["locationKind"] = (int)ItemLocationKind.Ground,
            ["x"] = engine.Map.GoblinSpawn.X,
            ["y"] = engine.Map.GoblinSpawn.Y,
            ["z"] = engine.Map.GoblinSpawn.Z,
            ["ownerId"] = 0,
        });
        engine = SimulationEngine.Load(save.ToJsonString(), SimulationDefinitions.Foundation);

        engine.QueueCommand(SimulationCommand.EquipItem(
            engine.CurrentTick.Next(),
            sequence: 1,
            new EntityId(actorId),
            new EntityId(stackId)));
        engine.AdvanceTicks(1);

        var snapshot = engine.CreateSnapshot();
        var equipped = Assert.Single(snapshot.Actors);
        Assert.True(equipped.Equipment.HasFlag(PersonalEquipment.StoneClub));
        Assert.False(equipped.Equipment.HasFlag(PersonalEquipment.FightingStick));
        Assert.Contains(snapshot.ItemStacks, stack =>
            stack.Resource == ResourceKind.Equipment &&
            stack.Variant == ResourceVariant.EquipmentFightingStick &&
            stack.Location == ItemLocation.OnGround(engine.Map.GoblinSpawn));
        Assert.DoesNotContain(snapshot.ItemStacks, stack => stack.Id == new EntityId(stackId));
    }

    [Fact]
    public void SpecificStackCanReceiveOneShotUrgentHaulingPriority()
    {
        var engine = CreateEngine(initialWood: 5);
        var storagePosition = engine.Map.GetCardinalNeighbors(engine.Map.GoblinSpawn)
            .First(engine.World.IsTerrainTraversable);
        engine.ApplyCommandImmediately(SimulationCommand.CreateStorageZone(
            engine.CurrentTick,
            sequence: 1,
            storagePosition,
            ResourceKind.Wood,
            capacity: 10));
        var source = Assert.Single(engine.CreateSnapshot().ItemStacks, stack =>
            stack.Resource == ResourceKind.Wood &&
            stack.Location.Kind == ItemLocationKind.Ground);

        engine.ApplyCommandImmediately(SimulationCommand.PrioritizeItemHauling(
            engine.CurrentTick,
            sequence: 2,
            source.Id));

        var prioritized = Assert.Single(engine.CreateSnapshot().ItemStacks, stack =>
            stack.Id == source.Id);
        Assert.Equal(StoragePriority.Urgent, prioritized.HaulPriority);
        Assert.Contains(engine.DrainEvents(), item =>
            item.Kind == SimulationEventKind.ItemHaulPrioritized &&
            item.Target == source.Id);
        var restored = SimulationEngine.Load(engine.Save(), SimulationDefinitions.Foundation);
        Assert.Equal(engine.ComputeStateHash(), restored.ComputeStateHash());
        Assert.Equal(
            StoragePriority.Urgent,
            Assert.Single(restored.CreateSnapshot().ItemStacks, stack =>
                stack.Id == source.Id).HaulPriority);
    }

    [Fact]
    public void DirectPickupOrderCreatesTravelingHaulJobInsteadOfTeleportingGoblin()
    {
        var engine = CreateEngine(initialWood: 5);
        var actor = Assert.Single(engine.CreateSnapshot().Actors);
        var storagePosition = engine.Map.GetCardinalNeighbors(engine.Map.GoblinSpawn)
            .First(engine.World.IsTerrainTraversable);
        engine.ApplyCommandImmediately(SimulationCommand.CreateStorageZone(
            engine.CurrentTick,
            sequence: 1,
            storagePosition,
            ResourceKind.Wood,
            capacity: 10));
        var source = Assert.Single(engine.CreateSnapshot().ItemStacks, stack =>
            stack.Resource == ResourceKind.Wood &&
            stack.Location.Kind == ItemLocationKind.Ground);

        engine.ApplyCommandImmediately(SimulationCommand.OrderItemPickup(
            engine.CurrentTick,
            sequence: 2,
            actor.Id,
            source.Id));

        var ordered = Assert.Single(engine.CreateSnapshot().Actors);
        Assert.Equal(actor.Position, ordered.Position);
        Assert.Equal(ActorJobKind.Haul, ordered.Job.Kind);
        Assert.Equal(ActorJobStage.Collecting, ordered.Job.Stage);
        Assert.Equal(source.Id, ordered.Job.SourceStackId);
        Assert.Equal(EntityId.None, ordered.CarriedStackId);
    }

    private static SimulationEngine CreateEngine(int initialWood = 0) =>
        SimulationEngine.Create(
            new WorldSeed(0x434F4E54455854UL),
            SimulationDefinitions.Foundation,
            initialGoblinCount: 1,
            initialFoodStock: 20,
            initialWoodStock: initialWood);
}
