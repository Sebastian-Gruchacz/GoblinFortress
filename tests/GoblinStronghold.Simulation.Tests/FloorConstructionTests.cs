using System.Text.Json.Nodes;
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
