using GoblinStronghold.Simulation.Map;
using GoblinStronghold.Simulation.Presentation;
using Xunit;

namespace GoblinStronghold.Simulation.Tests;

public sealed class LowerLevelOpeningVignettePolicyTests
{
    [Fact]
    public void FullySurroundedCellHasNoVignette()
    {
        var strength = LowerLevelOpeningVignettePolicy.ResolveStrength(
            LowerLevelOpeningVignettePolicy.AllNeighbors,
            pixelX: 0,
            pixelY: 0,
            pixelsPerCell: 16,
            featherPixels: 5.5f);

        Assert.Equal(0f, strength);
    }

    [Fact]
    public void MissingNorthernNeighborDarkensOnlyNorthernEdge()
    {
        var mask = (byte)(
            LowerLevelOpeningVignettePolicy.AllNeighbors &
            ~LowerLevelOpeningVignettePolicy.North);

        var edge = Resolve(mask, pixelX: 8, pixelY: 0);
        var center = Resolve(mask, pixelX: 8, pixelY: 8);
        var oppositeEdge = Resolve(mask, pixelX: 8, pixelY: 15);

        Assert.True(edge > center);
        Assert.Equal(0f, center);
        Assert.Equal(0f, oppositeEdge);
    }

    [Fact]
    public void MissingDiagonalCreatesRoundedConcaveCorner()
    {
        var mask = (byte)(
            LowerLevelOpeningVignettePolicy.AllNeighbors &
            ~LowerLevelOpeningVignettePolicy.NorthEast);

        var affectedCorner = Resolve(mask, pixelX: 15, pixelY: 0);
        var otherCorner = Resolve(mask, pixelX: 0, pixelY: 0);
        var center = Resolve(mask, pixelX: 8, pixelY: 8);

        Assert.True(affectedCorner > 0f);
        Assert.Equal(0f, otherCorner);
        Assert.Equal(0f, center);
    }

    [Fact]
    public void NeighborMaskUsesWorldCoordinatesAcrossChunkBoundary()
    {
        var position = new GridPosition(15, 4, -1);
        var exposed = new HashSet<GridPosition>
        {
            position,
            new(16, 4, -1),
        };

        var mask = LowerLevelOpeningVignettePolicy.CreateNeighborMask(
            position,
            exposed.Contains);

        Assert.True((mask & LowerLevelOpeningVignettePolicy.East) != 0);
        Assert.False((mask & LowerLevelOpeningVignettePolicy.West) != 0);
    }

    private static float Resolve(byte mask, int pixelX, int pixelY) =>
        LowerLevelOpeningVignettePolicy.ResolveStrength(
            mask,
            pixelX,
            pixelY,
            pixelsPerCell: 16,
            featherPixels: 5.5f);
}
