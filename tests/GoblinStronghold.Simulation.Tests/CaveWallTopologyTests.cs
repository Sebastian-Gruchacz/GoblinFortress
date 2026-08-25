using GoblinStronghold.Simulation.Map;
using Xunit;

namespace GoblinStronghold.Simulation.Tests;

public sealed class CaveWallTopologyTests
{
    [Fact]
    public void DiagonalOpeningBehindSolidCardinalsCreatesInnerCorner()
    {
        var rock = new GridPosition(4, 4, -1);
        var openings = new HashSet<GridPosition>
        {
            new(5, 5, -1),
        };

        var corners = CaveWallTopology.GetInnerOpenCorners(rock, openings.Contains);

        Assert.Equal(CaveInnerCorner.SouthEast, corners);
    }

    [Fact]
    public void CardinalOpeningAlreadyOwnsEdgeAndSuppressesDiagonalCorner()
    {
        var rock = new GridPosition(4, 4, -1);
        var openings = new HashSet<GridPosition>
        {
            new(5, 4, -1),
            new(5, 5, -1),
        };

        var corners = CaveWallTopology.GetInnerOpenCorners(rock, openings.Contains);

        Assert.Equal(CaveInnerCorner.None, corners);
    }
}
