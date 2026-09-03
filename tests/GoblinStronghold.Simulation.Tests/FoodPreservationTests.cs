using System.Text.Json.Nodes;
using GoblinStronghold.Simulation.Resources;
using Xunit;

namespace GoblinStronghold.Simulation.Tests;

public sealed class FoodPreservationTests
{
    [Fact]
    public void RawFoodSpoilsFromGroundAndPocketsIntoExistingCompost()
    {
        var definitions = SimulationDefinitions.Foundation;
        var engine = SimulationEngine.Create(
            new WorldSeed(0x5A01UL),
            definitions,
            initialGoblinCount: 1,
            initialFoodStock: 0);
        var nextDayStart = SimulationCalendar.NextDayStart(
            engine.CurrentTick,
            definitions.Clock);
        var save = JsonNode.Parse(engine.Save())!.AsObject();
        save["currentTick"] = nextDayStart.Value - 1;
        var actor = save["actors"]!.AsArray()[0]!.AsObject();
        actor["personalFood"] = 1;
        actor["personalFoodKind"] = (int)FoodKind.Mushrooms;
        actor["personalFoodKinds"] = new JsonArray((int)FoodKind.Mushrooms);
        actor["personalFoodFreshUntilTicks"] = new JsonArray(nextDayStart.Value);
        var nextId = save["nextEntityId"]!.GetValue<ulong>();
        save["itemStacks"]!.AsArray().Add(new JsonObject
        {
            ["id"] = nextId,
            ["resource"] = (int)ResourceKind.Food,
            ["foodKind"] = (int)FoodKind.Berries,
            ["quantity"] = 2,
            ["locationKind"] = (int)ItemLocationKind.Ground,
            ["x"] = engine.Map.GoblinSpawn.X,
            ["y"] = engine.Map.GoblinSpawn.Y,
            ["z"] = engine.Map.GoblinSpawn.Z,
            ["freshUntilTick"] = nextDayStart.Value,
        });
        save["nextEntityId"] = nextId + 1;
        engine = SimulationEngine.Load(save.ToJsonString(), definitions);

        engine.AdvanceTicks(1);

        var snapshot = engine.CreateSnapshot();
        Assert.DoesNotContain(snapshot.ItemStacks, stack => stack.Id.Value == nextId);
        Assert.Empty(Assert.Single(snapshot.Actors).PersonalFoodKinds);
        Assert.Equal(3, snapshot.TribeNeeds.CompostNutrients);
        Assert.Equal(engine.ComputeStateHash(),
            SimulationEngine.Load(engine.Save(), definitions).ComputeStateHash());
    }

    [Fact]
    public void CookedProductsLastMuchLongerThanRawIngredients()
    {
        Assert.True(FoodPreservationPolicy.GetShelfLifeDays(FoodKind.CampSoup) >
            FoodPreservationPolicy.GetShelfLifeDays(FoodKind.Mushrooms));
        Assert.True(FoodPreservationPolicy.GetShelfLifeDays(FoodKind.CookedMeat) >
            FoodPreservationPolicy.GetShelfLifeDays(FoodKind.RawMeat));
        Assert.True(FoodPreservationPolicy.GetShelfLifeDays(FoodKind.DriedRations) >
            FoodPreservationPolicy.GetShelfLifeDays(FoodKind.CookedMeat));
        Assert.True(SimulationDefinitions.Foundation.HealthRecovery.GetFoodHealing(
                FoodKind.Medicine) >
            SimulationDefinitions.Foundation.HealthRecovery.GetFoodHealing(
                FoodKind.EdibleRoots));
    }

    [Fact]
    public void CampSoupCannotBePackedOrStored()
    {
        Assert.False(FoodUsePolicy.CanBePacked(FoodKind.CampSoup));
        Assert.False(FoodUsePolicy.CanBeStored(FoodKind.CampSoup));
        Assert.True(FoodUsePolicy.CanBePacked(FoodKind.DriedRations));
        Assert.True(FoodUsePolicy.CanBeStored(FoodKind.Medicine));
    }

    [Fact]
    public void GoblinCannotPickUpCampSoupFromTheCookingSite()
    {
        var definitions = SimulationDefinitions.Foundation;
        var engine = SimulationEngine.Create(
            new WorldSeed(0x5000UL), definitions, initialGoblinCount: 1, initialFoodStock: 0);
        var save = JsonNode.Parse(engine.Save())!.AsObject();
        var nextId = save["nextEntityId"]!.GetValue<ulong>();
        save["itemStacks"]!.AsArray().Add(new JsonObject
        {
            ["id"] = nextId,
            ["resource"] = (int)ResourceKind.Food,
            ["foodKind"] = (int)FoodKind.CampSoup,
            ["quantity"] = 1,
            ["locationKind"] = (int)ItemLocationKind.Ground,
            ["x"] = engine.Map.GoblinSpawn.X,
            ["y"] = engine.Map.GoblinSpawn.Y,
            ["z"] = engine.Map.GoblinSpawn.Z,
        });
        save["nextEntityId"] = nextId + 1;
        engine = SimulationEngine.Load(save.ToJsonString(), definitions);
        engine.QueueCommand(SimulationCommand.PickUp(
            engine.CurrentTick.Next(),
            engine.NextAvailableCommandSequence,
            new EntityId(1),
            new EntityId(nextId),
            quantity: 1));

        engine.AdvanceTicks(1);

        Assert.Equal(EntityId.None, Assert.Single(engine.CreateSnapshot().Actors).CarriedStackId);
        Assert.Contains(engine.CreateSnapshot().ItemStacks, stack => stack.Id.Value == nextId);
        Assert.Contains(engine.DrainEvents(), item =>
            item.Kind == SimulationEventKind.CommandRejected);
    }
}
