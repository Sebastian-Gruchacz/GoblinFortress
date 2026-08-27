using System.Text.Json.Nodes;
using GoblinStronghold.Simulation.Map;
using GoblinStronghold.Simulation.Resources;
using Xunit;

namespace GoblinStronghold.Simulation.Tests;

public sealed class SaveMigrationTests
{
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

        Assert.Equal(36, migrated["formatVersion"]!.GetValue<int>());
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
        Assert.Equal(36, JsonNode.Parse(restored.Save())!["formatVersion"]!.GetValue<int>());
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
        Assert.Equal(36, JsonNode.Parse(restored.Save())!["formatVersion"]!.GetValue<int>());
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
        Assert.Equal(36, JsonNode.Parse(restored.Save())!["formatVersion"]!.GetValue<int>());
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
    public void SaveFromNewerFormatIsRejectedClearly()
    {
        var save = JsonNode.Parse(CreateEngine().Save())!.AsObject();
        save["formatVersion"] = 37;

        var exception = Assert.Throws<InvalidDataException>(() => SimulationEngine.Load(
            save.ToJsonString(),
            SimulationDefinitions.Foundation));

        Assert.Contains("newer than supported version 36", exception.Message);
    }

    private static SimulationEngine CreateEngine() => SimulationEngine.Create(
        new WorldSeed(0x4D494752415445UL),
        SimulationDefinitions.Foundation,
        initialGoblinCount: 1,
        initialFoodStock: 8,
        initialWoodStock: 8,
        scatterInitialBrushwood: true);
}
