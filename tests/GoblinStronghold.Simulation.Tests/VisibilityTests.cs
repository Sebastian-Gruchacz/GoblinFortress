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
#if DEBUG
        Assert.True(SimulationDebugSettings.ForCurrentBuild.RevealFogFromNonPlayerUnits);
#else
        Assert.False(SimulationDebugSettings.ForCurrentBuild.RevealFogFromNonPlayerUnits);
#endif
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

        engine.AdvanceTicks(200);

        Assert.True(engine.Visibility.DiscoveredCellCount > initialDiscovered);
        Assert.Contains(engine.CreateSnapshot().Visibility, state => state == CellVisibility.Explored);
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
