using System.Text.Json.Nodes;
using GoblinStronghold.Simulation.Map;
using Xunit;

namespace GoblinStronghold.Simulation.Tests;

public sealed class VisibilityTests
{
    [Theory]
    [InlineData(CellVisibility.Unknown, false)]
    [InlineData(CellVisibility.Explored, true)]
    [InlineData(CellVisibility.Visible, true)]
    public void DiscoveredTerrainIncludesFoggedMemory(
        CellVisibility visibility,
        bool expected)
    {
        Assert.Equal(expected, visibility.IsDiscovered());
    }

    [Fact]
    public void CurrentBuildVisibilityAidIsDisabledForRelease()
    {
        Assert.False(SimulationDebugSettings.ForCurrentBuild.RevealFogFromNonPlayerUnits);
    }

    [Fact]
    public void InitialVisibilityRevealsSpawnButNotDistantVillage()
    {
        var engine = CreateEngine();
        var snapshot = engine.CreateSnapshot();

        Assert.Equal(CellVisibility.Visible, snapshot.GetVisibility(engine.Map.GoblinSpawn, engine.Map.Width));
        Assert.Equal(CellVisibility.Unknown, snapshot.GetVisibility(engine.Map.HumanVillage, engine.Map.Width));
        Assert.InRange(
            snapshot.Visibility.Count(state => state == CellVisibility.Visible),
            1,
            snapshot.Visibility.Count - 1);
    }

    [Fact]
    public void OlderSnapshotTreatsNewlyMaterializedLevelAsUnknown()
    {
        var engine = CreateEngine();
        var snapshot = engine.CreateSnapshot();
        var newLevel = engine.Map.DeepestCaveLevel - 1;

        engine.Map.MaterializeCaveLevel(newLevel);

        var position = engine.Map.GoblinSpawn with { Z = newLevel };
        Assert.Equal(CellVisibility.Unknown, snapshot.GetVisibility(position, engine.Map.Width));
        Assert.Equal(
            CellVisibility.Unknown,
            engine.CreateSnapshot().GetVisibility(position, engine.Map.Width));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            snapshot.GetVisibility(position with { X = -1 }, engine.Map.Width));
    }

    [Fact]
    public void VisibilityReservesStableLayersForMaterializedHillVolume()
    {
        var map = SwampMapGenerator.Generate(
            new WorldSeed(0x48494C4C464F47UL),
            width: 64,
            height: 64);
        var engine = SimulationEngine.Create(
            map.Seed,
            SimulationDefinitions.Foundation,
            map,
            initialGoblinCount: 1,
            initialFoodStock: 8);
        var snapshot = engine.CreateSnapshot();
        var expectedLayers = 1 + map.MaterializedNegativeLevelCount +
            map.MaterializedPositiveLevelCount;
        var hillColumn = Enumerable.Range(0, map.Height)
            .SelectMany(y => Enumerable.Range(0, map.Width)
                .Select(x => new GridPosition(x, y)))
            .First(position => map.GetColumnCell(position).SurfaceLevel > 0);
        var hillSurface = map.GetTerrainSurfacePosition(hillColumn);

        Assert.Equal(map.CellCount * expectedLayers, snapshot.Visibility.Count);
        Assert.Equal(CellVisibility.Unknown, snapshot.GetVisibility(hillSurface, map.Width));

        var save = JsonNode.Parse(engine.Save())!.AsObject();
        var legacyLength = map.CellCount * (map.CaveLevelCount + 1);
        save["visibility"] = new JsonArray(Enumerable.Range(0, legacyLength)
            .Select(index => JsonValue.Create(index == 0
                ? CellVisibility.Explored
                : CellVisibility.Unknown))
            .ToArray<JsonNode?>());
        var restored = SimulationEngine.Load(
            save.ToJsonString(),
            SimulationDefinitions.Foundation);
        var restoredSnapshot = restored.CreateSnapshot();
        Assert.Equal(
            CellVisibility.Explored,
            restoredSnapshot.GetVisibility(new GridPosition(0, 0), map.Width));
        Assert.Equal(CellVisibility.Unknown, restoredSnapshot.GetVisibility(hillSurface, map.Width));
        Assert.Equal(map.CellCount * expectedLayers, restoredSnapshot.Visibility.Count);
    }

    [Fact]
    public void HilltopObserverRevealsSurfaceWithoutRevealingBuriedRock()
    {
        var map = SwampMapGenerator.Generate(
            new WorldSeed(0x48494C4C464F47UL),
            width: 64,
            height: 64);
        var column = Enumerable.Range(0, map.Height)
            .SelectMany(y => Enumerable.Range(0, map.Width)
                .Select(x => new GridPosition(x, y)))
            .First(position => map.GetColumnCell(position).SurfaceLevel > 0);
        var hilltop = map.GetTerrainSurfacePosition(column);
        var buried = hilltop with { Z = hilltop.Z - 1 };
        var engine = SimulationEngine.Create(
            map.Seed,
            SimulationDefinitions.Foundation,
            map,
            initialGoblinCount: 1,
            initialFoodStock: 8);
        var save = JsonNode.Parse(engine.Save())!.AsObject();
        save["actors"]![0]!["x"] = hilltop.X;
        save["actors"]![0]!["y"] = hilltop.Y;
        save["actors"]![0]!["z"] = hilltop.Z;
        engine = SimulationEngine.Load(save.ToJsonString(), SimulationDefinitions.Foundation);

        engine.AdvanceTicks(1);

        Assert.Equal(CellVisibility.Visible, engine.Visibility.Get(hilltop));
        Assert.Equal(CellVisibility.Unknown, engine.Visibility.Get(buried));
    }

    [Fact]
    public void GoblinStructuresRevealTheirConfiguredSurroundingsWithoutActors()
    {
        var seed = new WorldSeed(0x535452554354UL);
        var map = SwampMapGenerator.Generate(seed, width: 32, height: 32);
        var engine = SimulationEngine.Create(
            seed,
            SimulationDefinitions.Foundation,
            map,
            initialGoblinCount: 0,
            initialFoodStock: 0);
        var hut = engine.CreateSnapshot().WorldObjects.First(worldObject =>
            worldObject.Owner == WorldObjectOwner.GoblinTribe &&
            worldObject.Kind == WorldObjectKind.GoblinHut);

        var snapshot = engine.CreateSnapshot();

        Assert.Equal(
            CellVisibility.Visible,
            snapshot.GetVisibility(hut.Anchor, engine.Map.Width));
        Assert.True(snapshot.Visibility.Count(state => state == CellVisibility.Visible) > 1);
        Assert.Equal(
            CellVisibility.Unknown,
            snapshot.GetVisibility(engine.Map.HumanVillage, engine.Map.Width));
    }

    [Fact]
    public void DebugVisibilityAlsoRevealsAroundNonPlayerUnits()
    {
        var seed = new WorldSeed(0x4445425547UL);
        var map = SwampMapGenerator.Generate(seed, width: 32, height: 32);
        var engine = SimulationEngine.Create(
            seed,
            SimulationDefinitions.Foundation,
            map,
            initialGoblinCount: 1,
            initialFoodStock: 0,
            debugSettings: new SimulationDebugSettings(
                RevealFogFromNonPlayerUnits: true));

        var snapshot = engine.CreateSnapshot();

        Assert.Equal(
            CellVisibility.Visible,
            snapshot.GetVisibility(engine.Map.HumanVillage, engine.Map.Width));
        Assert.All(snapshot.HumanVillage.Cohorts.Where(cohort => cohort.Population > 0), cohort =>
            Assert.Equal(
                CellVisibility.Visible,
                snapshot.GetVisibility(cohort.Position, engine.Map.Width)));
    }

    [Fact]
    public void ExplorerExpandsFogAndExploredCellsRemainRemembered()
    {
        var engine = CreateEngine();
        var initialDiscovered = engine.Visibility.DiscoveredCellCount;
        engine.QueueCommand(SimulationCommand.DesignateScouting(
            engine.CurrentTick.Next(),
            sequence: 1,
            new GridPosition(0, 0),
            new GridPosition(engine.Map.Width - 1, engine.Map.Height - 1)));

        engine.AdvanceTicks(200);

        Assert.True(engine.Visibility.DiscoveredCellCount > initialDiscovered);
        Assert.Contains(engine.CreateSnapshot().Visibility, state => state == CellVisibility.Explored);
    }

    [Fact]
    public void GoblinsDoNotEnterUnknownTerrainWithoutScoutingDesignation()
    {
        var engine = CreateEngine();
        var initialDiscovered = engine.Visibility.DiscoveredCellCount;

        engine.AdvanceTicks(200);

        Assert.Equal(initialDiscovered, engine.Visibility.DiscoveredCellCount);
        Assert.DoesNotContain(
            engine.CreateSnapshot().Actors,
            actor => actor.Job.Kind == ActorJobKind.Explore);
    }

    [Fact]
    public void SaveLoadPreservesFogAndExplorationOutcome()
    {
        var engine = CreateEngine();
        engine.AdvanceTicks(73);
        var restored = SimulationEngine.Load(engine.Save(), SimulationDefinitions.Foundation);

        Assert.Equal(engine.CreateSnapshot().Visibility, restored.CreateSnapshot().Visibility);
        Assert.Equal(engine.ComputeStateHash(), restored.ComputeStateHash());

        engine.AdvanceTicks(200);
        restored.AdvanceTicks(200);

        Assert.Equal(engine.ComputeStateHash(), restored.ComputeStateHash());
        Assert.Equal(engine.CreateSnapshot().Visibility, restored.CreateSnapshot().Visibility);
    }

    [Fact]
    public void LoadRejectsFogWithWrongCellCount()
    {
        var engine = CreateEngine();
        var save = JsonNode.Parse(engine.Save())?.AsObject()
            ?? throw new InvalidOperationException("The simulation produced invalid JSON.");
        save["visibility"]!.AsArray().RemoveAt(0);

        var exception = Assert.Throws<InvalidDataException>(() =>
            SimulationEngine.Load(save.ToJsonString(), SimulationDefinitions.Foundation));
        Assert.Contains("fog-of-war", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static SimulationEngine CreateEngine()
    {
        var seed = new WorldSeed(0x464F47UL);
        var map = SwampMapGenerator.Generate(seed, width: 32, height: 32);
        return SimulationEngine.Create(
            seed,
            SimulationDefinitions.Foundation,
            map,
            initialGoblinCount: 1,
            initialFoodStock: 0);
    }
}
