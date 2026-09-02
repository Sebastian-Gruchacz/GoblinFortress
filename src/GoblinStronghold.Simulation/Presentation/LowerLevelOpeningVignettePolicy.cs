using GoblinStronghold.Simulation.Map;

namespace GoblinStronghold.Simulation.Presentation;

public static class LowerLevelOpeningVignettePolicy
{
    public const byte North = 1 << 0;
    public const byte NorthEast = 1 << 1;
    public const byte East = 1 << 2;
    public const byte SouthEast = 1 << 3;
    public const byte South = 1 << 4;
    public const byte SouthWest = 1 << 5;
    public const byte West = 1 << 6;
    public const byte NorthWest = 1 << 7;
    public const byte AllNeighbors = byte.MaxValue;

    private static readonly (int X, int Y, byte Bit)[] Neighbors =
    [
        (0, -1, North),
        (1, -1, NorthEast),
        (1, 0, East),
        (1, 1, SouthEast),
        (0, 1, South),
        (-1, 1, SouthWest),
        (-1, 0, West),
        (-1, -1, NorthWest),
    ];

    public static byte CreateNeighborMask(
        GridPosition position,
        Func<GridPosition, bool> isExposed)
    {
        ArgumentNullException.ThrowIfNull(isExposed);

        byte result = 0;
        foreach (var neighbor in Neighbors)
        {
            if (isExposed(new GridPosition(
                    position.X + neighbor.X,
                    position.Y + neighbor.Y,
                    position.Z)))
            {
                result |= neighbor.Bit;
            }
        }
        return result;
    }

    public static float ResolveStrength(
        byte neighborMask,
        int pixelX,
        int pixelY,
        int pixelsPerCell,
        float featherPixels)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(pixelsPerCell, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(featherPixels, 0.01f);
        ArgumentOutOfRangeException.ThrowIfNegative(pixelX);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(pixelX, pixelsPerCell);
        ArgumentOutOfRangeException.ThrowIfNegative(pixelY);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(pixelY, pixelsPerCell);

        if (neighborMask == AllNeighbors)
        {
            return 0f;
        }

        var fromNorth = EdgeStrength(pixelY + 0.5f, featherPixels);
        var fromEast = EdgeStrength(pixelsPerCell - pixelX - 0.5f, featherPixels);
        var fromSouth = EdgeStrength(pixelsPerCell - pixelY - 0.5f, featherPixels);
        var fromWest = EdgeStrength(pixelX + 0.5f, featherPixels);
        var strength = 0f;
        if (!Has(neighborMask, North))
        {
            strength = Math.Max(strength, fromNorth);
        }
        if (!Has(neighborMask, East))
        {
            strength = Math.Max(strength, fromEast);
        }
        if (!Has(neighborMask, South))
        {
            strength = Math.Max(strength, fromSouth);
        }
        if (!Has(neighborMask, West))
        {
            strength = Math.Max(strength, fromWest);
        }

        strength = Math.Max(strength, ResolveConcaveCorner(
            neighborMask,
            North,
            East,
            NorthEast,
            pixelsPerCell - pixelX - 0.5f,
            pixelY + 0.5f,
            featherPixels));
        strength = Math.Max(strength, ResolveConcaveCorner(
            neighborMask,
            East,
            South,
            SouthEast,
            pixelsPerCell - pixelX - 0.5f,
            pixelsPerCell - pixelY - 0.5f,
            featherPixels));
        strength = Math.Max(strength, ResolveConcaveCorner(
            neighborMask,
            South,
            West,
            SouthWest,
            pixelX + 0.5f,
            pixelsPerCell - pixelY - 0.5f,
            featherPixels));
        strength = Math.Max(strength, ResolveConcaveCorner(
            neighborMask,
            West,
            North,
            NorthWest,
            pixelX + 0.5f,
            pixelY + 0.5f,
            featherPixels));
        return Math.Clamp(strength, 0f, 1f);
    }

    private static float EdgeStrength(float distance, float featherPixels) =>
        Math.Clamp(1f - (distance / featherPixels), 0f, 1f);

    private static float ResolveConcaveCorner(
        byte mask,
        byte firstCardinal,
        byte secondCardinal,
        byte diagonal,
        float distanceX,
        float distanceY,
        float featherPixels)
    {
        if (!Has(mask, firstCardinal) ||
            !Has(mask, secondCardinal) ||
            Has(mask, diagonal))
        {
            return 0f;
        }

        return EdgeStrength(
            MathF.Sqrt((distanceX * distanceX) + (distanceY * distanceY)),
            featherPixels);
    }

    private static bool Has(byte mask, byte bit) => (mask & bit) != 0;
}
