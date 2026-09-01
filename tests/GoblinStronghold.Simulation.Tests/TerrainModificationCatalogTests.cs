using System.Text;
using GoblinStronghold.Simulation.ContentPacks;
using GoblinStronghold.Simulation.Map;
using GoblinStronghold.Simulation.Planning;
using GoblinStronghold.Simulation.Resources;
using GoblinStronghold.Simulation.Terrain;
using Xunit;

namespace GoblinStronghold.Simulation.Tests;

public sealed class TerrainModificationCatalogTests
{
    [Fact]
    public void CatalogDefinesEveryCurrentTerrainModification()
    {
        Assert.Equal(3, TerrainModificationCatalog.All.Count);
        Assert.Equal(
            [
                WorkDesignationKind.MineRock,
                WorkDesignationKind.CarveRampDown,
                WorkDesignationKind.CarveRampUp,
            ],
            TerrainModificationCatalog.All.Select(item => item.LegacyDesignation));
    }

    [Fact]
    public void StableAndLegacyIdsResolveToTheSameDefinition()
    {
        var legacyId = TerrainModificationCatalog.Get("mine-rock");
        var stableId = TerrainModificationCatalog.Get("core:mine-rock");
        var legacyEnum = TerrainModificationCatalog.Get(WorkDesignationKind.MineRock);

        Assert.Same(legacyId, stableId);
        Assert.Same(legacyId, legacyEnum);
        Assert.Equal("core:mine-rock", legacyId.StableId.Value);
    }

    [Fact]
    public void MiningAndCarvedRampsKeepDifferentPlacementGestures()
    {
        Assert.Equal(
            WorldToolPlacementMode.Area,
            TerrainModificationCatalog.Get(WorkDesignationKind.MineRock).PlacementMode);
        Assert.Equal(
            WorldToolPlacementMode.Point,
            TerrainModificationCatalog.Get(WorkDesignationKind.CarveRampDown).PlacementMode);
        Assert.Equal(
            WorldToolPlacementMode.Point,
            TerrainModificationCatalog.Get(WorkDesignationKind.CarveRampUp).PlacementMode);
    }

    [Fact]
    public void TerrainToolsHaveTheirOwnMenuBranch()
    {
        Assert.All(TerrainModificationCatalog.All, definition =>
            Assert.Equal(["terrain", "excavation"], definition.MenuPath));
    }

    [Fact]
    public void CoreDefinitionsContainValidatedExecutionAndYieldData()
    {
        var mining = TerrainModificationCatalog.Get(WorkDesignationKind.MineRock);
        var ramp = TerrainModificationCatalog.Get(WorkDesignationKind.CarveRampDown);

        Assert.Equal(8, mining.Work.BaseTicksMultiplier);
        Assert.Equal(12, ramp.Work.BaseTicksMultiplier);
        Assert.Equal(ResourceKind.Stone, mining.Work.Yield.Resource);
        Assert.True(mining.Work.Yield.VariantFromRock);
        Assert.Equal(8, mining.Work.Yield.Deposits.Count);
        Assert.Empty(ramp.Work.Yield.Deposits);
        Assert.Equal(ResourceVariant.Diamond,
            mining.Work.Yield.Deposits[MineralDepositKind.Diamond].Variant);
    }

    [Fact]
    public void InvalidExecutionValuesRejectTheWholeTerrainCatalog()
    {
        using var source = CoreContentPack.Pack.OpenRead("content/terrain-modifications.json");
        using var reader = new StreamReader(source, Encoding.UTF8);
        var invalidJson = reader.ReadToEnd().Replace(
            "\"baseTicksMultiplier\": 8",
            "\"baseTicksMultiplier\": 0",
            StringComparison.Ordinal);
        using var invalid = new MemoryStream(Encoding.UTF8.GetBytes(invalidJson));

        Assert.Throws<InvalidOperationException>(() =>
            TerrainModificationCatalog.LoadDefinitions(invalid));
    }

    [Theory]
    [InlineData(WorkDesignationKind.MineRock, 3, 3)]
    [InlineData(WorkDesignationKind.CarveRampDown, 2, 2)]
    [InlineData(WorkDesignationKind.CarveRampUp, 2, 2)]
    public void CommandFactoryPreservesLegacyCommandContract(
        WorkDesignationKind kind,
        int expectedEndX,
        int expectedEndY)
    {
        var executeAt = new SimulationTick(17);
        var start = new GridPosition(2, 2, -1);
        var end = new GridPosition(3, 3, -1);

        var command = TerrainModificationCommandFactory.CreateDesignation(
            TerrainModificationCatalog.Get(kind),
            executeAt,
            sequence: 9,
            start,
            end);

        Assert.Equal(SimulationCommandKind.DesignateWork, command.Kind);
        Assert.Equal(executeAt, command.ExecuteAt);
        Assert.Equal(9UL, command.Sequence);
        Assert.Equal(start, command.Position);
        Assert.Equal(new GridPosition(expectedEndX, expectedEndY, -1), command.EndPosition);
        Assert.Equal(ResourceKind.Any, command.Resource);
        Assert.Equal((int)kind, command.Amount);
    }
}
