using GoblinStronghold.Simulation.Construction;
using GoblinStronghold.Simulation.Map;
using GoblinStronghold.Simulation.Resources;
using Xunit;

namespace GoblinStronghold.Simulation.Tests;

public sealed class ConstructionDismantlingTargetTests
{
    [Fact]
    public void WorldObjectTargetOwnsFootprintDurationAndAccessCells()
    {
        var worldObject = new WorldObjectSnapshot(
            new WorldObjectId(7),
            WorldObjectKind.WoodenWall,
            WorldObjectOwner.GoblinTribe,
            new GridPosition(4, 5),
            CardinalOrientation.North,
            [
                new WorldObjectPartSnapshot(
                    new GridPosition(0, 0),
                    SpatialOccupancyChannel.Solid,
                    WorldObjectPartKind.Wall),
                new WorldObjectPartSnapshot(
                    new GridPosition(1, 0),
                    SpatialOccupancyChannel.Solid,
                    WorldObjectPartKind.Wall),
            ]);

        Assert.True(ConstructionDismantlingTargetFactory.TryCreate(
            worldObject,
            out var target));

        Assert.Equal(new EntityId(7), target.EntityId);
        Assert.Equal(ConstructionKind.WoodenWall, target.Construction);
        Assert.Equal(2, target.Footprint.Count);
        Assert.Equal(
            ConstructionDismantlingPolicy.GetWorkTicks(ConstructionKind.WoodenWall, 2),
            target.WorkTicks);
        var traversable = new HashSet<GridPosition>
        {
            new(3, 5),
            new(4, 4),
            new(5, 4),
            new(6, 5),
        };
        var access = ConstructionDismantlingTargetFactory.GetAccessCells(
            target,
            traversable.Contains,
            GetCardinalNeighbors);
        Assert.True(traversable.SetEquals(access));
    }

    [Theory]
    [InlineData(StorageProviderKind.OpenPile, ResourceKind.Food, ConstructionKind.FoodStorage)]
    [InlineData(StorageProviderKind.OpenPile, ResourceKind.Stone, ConstructionKind.StoneStorage)]
    [InlineData(StorageProviderKind.WaterBarrel, ResourceKind.Water, ConstructionKind.WaterBarrel)]
    [InlineData(StorageProviderKind.WoodenChest, ResourceKind.Food, ConstructionKind.WoodenChest)]
    public void StorageTargetMapsProviderBeforeResource(
        StorageProviderKind provider,
        ResourceKind resource,
        ConstructionKind expected)
    {
        var target = ConstructionDismantlingTargetFactory.CreateStorage(
            new EntityId(9),
            new GridPosition(2, 3),
            provider,
            resource);

        Assert.Equal(expected, target.Construction);
        Assert.Equal(new[] { new GridPosition(2, 3) }, target.Footprint);
        Assert.Equal(
            ConstructionDismantlingPolicy.GetWorkTicks(expected),
            target.WorkTicks);
    }

    [Fact]
    public void ForeignWorldObjectIsNotADismantlingTarget()
    {
        var worldObject = new WorldObjectSnapshot(
            new WorldObjectId(11),
            WorldObjectKind.WoodenWall,
            WorldObjectOwner.HumanVillage,
            new GridPosition(1, 1),
            CardinalOrientation.North,
            [new WorldObjectPartSnapshot(
                new GridPosition(0, 0),
                SpatialOccupancyChannel.Solid,
                WorldObjectPartKind.Wall)]);

        Assert.False(ConstructionDismantlingTargetFactory.TryCreate(
            worldObject,
            out _));
    }

    private static IEnumerable<GridPosition> GetCardinalNeighbors(GridPosition position) =>
        new[]
        {
            position with { X = position.X - 1 },
            position with { X = position.X + 1 },
            position with { Y = position.Y - 1 },
            position with { Y = position.Y + 1 },
        };
}
