using GoblinStronghold.Simulation.Map;
using Xunit;

namespace GoblinStronghold.Simulation.Tests;

public sealed class WallEnclosureAnalysisTests
{
    [Fact]
    public void ClosedRectangleFindsInteriorAndInteriorFacingWallSides()
    {
        var barriers = RectanglePerimeter(left: 2, top: 2, width: 5, height: 4);

        var analysis = WallEnclosureAnalysis.Analyze(10, 9, barriers);

        Assert.Equal(6, analysis.InteriorCells.Count);
        Assert.Equal(WallInteriorFacing.South,
            analysis.GetInteriorFacing(new GridPosition(4, 2)));
        Assert.Equal(WallInteriorFacing.North,
            analysis.GetInteriorFacing(new GridPosition(4, 5)));
        Assert.Equal(WallInteriorFacing.East,
            analysis.GetInteriorFacing(new GridPosition(2, 3)));
        Assert.Equal(WallInteriorFacing.West,
            analysis.GetInteriorFacing(new GridPosition(6, 3)));
        Assert.Equal(
            WallInteriorFacing.South | WallInteriorFacing.East,
            analysis.GetInteriorFacing(new GridPosition(2, 2)));
    }

    [Fact]
    public void GapConnectsRoomToExteriorAndRemovesInteriorFaces()
    {
        var barriers = RectanglePerimeter(left: 2, top: 2, width: 5, height: 4);
        barriers.Remove(new GridPosition(4, 2));

        var analysis = WallEnclosureAnalysis.Analyze(10, 9, barriers);

        Assert.Empty(analysis.InteriorCells);
        Assert.All(barriers, position =>
            Assert.Equal(WallInteriorFacing.None, analysis.GetWallSides(position).RoomSides));
    }

    [Fact]
    public void FreeStandingWallsKeepTheirTopProfileWithoutInventingAnInteriorFace()
    {
        var barriers = new HashSet<GridPosition>
        {
            new(2, 3),
            new(3, 3),
            new(4, 3),
            new(4, 4),
        };

        var analysis = WallEnclosureAnalysis.Analyze(8, 8, barriers);

        Assert.Equal(WallInteriorFacing.None,
            analysis.GetWallSides(new GridPosition(3, 3)).VisibleFaces);
        Assert.Equal(WallInteriorFacing.None,
            analysis.GetWallSides(new GridPosition(4, 3)).VisibleFaces);
    }

    [Fact]
    public void SolidMaterialCoversItsSideOfAFreeStandingWall()
    {
        var barriers = new HashSet<GridPosition>
        {
            new(2, 3),
            new(3, 3),
            new(4, 3),
        };
        var solids = new HashSet<GridPosition> { new(3, 2) };

        var analysis = WallEnclosureAnalysis.Analyze(8, 8, barriers, solids);
        var sides = analysis.GetWallSides(new GridPosition(3, 3));

        Assert.Equal(WallInteriorFacing.North, sides.CoveredSides);
        Assert.Equal(WallInteriorFacing.South, sides.VisibleFaces);
    }

    [Fact]
    public void WallMountUsesTheOnlyRoomFacingSide()
    {
        var wall = new WallRenderSides(
            WallInteriorFacing.East | WallInteriorFacing.West,
            WallInteriorFacing.South,
            WallInteriorFacing.None,
            WallInteriorFacing.South);

        var resolved = WallMountPlacementResolver.TryResolve(
            wall,
            CardinalOrientation.North,
            out var placement);

        Assert.True(resolved);
        Assert.Equal(CardinalOrientation.South, placement.Side);
        Assert.True(placement.RunsHorizontally);
    }

    [Fact]
    public void WallMountKeepsARequestedSideOfATwoSidedPartition()
    {
        var wall = new WallRenderSides(
            WallInteriorFacing.East | WallInteriorFacing.West,
            WallInteriorFacing.None,
            WallInteriorFacing.None,
            WallInteriorFacing.North | WallInteriorFacing.South);

        var resolved = WallMountPlacementResolver.TryResolve(
            wall,
            CardinalOrientation.South,
            out var placement);

        Assert.True(resolved);
        Assert.Equal(CardinalOrientation.South, placement.Side);
    }

    [Fact]
    public void WallMountRejectsAnAmbiguousCornerWithoutAValidPreferredSide()
    {
        var wall = new WallRenderSides(
            WallInteriorFacing.North | WallInteriorFacing.East,
            WallInteriorFacing.None,
            WallInteriorFacing.None,
            WallInteriorFacing.North | WallInteriorFacing.East);

        Assert.False(WallMountPlacementResolver.TryResolve(
            wall,
            CardinalOrientation.South,
            out _));
    }

    private static HashSet<GridPosition> RectanglePerimeter(
        int left,
        int top,
        int width,
        int height)
    {
        var result = new HashSet<GridPosition>();
        for (var x = left; x < left + width; x++)
        {
            result.Add(new GridPosition(x, top));
            result.Add(new GridPosition(x, top + height - 1));
        }
        for (var y = top; y < top + height; y++)
        {
            result.Add(new GridPosition(left, y));
            result.Add(new GridPosition(left + width - 1, y));
        }
        return result;
    }
}
