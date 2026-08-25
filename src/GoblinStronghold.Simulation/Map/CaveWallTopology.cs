namespace GoblinStronghold.Simulation.Map;

[Flags]
public enum CaveInnerCorner : byte
{
    None = 0,
    NorthWest = 1,
    NorthEast = 2,
    SouthEast = 4,
    SouthWest = 8,
}

public static class CaveWallTopology
{
    public static CaveInnerCorner GetInnerOpenCorners(
        GridPosition rockPosition,
        Func<GridPosition, bool> isOpen)
    {
        ArgumentNullException.ThrowIfNull(isOpen);

        var northOpen = isOpen(rockPosition with { Y = rockPosition.Y - 1 });
        var eastOpen = isOpen(rockPosition with { X = rockPosition.X + 1 });
        var southOpen = isOpen(rockPosition with { Y = rockPosition.Y + 1 });
        var westOpen = isOpen(rockPosition with { X = rockPosition.X - 1 });
        var corners = CaveInnerCorner.None;
        if (!northOpen && !westOpen &&
            isOpen(rockPosition with { X = rockPosition.X - 1, Y = rockPosition.Y - 1 }))
        {
            corners |= CaveInnerCorner.NorthWest;
        }
        if (!northOpen && !eastOpen &&
            isOpen(rockPosition with { X = rockPosition.X + 1, Y = rockPosition.Y - 1 }))
        {
            corners |= CaveInnerCorner.NorthEast;
        }
        if (!southOpen && !eastOpen &&
            isOpen(rockPosition with { X = rockPosition.X + 1, Y = rockPosition.Y + 1 }))
        {
            corners |= CaveInnerCorner.SouthEast;
        }
        if (!southOpen && !westOpen &&
            isOpen(rockPosition with { X = rockPosition.X - 1, Y = rockPosition.Y + 1 }))
        {
            corners |= CaveInnerCorner.SouthWest;
        }
        return corners;
    }
}
