namespace GoblinStronghold.Simulation.Presentation;

public static class RetainedPresentationBoundsPolicy
{
    public static PresentationCellBounds ExpandToChunks(
        PresentationCellBounds viewport,
        int chunkSize,
        int mapWidth,
        int mapHeight)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(chunkSize);
        ArgumentOutOfRangeException.ThrowIfNegative(mapWidth);
        ArgumentOutOfRangeException.ThrowIfNegative(mapHeight);

        return new PresentationCellBounds(
            Math.Max(0, (viewport.MinimumX / chunkSize - 1) * chunkSize),
            Math.Max(0, (viewport.MinimumY / chunkSize - 1) * chunkSize),
            Math.Min(
                mapWidth,
                ((viewport.MaximumX + chunkSize - 1) / chunkSize + 1) * chunkSize),
            Math.Min(
                mapHeight,
                ((viewport.MaximumY + chunkSize - 1) / chunkSize + 1) * chunkSize));
    }

    public static bool Contains(
        PresentationCellBounds outer,
        PresentationCellBounds inner) =>
        inner.MinimumX >= outer.MinimumX && inner.MinimumY >= outer.MinimumY &&
        inner.MaximumX <= outer.MaximumX && inner.MaximumY <= outer.MaximumY;
}
