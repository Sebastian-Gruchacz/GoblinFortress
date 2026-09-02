using GoblinStronghold.Simulation.Construction;
using GoblinStronghold.Simulation.Map;
using GoblinStronghold.Simulation.Planning;
using GoblinStronghold.Simulation.Resources;
using GoblinStronghold.Simulation.Workshops;
using Xunit;

namespace GoblinStronghold.Simulation.Tests;

public sealed class ConstructionBlueprintCatalogTests
{
    [Fact]
    public void CatalogDefinesEveryLegacyConstructionKindExactlyOnce()
    {
        Assert.Equal(
            Enum.GetValues<ConstructionKind>().Length,
            ConstructionBlueprintDefinitions.All.Count);

        foreach (var kind in Enum.GetValues<ConstructionKind>())
        {
            var definition = ConstructionBlueprintDefinitions.Get(kind);

            Assert.Equal(kind, definition.Kind);
            Assert.StartsWith("core:", definition.StableId.Value);
            Assert.True(definition.MaterialQuantity > 0);
            Assert.True(definition.WorkTicks > 0);
        }
    }

    [Fact]
    public void LegacyAndStableIdsResolveToTheSameBlueprint()
    {
        var legacy = ConstructionBlueprintDefinitions.Get("stone-wall");
        var stable = ConstructionBlueprintDefinitions.Get("core:stone-wall");

        Assert.Same(legacy, stable);
        Assert.Equal(ConstructionKind.StoneWall, legacy.Kind);
    }

    [Fact]
    public void LinearBlueprintScalesMaterialsAndWorkByFootprint()
    {
        var definition = ConstructionBlueprintDefinitions.Get(ConstructionKind.StoneWall);
        var start = new GridPosition(3, 4, 0);
        var end = start with { X = 5 };
        var footprint = definition.GetFootprint(start, end);

        Assert.Equal(3, footprint.Count);
        Assert.Equal(6, definition.GetRequiredQuantity(footprint.Count));
        Assert.Equal(180, definition.GetWorkTicks(footprint.Count));
        Assert.Equal(ResourceKind.Stone, definition.RequiredResource);
        Assert.True(definition.RetainsMaterialIdentity);
    }

    [Fact]
    public void FixedRectangleBlueprintOwnsItsFootprintGeometry()
    {
        var definition = ConstructionBlueprintDefinitions.Get(ConstructionKind.GoblinHut);
        var anchor = new GridPosition(8, 9, -1);
        var footprint = definition.GetFootprint(anchor, anchor);

        Assert.Equal(9, footprint.Count);
        Assert.Contains(anchor with { X = 10, Y = 11 }, footprint);
        Assert.All(footprint, cell => Assert.Equal(anchor.Z, cell.Z));
    }

    [Fact]
    public void WorkshopBlueprintUsesWorkshopConstructionQuantity()
    {
        var definition = ConstructionBlueprintDefinitions.Get(ConstructionKind.Bloomery);
        var workshop = WorkshopCatalog.Get(WorkshopKind.Bloomery);

        Assert.Equal(WorkshopKind.Bloomery, definition.Workshop);
        Assert.Equal(
            workshop.ConstructionRequirements.Sum(item => item.Quantity),
            definition.GetRequiredQuantity(1));
    }

    [Fact]
    public void EquipmentBlueprintKeepsRequiredFinishedVariant()
    {
        var definition = ConstructionBlueprintDefinitions.Get(ConstructionKind.WoodenChest);

        Assert.Equal(ResourceKind.Equipment, definition.RequiredResource);
        Assert.Equal(ResourceVariant.EquipmentWoodenChest, definition.RequiredVariant);
        Assert.False(definition.RetainsMaterialIdentity);
    }

    [Fact]
    public void StoneWalkwayAcceptsASelectedConstructionStone()
    {
        var start = new GridPosition(4, 5, -1);
        var end = start with { X = 6 };
        var site = ConstructionBlueprintCatalog.CreateSite(
            new EntityId(1),
            ConstructionKind.BasaltWalkway,
            start,
            end,
            requiredVariantOverride: ResourceVariant.Granite);
        var command = SimulationCommand.BuildBasaltWalkway(
            SimulationTick.Zero,
            sequence: 1,
            start,
            end,
            ResourceVariant.Granite);

        Assert.Equal(ResourceVariant.Granite, site.RequiredVariant);
        Assert.Equal(ResourceVariant.Granite, command.MaterialVariant);
        Assert.True(ConstructionBlueprintCatalog.RetainsMaterialIdentity(site.Kind));
    }

    [Fact]
    public void PlanningModesSeparateBlueprintsDesignationsAndSimpleObjects()
    {
        Assert.Equal(
            ConstructionPlanningMode.BuildingBlueprint,
            ConstructionBlueprintDefinitions.Get(ConstructionKind.GoblinHut).PlanningMode);
        Assert.Equal(
            ConstructionPlanningMode.CellDesignation,
            ConstructionBlueprintDefinitions.Get(ConstructionKind.StoneFloor).PlanningMode);
        Assert.Equal(
            ConstructionPlanningMode.SimplePlacement,
            ConstructionBlueprintDefinitions.Get(ConstructionKind.WoodenDoor).PlanningMode);
    }

    [Fact]
    public void PlacementModesDescribeGestureWithoutInspectingLegacyKind()
    {
        Assert.Equal(
            WorldToolPlacementMode.Line,
            ConstructionBlueprintDefinitions.Get(ConstructionKind.StoneWall).PlacementMode);
        Assert.Equal(
            WorldToolPlacementMode.Area,
            ConstructionBlueprintDefinitions.Get(ConstructionKind.WoodenFloor).PlacementMode);
        Assert.Equal(
            WorldToolPlacementMode.FixedFootprint,
            ConstructionBlueprintDefinitions.Get(ConstructionKind.GoblinHut).PlacementMode);
        Assert.Equal(
            WorldToolPlacementMode.DirectionalConnection,
            ConstructionBlueprintDefinitions.Get(ConstructionKind.StoneRamp).PlacementMode);
    }

    [Fact]
    public void DoorAndFrameCommandsRetainSelectedMaterialVariants()
    {
        var position = new GridPosition(4, 7, -2);
        var frame = SimulationCommand.BuildStoneDoorFrame(
            new SimulationTick(3), 1, position, ResourceVariant.Granite);
        var door = SimulationCommand.BuildWoodenDoor(
            new SimulationTick(3), 2, position, ResourceVariant.OakWood);

        Assert.Equal(ResourceVariant.Granite, frame.MaterialVariant);
        Assert.Equal(ResourceVariant.OakWood, door.MaterialVariant);
    }

    [Fact]
    public void MenuPathsCanBuildArbitrarilyNestedMenus()
    {
        Assert.Equal(
            ["basic", "terrain", "advanced"],
            ConstructionBlueprintDefinitions.GetMenuChildren());
        Assert.Equal(
            ["storage", "structures", "structural", "lighting", "workshops"],
            ConstructionBlueprintDefinitions.GetMenuChildren("basic"));

        var structures = ConstructionBlueprintDefinitions.GetMenuBlueprints(
            "basic",
            "structures");

        Assert.Equal(
            [ConstructionKind.GoblinFieldCamp, ConstructionKind.GoblinHut],
            structures.Select(definition => definition.Kind));
        Assert.Equal(
            [ConstructionKind.WoodenWalkway, ConstructionKind.BasaltWalkway],
            ConstructionBlueprintDefinitions.GetMenuBlueprints("terrain", "routes")
                .Select(definition => definition.Kind));
        Assert.Equal(
            [ConstructionKind.Bloomery, ConstructionKind.SmeltingFurnace,
                ConstructionKind.CrucibleFurnace],
            ConstructionBlueprintDefinitions.GetMenuBlueprints("advanced", "production")
                .Select(definition => definition.Kind));
    }
}
