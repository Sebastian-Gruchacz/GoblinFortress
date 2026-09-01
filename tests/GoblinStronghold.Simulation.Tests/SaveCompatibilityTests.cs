using System.Text.Json.Nodes;
using GoblinStronghold.Simulation.Civilizations.Polities;
using GoblinStronghold.Simulation.Map;
using GoblinStronghold.Simulation.Map.Generation;
using Xunit;

namespace GoblinStronghold.Simulation.Tests;

public sealed class SaveCompatibilityTests
{
    [Fact]
    public void CurrentBaselineRoundTripsWithoutMigration()
    {
        var source = CreateEngine();

        var restored = SimulationEngine.Load(
            source.Save(),
            SimulationDefinitions.Foundation);

        Assert.Equal(source.ComputeStateHash(), restored.ComputeStateHash());
        Assert.Equal(
            SimulationSaveFormat.CurrentVersion,
            JsonNode.Parse(restored.Save())!["formatVersion"]!.GetValue<int>());
        Assert.Equal(SwampMapGenerator.DefaultProfileId, restored.Map.ProfileId);
        Assert.Equal(RiverGenerationMode.SingleChannel, restored.Map.RiverMode);
        var snapshot = restored.CreateSnapshot();
        Assert.Equal(CorePolityIds.PlayerTribe, snapshot.PlayerPolityId);
        Assert.Equal(CorePolityIds.HumanVillage, snapshot.HumanVillage.PolityId);
        Assert.Equal(
            snapshot.UndergroundFactions.Count,
            snapshot.UndergroundFactions.Select(faction => faction.PolityId).Distinct().Count());
    }

    [Fact]
    public void Format72MigratesToExistingSingleRiverChannel()
    {
        var save = JsonNode.Parse(CreateEngine().Save())!.AsObject();
        save["formatVersion"] = SimulationSaveFormat.RiverModeMigrationVersion;
        save.Remove("mapRiverMode");

        var restored = SimulationEngine.Load(
            save.ToJsonString(),
            SimulationDefinitions.Foundation);
        var migrated = JsonNode.Parse(restored.Save())!.AsObject();

        Assert.Equal(RiverGenerationMode.SingleChannel, restored.Map.RiverMode);
        Assert.Equal(
            (int)RiverGenerationMode.SingleChannel,
            migrated["mapRiverMode"]!.GetValue<int>());
        Assert.Equal(
            SimulationSaveFormat.CurrentVersion,
            migrated["formatVersion"]!.GetValue<int>());
    }

    [Fact]
    public void Format73KeepsGoblinsSexlessAndAssignsHumanSexDeterministically()
    {
        var save = JsonNode.Parse(CreateEngine().Save())!.AsObject();
        save["formatVersion"] = SimulationSaveFormat.ActorSexMigrationVersion;
        foreach (var actor in save["actors"]!.AsArray())
        {
            actor!.AsObject().Remove("sex");
        }
        foreach (var villager in save["humanVillage"]!["villagers"]!.AsArray())
        {
            villager!.AsObject().Remove("sex");
        }

        var restored = SimulationEngine.Load(
            save.ToJsonString(),
            SimulationDefinitions.Foundation);
        var snapshot = restored.CreateSnapshot();

        Assert.All(snapshot.Actors, actor => Assert.Equal(ActorSex.Sexless, actor.Sex));
        Assert.All(snapshot.HumanVillage.Villagers, villager => Assert.Equal(
            villager.Id % 2 == 0 ? ActorSex.Male : ActorSex.Female,
            villager.Sex));
        Assert.Equal(
            SimulationSaveFormat.CurrentVersion,
            JsonNode.Parse(restored.Save())!["formatVersion"]!.GetValue<int>());
    }

    [Fact]
    public void Format74AddsStablePolityIds()
    {
        var save = JsonNode.Parse(CreateEngine().Save())!.AsObject();
        save["formatVersion"] = SimulationSaveFormat.PolityIdMigrationVersion;
        save.Remove("playerPolityId");
        save["humanVillage"]!.AsObject().Remove("polityId");
        foreach (var faction in save["undergroundFactions"]!.AsArray())
        {
            faction!.AsObject().Remove("polityId");
        }

        var restored = SimulationEngine.Load(
            save.ToJsonString(),
            SimulationDefinitions.Foundation);
        var snapshot = restored.CreateSnapshot();

        Assert.Equal(CorePolityIds.PlayerTribe, snapshot.PlayerPolityId);
        Assert.Equal(CorePolityIds.HumanVillage, snapshot.HumanVillage.PolityId);
        Assert.All(snapshot.UndergroundFactions, faction => Assert.Equal(
            CorePolityIds.CaveDwarfClan(faction.Id),
            faction.PolityId));
        Assert.Equal(
            SimulationSaveFormat.CurrentVersion,
            JsonNode.Parse(restored.Save())!["formatVersion"]!.GetValue<int>());
    }

    [Fact]
    public void UnknownPlayerPolityIsRejected()
    {
        var save = JsonNode.Parse(CreateEngine().Save())!.AsObject();
        save["playerPolityId"] = "core:other-tribe";

        var exception = Assert.Throws<InvalidDataException>(() =>
            SimulationEngine.Load(
                save.ToJsonString(),
                SimulationDefinitions.Foundation));

        Assert.Contains("unsupported player polity", exception.Message);
    }

    [Fact]
    public void BranchingRiverModeRoundTripsInCurrentFormat()
    {
        var seed = new WorldSeed(0x534156454252414EUL);
        var map = SwampMapGenerator.Generate(
            LocationGenerationRequest.CreateDefault(seed, 64, 64) with
            {
                RiverMode = RiverGenerationMode.BranchingChannels,
            });
        var source = SimulationEngine.Create(
            seed,
            SimulationDefinitions.Foundation,
            map,
            initialGoblinCount: 4,
            initialFoodStock: 40);

        var restored = SimulationEngine.Load(
            source.Save(),
            SimulationDefinitions.Foundation);

        Assert.Equal(RiverGenerationMode.BranchingChannels, restored.Map.RiverMode);
        Assert.Equal(source.Map.ComputeFingerprint(), restored.Map.ComputeFingerprint());
        Assert.Equal(source.ComputeStateHash(), restored.ComputeStateHash());
    }

    [Fact]
    public void Format71MigratesToExplicitDefaultLocationProfile()
    {
        var save = JsonNode.Parse(CreateEngine().Save())!.AsObject();
        save["formatVersion"] = SimulationSaveFormat.LocationProfileMigrationVersion;
        save.Remove("mapProfileId");

        var restored = SimulationEngine.Load(
            save.ToJsonString(),
            SimulationDefinitions.Foundation);
        var migrated = JsonNode.Parse(restored.Save())!.AsObject();

        Assert.Equal(SwampMapGenerator.DefaultProfileId, restored.Map.ProfileId);
        Assert.Equal(
            SwampMapGenerator.DefaultProfileId.Value,
            migrated["mapProfileId"]!.GetValue<string>());
        Assert.Equal(
            SimulationSaveFormat.CurrentVersion,
            migrated["formatVersion"]!.GetValue<int>());
    }

    [Fact]
    public void Format70MigratesToCleanSurfaceGrimeState()
    {
        var save = JsonNode.Parse(CreateEngine().Save())!.AsObject();
        save["formatVersion"] = SimulationSaveFormat.SurfaceGrimeMigrationVersion;
        save.Remove("surfaceGrime");
        foreach (var actor in save["actors"]!.AsArray())
        {
            actor!.AsObject().Remove("carriedGrime");
        }
        foreach (var animal in save["animals"]!.AsArray())
        {
            animal!.AsObject().Remove("carriedGrime");
        }
        foreach (var villager in save["humanVillage"]!["villagers"]!.AsArray())
        {
            villager!.AsObject().Remove("carriedGrime");
        }

        var restored = SimulationEngine.Load(
            save.ToJsonString(),
            SimulationDefinitions.Foundation);
        var snapshot = restored.CreateSnapshot();

        Assert.Empty(snapshot.SurfaceGrime);
        Assert.All(snapshot.Animals, animal => Assert.Equal(0, animal.CarriedGrime));
        Assert.All(snapshot.HumanVillage.Villagers, villager =>
            Assert.Equal(0, villager.CarriedGrime));
        var migrated = JsonNode.Parse(restored.Save())!.AsObject();
        Assert.Equal(
            SimulationSaveFormat.CurrentVersion,
            migrated["formatVersion"]!.GetValue<int>());
        Assert.All(migrated["actors"]!.AsArray(), actor =>
            Assert.Equal(0, actor!["carriedGrime"]!.GetValue<int>()));
    }

    [Theory]
    [InlineData(61)]
    [InlineData(63)]
    public void NonBaselineFormatIsRejected(int formatVersion)
    {
        var save = JsonNode.Parse(CreateEngine().Save())!.AsObject();
        save["formatVersion"] = formatVersion;

        var exception = Assert.Throws<InvalidDataException>(() =>
            SimulationEngine.Load(
                save.ToJsonString(),
                SimulationDefinitions.Foundation));

        Assert.Contains("obsolete or incompatible", exception.Message);
        Assert.Contains(SimulationSaveFormat.CurrentVersion.ToString(), exception.Message);
    }

    [Fact]
    public void UnknownLocationProfileIsRejectedBeforeMapRegeneration()
    {
        var save = JsonNode.Parse(CreateEngine().Save())!.AsObject();
        save["mapProfileId"] = "core:missing-location";

        var exception = Assert.Throws<InvalidDataException>(() =>
            SimulationEngine.Load(
                save.ToJsonString(),
                SimulationDefinitions.Foundation));

        Assert.Contains("unsupported map profile", exception.Message);
    }

    [Fact]
    public void UnknownRiverModeIsRejectedBeforeMapRegeneration()
    {
        var save = JsonNode.Parse(CreateEngine().Save())!.AsObject();
        save["mapRiverMode"] = 255;

        var exception = Assert.Throws<InvalidDataException>(() =>
            SimulationEngine.Load(
                save.ToJsonString(),
                SimulationDefinitions.Foundation));

        Assert.Contains("unsupported river mode", exception.Message);
    }

    [Fact]
    public void CurrentSaveProjectsLegacySurfaceGroundStackOntoMaterialSurface()
    {
        var seed = new WorldSeed(0x47524F554E445AUL);
        var map = SwampMapGenerator.Generate(seed, width: 48, height: 48);
        var source = SimulationEngine.Create(
            seed,
            SimulationDefinitions.Foundation,
            map,
            initialGoblinCount: 1,
            initialFoodStock: 1);
        var materialSurface = Enumerable.Range(0, map.Height)
            .SelectMany(y => Enumerable.Range(0, map.Width)
                .Select(x => map.GetTerrainSurfacePosition(new GridPosition(x, y, 0))))
            .First(position => position.Z != 0 &&
                map.TryGetInitialGeometry(position, out var geometry) &&
                geometry.Support == CellSupportKind.NaturalFlat &&
                geometry.FluidDepthLevels == 0);
        var save = JsonNode.Parse(source.Save())!.AsObject();
        var stack = save["itemStacks"]!.AsArray().Single()!.AsObject();
        stack["x"] = materialSurface.X;
        stack["y"] = materialSurface.Y;
        stack["z"] = 0;

        var restored = SimulationEngine.Load(
            save.ToJsonString(),
            SimulationDefinitions.Foundation);

        Assert.Equal(
            materialSurface,
            Assert.Single(restored.CreateSnapshot().ItemStacks).Location.Position);
    }

    private static SimulationEngine CreateEngine() => SimulationEngine.Create(
        new WorldSeed(0x5341564542415345UL),
        SimulationDefinitions.Foundation,
        initialGoblinCount: 4,
        initialFoodStock: 40);
}
