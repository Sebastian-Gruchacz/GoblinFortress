using System.Text.Json.Nodes;
using GoblinStronghold.Simulation.Map;
using GoblinStronghold.Simulation.Resources;
using Xunit;

namespace GoblinStronghold.Simulation.Tests;

public sealed class SaveMigrationTests
{
    [Fact]
    public void VersionFiftySevenInitializesUndergroundFactionState()
    {
        var save = JsonNode.Parse(CreateEngine().Save())!.AsObject();
        save["formatVersion"] = 57;
        save.Remove("undergroundFactions");

        var restored = SimulationEngine.Load(
            save.ToJsonString(),
            SimulationDefinitions.Foundation);

        Assert.Empty(restored.CreateSnapshot().UndergroundFactions);
        Assert.Equal(59, JsonNode.Parse(restored.Save())!["formatVersion"]!.GetValue<int>());
    }

    [Fact]
    public void VersionFiftyFourInitializesPersistentCorpses()
    {
        var save = JsonNode.Parse(CreateEngine().Save())!.AsObject();
        save["formatVersion"] = 54;
        save.Remove("corpses");

        var restored = SimulationEngine.Load(
            save.ToJsonString(),
            SimulationDefinitions.Foundation);

        Assert.Empty(restored.CreateSnapshot().Corpses);
        Assert.Equal(59, JsonNode.Parse(restored.Save())!["formatVersion"]!.GetValue<int>());
    }

    [Fact]
    public void VersionFiftyThreeDisablesLegacyDefaultAutomaticRaidLaunch()
    {
        var save = JsonNode.Parse(CreateEngine().Save())!.AsObject();
        save["formatVersion"] = 53;
        save["raidDirectives"] = (int)(SimulationEngine.DefaultRaidDirectives |
            RaidDirective.AutoLaunchWhenReady);

        var restored = SimulationEngine.Load(
            save.ToJsonString(),
            SimulationDefinitions.Foundation);
        var migrated = JsonNode.Parse(restored.Save())!.AsObject();

        Assert.Equal(59, migrated["formatVersion"]!.GetValue<int>());
        Assert.False(restored.CreateSnapshot().RaidPlan.Has(
            RaidDirective.AutoLaunchWhenReady));
    }

    [Fact]
    public void VersionFiftyTwoAddsEquipmentResourcePriority()
    {
        var save = JsonNode.Parse(CreateEngine().Save())!.AsObject();
        save["formatVersion"] = 52;
        var priorities = save["resourcePriorities"]!.AsArray();
        priorities.Remove(priorities.Single(priority =>
            priority!["resource"]!.GetValue<int>() == (int)ResourceKind.Equipment));

        var restored = SimulationEngine.Load(
            save.ToJsonString(),
            SimulationDefinitions.Foundation);
        var migrated = JsonNode.Parse(restored.Save())!.AsObject();

        Assert.Equal(59, migrated["formatVersion"]!.GetValue<int>());
        Assert.Contains(migrated["resourcePriorities"]!.AsArray(), priority =>
            priority!["resource"]!.GetValue<int>() == (int)ResourceKind.Equipment &&
            priority["priority"]!.GetValue<int>() == (int)StoragePriority.Normal);
    }

    [Fact]
    public void VersionThirtyAddsMissingResourcePrioritiesAndMigratesToCurrentVersion()
    {
        var engine = CreateEngine();
        var save = JsonNode.Parse(engine.Save())!.AsObject();
        save["formatVersion"] = 30;
        var priorities = save["resourcePriorities"]!.AsArray();
        var hidePriority = priorities.Single(priority =>
            priority!["resource"]!.GetValue<int>() == (int)ResourceKind.Hide);
        priorities.Remove(hidePriority);

        var restored = SimulationEngine.Load(
            save.ToJsonString(),
            SimulationDefinitions.Foundation);
        var migrated = JsonNode.Parse(restored.Save())!.AsObject();

        Assert.Equal(59, migrated["formatVersion"]!.GetValue<int>());
        Assert.Contains(migrated["resourcePriorities"]!.AsArray(), priority =>
            priority!["resource"]!.GetValue<int>() == (int)ResourceKind.Hide &&
            priority["priority"]!.GetValue<int>() == (int)StoragePriority.Normal);
    }

    [Fact]
    public void VersionThirtyMovesActorOutOfObsoleteBlockedPosition()
    {
        var engine = CreateEngine();
        var blockedPosition = engine.World.CreateWorldObjectSnapshot()
            .SelectMany(worldObject => worldObject.Parts.Select(part => new GridPosition(
                worldObject.Anchor.X + part.RelativePosition.X,
                worldObject.Anchor.Y + part.RelativePosition.Y,
                worldObject.Anchor.Z + part.RelativePosition.Z)))
            .First(position => position.Z == 0 &&
                engine.Map.IsWithin(position) &&
                !engine.World.IsTerrainTraversable(position));
        var save = JsonNode.Parse(engine.Save())!.AsObject();
        save["formatVersion"] = 30;
        var actor = save["actors"]![0]!.AsObject();
        actor["x"] = blockedPosition.X;
        actor["y"] = blockedPosition.Y;
        actor["z"] = blockedPosition.Z;

        var restored = SimulationEngine.Load(
            save.ToJsonString(),
            SimulationDefinitions.Foundation);
        var restoredActor = Assert.Single(restored.CreateSnapshot().Actors);

        Assert.NotEqual(blockedPosition, restoredActor.Position);
        Assert.True(restored.World.IsTerrainTraversable(restoredActor.Position));
        Assert.Equal(ActorJobKind.None, restoredActor.Job.Kind);
        Assert.Equal(59, JsonNode.Parse(restored.Save())!["formatVersion"]!.GetValue<int>());
    }

    [Fact]
    public void VersionThirtyOneMigratesToCurrentWithoutChangingSimulationState()
    {
        var engine = CreateEngine();
        var save = JsonNode.Parse(engine.Save())!.AsObject();
        save["formatVersion"] = 31;

        var restored = SimulationEngine.Load(
            save.ToJsonString(),
            SimulationDefinitions.Foundation);

        Assert.Equal(engine.ComputeStateHash(), restored.ComputeStateHash());
        Assert.Equal(59, JsonNode.Parse(restored.Save())!["formatVersion"]!.GetValue<int>());
    }

    [Fact]
    public void VersionThirtyFiveClampsLegacyAnimalFatigueToSpeciesCapacity()
    {
        var save = JsonNode.Parse(CreateEngine().Save())!.AsObject();
        save["formatVersion"] = 35;
        var hare = save["animals"]!.AsArray().First(animal =>
            animal!["kind"]!.GetValue<int>() == (int)AnimalKind.MarshHare)!.AsObject();
        hare["fatigue"] = 12;

        var restored = SimulationEngine.Load(
            save.ToJsonString(),
            SimulationDefinitions.Foundation);
        var restoredHare = restored.CreateSnapshot().Animals.First(animal =>
            animal.Kind == AnimalKind.MarshHare);

        Assert.Equal(restoredHare.MaximumFatigue, restoredHare.Fatigue);
        Assert.Equal(59, JsonNode.Parse(restored.Save())!["formatVersion"]!.GetValue<int>());
    }

    [Fact]
    public void CurrentSaveDoesNotRecreateStarterToolsLostByTheTribe()
    {
        var save = JsonNode.Parse(CreateEngine().Save())!.AsObject();
        var actor = save["actors"]![0]!.AsObject();
        actor["equipment"] = (int)(PersonalEquipment.RagClothes |
            PersonalEquipment.PrimitiveWaterskin);

        var restored = SimulationEngine.Load(
            save.ToJsonString(),
            SimulationDefinitions.Foundation);

        Assert.Equal(
            PersonalEquipment.RagClothes | PersonalEquipment.PrimitiveWaterskin,
            Assert.Single(restored.CreateSnapshot().Actors).Equipment);
    }

    [Fact]
    public void VersionFortyFiveMaterializesSavedHumanCohortsAsVillagers()
    {
        var save = JsonNode.Parse(CreateEngine().Save())!.AsObject();
        save["formatVersion"] = 45;
        save["humanVillage"]!.AsObject().Remove("villagers");

        var restored = SimulationEngine.Load(
            save.ToJsonString(),
            SimulationDefinitions.Foundation);
        var village = restored.CreateSnapshot().HumanVillage;

        Assert.Equal(village.Population, village.Villagers.Count(villager =>
            villager.Health > 0));
        Assert.Equal(
            village.Villagers.Count,
            village.Villagers.Select(villager => villager.Position).Distinct().Count());
        Assert.Equal(
            village.Cohorts.Single(cohort => cohort.Role == HumanCohortRole.Guards).Population,
            village.Villagers.Count(villager =>
                villager.Role == HumanCohortRole.Guards && villager.Health > 0));
        Assert.Equal(59, JsonNode.Parse(restored.Save())!["formatVersion"]!.GetValue<int>());
    }

    [Fact]
    public void VersionFortySixInitializesIndividualHumanNeeds()
    {
        var save = JsonNode.Parse(CreateEngine().Save())!.AsObject();
        save["formatVersion"] = 46;
        foreach (var villager in save["humanVillage"]!["villagers"]!.AsArray())
        {
            villager!.AsObject().Remove("hunger");
            villager.AsObject().Remove("thirst");
        }

        var restored = SimulationEngine.Load(
            save.ToJsonString(),
            SimulationDefinitions.Foundation);

        Assert.All(restored.CreateSnapshot().HumanVillage.Villagers, villager =>
        {
            Assert.Equal(0, villager.Hunger);
            Assert.Equal(0, villager.Thirst);
        });
        Assert.Equal(59, JsonNode.Parse(restored.Save())!["formatVersion"]!.GetValue<int>());
    }

    [Fact]
    public void VersionFortySevenInitializesHumanWorkProgress()
    {
        var save = JsonNode.Parse(CreateEngine().Save())!.AsObject();
        save["formatVersion"] = 47;
        foreach (var villager in save["humanVillage"]!["villagers"]!.AsArray())
        {
            villager!.AsObject().Remove("workProgress");
        }

        var restored = SimulationEngine.Load(
            save.ToJsonString(),
            SimulationDefinitions.Foundation);

        Assert.All(restored.CreateSnapshot().HumanVillage.Villagers,
            villager => Assert.Equal(0, villager.WorkProgress));
        Assert.Equal(59, JsonNode.Parse(restored.Save())!["formatVersion"]!.GetValue<int>());
    }

    [Fact]
    public void VersionFortyEightInitializesHumanFieldWorkProgress()
    {
        var save = JsonNode.Parse(CreateEngine().Save())!.AsObject();
        save["formatVersion"] = 48;
        foreach (var field in save["humanVillage"]!["fields"]!.AsArray())
        {
            field!.AsObject().Remove("workProgress");
        }

        var restored = SimulationEngine.Load(
            save.ToJsonString(),
            SimulationDefinitions.Foundation);

        Assert.All(restored.CreateSnapshot().HumanVillage.Fields,
            field => Assert.Equal(0, field.WorkProgress));
        Assert.Equal(59, JsonNode.Parse(restored.Save())!["formatVersion"]!.GetValue<int>());
    }

    [Fact]
    public void VersionFortyNineInitializesHumanTreeFellingPlan()
    {
        var save = JsonNode.Parse(CreateEngine().Save())!.AsObject();
        save["formatVersion"] = 49;
        var village = save["humanVillage"]!.AsObject();
        village.Remove("treeFellingX");
        village.Remove("treeFellingY");
        village.Remove("treeFellingZ");
        village.Remove("treeFellingProgress");

        var restored = SimulationEngine.Load(
            save.ToJsonString(),
            SimulationDefinitions.Foundation);

        Assert.Null(restored.CreateSnapshot().HumanVillage.TreeFellingTarget);
        Assert.Equal(0, restored.CreateSnapshot().HumanVillage.TreeFellingProgress);
        Assert.Equal(59, JsonNode.Parse(restored.Save())!["formatVersion"]!.GetValue<int>());
    }

    [Fact]
    public void VersionFiftyInitializesHumanGoodsWorkProgress()
    {
        var save = JsonNode.Parse(CreateEngine().Save())!.AsObject();
        save["formatVersion"] = 50;
        save["humanVillage"]!.AsObject().Remove("goodsWorkProgress");

        var restored = SimulationEngine.Load(
            save.ToJsonString(),
            SimulationDefinitions.Foundation);

        Assert.Equal(0, restored.CreateSnapshot().HumanVillage.GoodsWorkProgress);
        Assert.Equal(59, JsonNode.Parse(restored.Save())!["formatVersion"]!.GetValue<int>());
    }

    [Fact]
    public void VersionFiftyOneInitializesHumanStorehouseWork()
    {
        var save = JsonNode.Parse(CreateEngine().Save())!.AsObject();
        save["formatVersion"] = 51;
        var village = save["humanVillage"]!.AsObject();
        village.Remove("storehouseSiteX");
        village.Remove("storehouseSiteY");
        village.Remove("storehouseSiteZ");
        village.Remove("storehouseWorkProgress");

        var restored = SimulationEngine.Load(
            save.ToJsonString(),
            SimulationDefinitions.Foundation);

        Assert.Null(restored.CreateSnapshot().HumanVillage.StorehouseSite);
        Assert.Equal(0, restored.CreateSnapshot().HumanVillage.StorehouseWorkProgress);
        Assert.Equal(59, JsonNode.Parse(restored.Save())!["formatVersion"]!.GetValue<int>());
    }

    [Fact]
    public void VersionFortyFourRaisesPopulationTargetForPreviouslyConstructedHut()
    {
        var engine = SimulationEngine.Create(
            new WorldSeed(0x4855544D494752UL),
            SimulationDefinitions.Foundation,
            initialGoblinCount: 1,
            initialFoodStock: 20,
            initialWoodStock: 20);
        var position = Enumerable.Range(0, engine.Map.Height - 2)
            .SelectMany(y => Enumerable.Range(0, engine.Map.Width - 2)
                .Select(x => new GridPosition(x, y)))
            .Where(engine.World.CanBuildGoblinHut)
            .OrderBy(cell => Math.Abs(cell.X - engine.Map.GoblinSpawn.X) +
                Math.Abs(cell.Y - engine.Map.GoblinSpawn.Y))
            .First();
        engine.QueueCommand(SimulationCommand.BuildGoblinHut(
            new SimulationTick(1), sequence: 1, position));
        engine.AdvanceTicks(1);
        SimulationTestSteps.AdvanceUntilConstructionCompletes(engine, maximumTicks: 12_000);
        var save = JsonNode.Parse(engine.Save())!.AsObject();
        save["formatVersion"] = 44;
        save["populationTarget"] = 1;

        var restored = SimulationEngine.Load(
            save.ToJsonString(),
            SimulationDefinitions.Foundation);

        Assert.Equal(10, restored.CreateSnapshot().PopulationTarget);
        Assert.Equal(59, JsonNode.Parse(restored.Save())!["formatVersion"]!.GetValue<int>());
    }

    [Fact]
    public void VersionFiftySixAssignsDeterministicAnimalSex()
    {
        var save = JsonNode.Parse(CreateEngine().Save())!.AsObject();
        save["formatVersion"] = 56;
        foreach (var animal in save["animals"]!.AsArray())
        {
            animal!.AsObject().Remove("sex");
        }

        var restored = SimulationEngine.Load(
            save.ToJsonString(),
            SimulationDefinitions.Foundation);

        Assert.All(restored.CreateSnapshot().Animals, animal =>
            Assert.Equal(
                animal.Id % 2 == 0 ? AnimalSex.Male : AnimalSex.Female,
                animal.Sex));
        Assert.Equal(59, JsonNode.Parse(restored.Save())!["formatVersion"]!.GetValue<int>());
        var roundTripped = SimulationEngine.Load(
            restored.Save(),
            SimulationDefinitions.Foundation);
        Assert.Equal(restored.ComputeStateHash(), roundTripped.ComputeStateHash());
    }

    [Fact]
    public void SaveFromNewerFormatIsRejectedClearly()
    {
        var save = JsonNode.Parse(CreateEngine().Save())!.AsObject();
        save["formatVersion"] = 60;

        var exception = Assert.Throws<InvalidDataException>(() => SimulationEngine.Load(
            save.ToJsonString(),
            SimulationDefinitions.Foundation));

        Assert.Contains("newer than supported version 59", exception.Message);
    }

    private static SimulationEngine CreateEngine() => SimulationEngine.Create(
        new WorldSeed(0x4D494752415445UL),
        SimulationDefinitions.Foundation,
        initialGoblinCount: 1,
        initialFoodStock: 8,
        initialWoodStock: 8,
        scatterInitialBrushwood: true);
}
