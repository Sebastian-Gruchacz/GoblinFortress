using System.Text.Json.Nodes;
using GoblinStronghold.Simulation.Equipment;
using GoblinStronghold.Simulation.Map;
using GoblinStronghold.Simulation.Resources;
using Xunit;

namespace GoblinStronghold.Simulation.Tests;

public sealed class FloorConstructionTests
{
    [Fact]
    public void WoodenFloorAreaKeepsSelectedMaterialAndSurvivesSaveLoad()
    {
        var engine = CreateWithMaterial(
            new WorldSeed(0x574F4F44464C4F4FUL),
            ResourceKind.Wood,
            ResourceVariant.OakWood,
            quantity: 4);
        var cells = FindFloorRectangle(engine, width: 2, height: 2);
        engine.QueueCommand(SimulationCommand.BuildWoodenFloor(
            new SimulationTick(1),
            sequence: 1,
            cells[0],
            cells[^1],
            ResourceVariant.OakWood));
        var pendingRestored = SimulationEngine.Load(
            engine.Save(), SimulationDefinitions.Foundation);
        Assert.Equal(engine.ComputeStateHash(), pendingRestored.ComputeStateHash());

        engine.AdvanceTicks(1);

        var sites = engine.CreateSnapshot().ConstructionSites.OrderBy(site => site.Id).ToArray();
        Assert.Equal(4, sites.Length);
        Assert.All(sites, site =>
        {
            Assert.Equal(ConstructionKind.WoodenFloor, site.Kind);
            var material = Assert.Single(site.Materials);
            Assert.Equal(ResourceVariant.OakWood, material.Variant);
            Assert.Equal(1, material.RequiredQuantity);
        });
        var restored = SimulationEngine.Load(engine.Save(), SimulationDefinitions.Foundation);
        Assert.Equal(engine.ComputeStateHash(), restored.ComputeStateHash());

        SimulationTestSteps.AdvanceUntilConstructionCompletes(engine);
        SimulationTestSteps.AdvanceUntilConstructionCompletes(restored);

        Assert.Equal(engine.ComputeStateHash(), restored.ComputeStateHash());
        Assert.All(cells, cell =>
        {
            var floor = Assert.Single(engine.World.GetWorldObjectsAt(cell)
                .Where(worldObject => worldObject.Kind == WorldObjectKind.WoodenFloor));
            Assert.Equal(ResourceVariant.OakWood, floor.MaterialVariant);
            Assert.Equal(WorldObjectPartKind.Floor, Assert.Single(floor.Parts).Kind);
            Assert.True(engine.World.IsTerrainTraversable(cell));
        });
    }

    [Fact]
    public void StoneFloorConsumesOnlyTheSelectedStone()
    {
        var engine = CreateWithMaterial(
            new WorldSeed(0x53544F4E464C4F4FUL),
            ResourceKind.Stone,
            ResourceVariant.Sandstone,
            quantity: 1);
        var saveWithHammer = JsonNode.Parse(engine.Save())!.AsObject();
        Assert.Single(saveWithHammer["actors"]!.AsArray())!["equipment"] =
            (int)(PersonalEquipment.RagClothes | PersonalEquipment.WoodenHammer);
        engine = SimulationEngine.Load(
            saveWithHammer.ToJsonString(),
            SimulationDefinitions.Foundation);
        var position = FindFloorRectangle(engine, width: 1, height: 1)[0];
        engine.QueueCommand(SimulationCommand.BuildStoneFloor(
            new SimulationTick(1),
            sequence: 1,
            position,
            position,
            ResourceVariant.Sandstone));

        engine.AdvanceTicks(1);
        var material = Assert.Single(Assert.Single(
            engine.CreateSnapshot().ConstructionSites).Materials);
        Assert.Equal(ResourceKind.Stone, material.Resource);
        Assert.Equal(ResourceVariant.Sandstone, material.Variant);

        SimulationTestSteps.AdvanceUntilConstructionCompletes(engine);

        var floor = Assert.Single(engine.World.GetWorldObjectsAt(position)
            .Where(worldObject => worldObject.Kind == WorldObjectKind.StoneFloor));
        Assert.Equal(ResourceVariant.Sandstone, floor.MaterialVariant);
        Assert.True(engine.World.IsTerrainTraversable(position));
    }

    [Theory]
    [InlineData(SimulationSaveFormat.ConstructionToolsMigrationVersion)]
    [InlineData(SimulationSaveFormat.ConstructionHammerMigrationVersion)]
    [InlineData(SimulationSaveFormat.ConstructionToolLevelsMigrationVersion)]
    [InlineData(SimulationSaveFormat.ConstructionToolFunctionsMigrationVersion)]
    public void LegacyConstructionRequirementIsReplacedWithToolFunctionAndLevel(
        int formatVersion)
    {
        var engine = CreateWithMaterial(
            new WorldSeed(0x544F4F4C4D494752UL),
            ResourceKind.Stone,
            ResourceVariant.Sandstone,
            quantity: 1);
        var position = FindFloorRectangle(engine, width: 1, height: 1)[0];
        engine.QueueCommand(SimulationCommand.BuildStoneFloor(
            new SimulationTick(1),
            sequence: 1,
            position,
            position,
            ResourceVariant.Sandstone));
        engine.AdvanceTicks(1);
        var save = JsonNode.Parse(engine.Save())!.AsObject();
        save["formatVersion"] = formatVersion;
        Assert.Single(save["constructionSites"]!.AsArray())!["requiredEquipment"] =
            (int)PersonalEquipment.PrimitivePickaxe;
        Assert.Single(save["constructionSites"]!.AsArray())!["requiredToolFunction"] =
            (int)ToolFunction.None;
        Assert.Single(save["constructionSites"]!.AsArray())!["minimumToolLevel"] = 0;

        var restored = SimulationEngine.Load(
            save.ToJsonString(),
            SimulationDefinitions.Foundation);

        Assert.Equal(
            PersonalEquipment.None,
            Assert.Single(restored.CreateSnapshot().ConstructionSites)
                .Capabilities.RequiredEquipment);
        Assert.Equal(
            ToolFunction.Construction,
            Assert.Single(restored.CreateSnapshot().ConstructionSites)
                .Capabilities.RequiredToolFunction);
        Assert.Equal(
            1,
            Assert.Single(restored.CreateSnapshot().ConstructionSites)
                .Capabilities.MinimumToolLevel);
        Assert.Equal(
            SimulationSaveFormat.CurrentVersion,
            JsonNode.Parse(restored.Save())!["formatVersion"]!.GetValue<int>());
    }

    [Fact]
    public void FloorCanBeBuiltAtTheLowerEndOfAnExcavatedRamp()
    {
        var seed = new WorldSeed(0x4C4F574552464C52UL);
        var map = SwampMapGenerator.Generate(seed, width: 48, height: 48);
        var engine = SimulationEngine.Create(
            seed,
            SimulationDefinitions.Foundation,
            map,
            initialGoblinCount: 1,
            initialFoodStock: 0,
            initialWoodStock: 0);
        var upper =
            (from y in Enumerable.Range(0, map.Height)
             from x in Enumerable.Range(0, map.Width)
             let candidate = new GridPosition(x, y, -1)
             where engine.World.CanCarveRampDown(candidate)
             select candidate).First();
        var lower = upper with { Z = upper.Z - 1 };
        Assert.True(engine.World.TryCarveVerticalRamp(
            upper,
            carveDown: true,
            SimulationTick.Zero,
            out _,
            out _));

        Assert.True(engine.World.CanPlanFloorConstruction([upper]));
        Assert.True(engine.World.CanPlanFloorConstruction([lower]));

        engine.World.BuildFloor(
            upper,
            SimulationTick.Zero,
            stone: false,
            ResourceVariant.OakWood);
        engine.World.BuildFloor(
            lower,
            SimulationTick.Zero,
            stone: true,
            ResourceVariant.Sandstone);

        Assert.True(engine.World.HasConstructedFloorSurface(upper));
        Assert.True(engine.World.HasConstructedFloorSurface(lower));
        Assert.NotNull(engine.Navigation.FindPath(upper, lower));
    }

    [Fact]
    public void FloorCoverCanBeBuiltBelowAnExistingWorkshop()
    {
        var engine = SimulationEngine.Create(
            new WorldSeed(0x554E44455253484FUL),
            SimulationDefinitions.Foundation,
            initialGoblinCount: 4,
            initialFoodStock: 0,
            initialWoodStock: 8);
        var position = engine.Map.GetCardinalNeighbors(engine.Map.GoblinSpawn)
            .First(engine.World.CanBuildPrimitiveWorkshop);
        engine.QueueCommand(SimulationCommand.BuildPrimitiveWorkshop(
            engine.CurrentTick.Next(),
            engine.NextAvailableCommandSequence,
            position));
        for (var tick = 0; tick < 5_000 &&
             !engine.World.HasPrimitiveWorkshop(position); tick++)
        {
            engine.AdvanceTicks(1);
        }
        Assert.True(engine.World.HasPrimitiveWorkshop(position));

        Assert.True(engine.World.CanPlanFloorConstruction([position]));
        engine.World.BuildFloor(
            position,
            engine.CurrentTick,
            stone: false,
            ResourceVariant.OakWood);

        Assert.True(engine.World.HasPrimitiveWorkshop(position));
        Assert.True(engine.World.HasConstructedFloorSurface(position));
        Assert.Contains(engine.World.GetWorldObjectsAt(position), worldObject =>
            worldObject.Kind == WorldObjectKind.PrimitiveWorkshop);
        Assert.Contains(engine.World.GetWorldObjectsAt(position), worldObject =>
            worldObject.Kind == WorldObjectKind.WoodenFloor);
    }

    private static SimulationEngine CreateWithMaterial(
        WorldSeed seed,
        ResourceKind resource,
        ResourceVariant variant,
        int quantity)
    {
        var generated = SimulationEngine.Create(
            seed,
            SimulationDefinitions.Foundation,
            initialGoblinCount: 1,
            initialFoodStock: 0,
            initialWoodStock: resource == ResourceKind.Wood ? quantity : 0,
            scatterInitialBrushwood: resource == ResourceKind.Stone);
        var save = JsonNode.Parse(generated.Save())!.AsObject();
        var stack = save["itemStacks"]!.AsArray()
            .Select(node => node!.AsObject())
            .First(model => model["resource"]!.GetValue<int>() == (int)resource);
        stack["x"] = generated.Map.GoblinSpawn.X;
        stack["y"] = generated.Map.GoblinSpawn.Y;
        stack["z"] = generated.Map.GoblinSpawn.Z;
        stack["quantity"] = quantity;
        stack["variant"] = (int)variant;
        return SimulationEngine.Load(save.ToJsonString(), SimulationDefinitions.Foundation);
    }

    private static IReadOnlyList<GridPosition> FindFloorRectangle(
        SimulationEngine engine,
        int width,
        int height)
    {
        for (var radius = 1; radius <= 8; radius++)
        {
            for (var y = engine.Map.GoblinSpawn.Y - radius;
                 y <= engine.Map.GoblinSpawn.Y + radius; y++)
            {
                for (var x = engine.Map.GoblinSpawn.X - radius;
                     x <= engine.Map.GoblinSpawn.X + radius; x++)
                {
                    var cells = SimulationCommand.GetAreaCells(
                        new GridPosition(x, y),
                        new GridPosition(x + width - 1, y + height - 1));
                    if (cells.All(cell =>
                            engine.Visibility.TryGet(cell, out var visibility) &&
                            visibility.IsDiscovered()) &&
                        engine.World.CanBuildFloors(cells) &&
                        cells.All(engine.World.IsTerrainTraversable) &&
                        cells.All(cell => cell != engine.Map.GoblinSpawn))
                    {
                        return cells;
                    }
                }
            }
        }

        throw new InvalidOperationException("No discovered floor rectangle was available.");
    }
}
