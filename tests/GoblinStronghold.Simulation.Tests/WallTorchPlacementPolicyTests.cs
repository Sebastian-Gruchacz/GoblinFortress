using GoblinStronghold.Simulation.Construction;
using GoblinStronghold.Simulation.Map;
using Xunit;

namespace GoblinStronghold.Simulation.Tests;

public sealed class WallTorchPlacementPolicyTests
{
    [Theory]
    [InlineData(0, -1, CardinalOrientation.North)]
    [InlineData(1, 0, CardinalOrientation.East)]
    [InlineData(0, 1, CardinalOrientation.South)]
    [InlineData(-1, 0, CardinalOrientation.West)]
    public void CardinalDragSelectsRequestedWallFace(
        int deltaX,
        int deltaY,
        CardinalOrientation expected)
    {
        var wall = new GridPosition(12, 17, -2);
        var handle = wall with { X = wall.X + deltaX, Y = wall.Y + deltaY };

        Assert.True(WallTorchPlacementPolicy.TryResolvePreferredSide(
            wall,
            handle,
            out var actual));
        Assert.Equal(expected, actual);
        Assert.Equal(handle, WallTorchPlacementPolicy.CreateHandle(wall, expected));
    }

    [Fact]
    public void LegacySingleCellOrderKeepsNorthFallback()
    {
        var wall = new GridPosition(12, 17, -2);

        Assert.True(WallTorchPlacementPolicy.TryResolvePreferredSide(
            wall,
            wall,
            out var actual));
        Assert.Equal(CardinalOrientation.North, actual);
    }

    [Theory]
    [InlineData(1, 1, 0)]
    [InlineData(2, 0, 0)]
    [InlineData(0, 1, 1)]
    public void NonCardinalOrCrossLevelDragIsRejected(int deltaX, int deltaY, int deltaZ)
    {
        var wall = new GridPosition(12, 17, -2);
        var handle = new GridPosition(
            wall.X + deltaX,
            wall.Y + deltaY,
            wall.Z + deltaZ);

        Assert.False(WallTorchPlacementPolicy.TryResolvePreferredSide(
            wall,
            handle,
            out _));
    }
}

public sealed class ConstructionSitePlacementPolicyTests
{
    [Fact]
    public void RampsSharingAnUpperEndpointConflictDespiteDistinctAnchors()
    {
        var firstLower = new GridPosition(5, 6, -1);
        var secondLower = new GridPosition(6, 5, -1);
        var upper = new GridPosition(6, 6, 0);

        Assert.True(ConstructionSitePlacementPolicy.Conflicts(
            ConstructionKind.StoneRamp,
            firstLower,
            upper,
            [firstLower],
            ConstructionKind.WoodenRamp,
            secondLower,
            upper,
            [secondLower]));
    }

    [Fact]
    public void DistinctRampsRemainIndependent()
    {
        var firstLower = new GridPosition(5, 6, -1);
        var secondLower = new GridPosition(8, 6, -1);

        Assert.False(ConstructionSitePlacementPolicy.Conflicts(
            ConstructionKind.StoneRamp,
            firstLower,
            firstLower with { Y = 5, Z = 0 },
            [firstLower],
            ConstructionKind.StoneRamp,
            secondLower,
            secondLower with { Y = 5, Z = 0 },
            [secondLower]));
    }

    [Fact]
    public void FloorDirectlyAboveARampConflictsWithItsOpenVolume()
    {
        var lower = new GridPosition(5, 6, -1);
        var floor = lower with { Z = 0 };

        Assert.True(ConstructionSitePlacementPolicy.Conflicts(
            ConstructionKind.StoneRamp,
            lower,
            lower with { Y = 5, Z = 0 },
            [lower],
            ConstructionKind.WoodenFloor,
            floor,
            floor,
            [floor]));
    }
}
