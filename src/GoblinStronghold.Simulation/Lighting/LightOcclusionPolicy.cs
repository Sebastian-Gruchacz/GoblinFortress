using GoblinStronghold.Simulation.Map;

namespace GoblinStronghold.Simulation.Lighting;

public static class LightOcclusionPolicy
{
    private static readonly (float X, float Y)[] SoftSourceOffsets =
    [
        (0f, 0f),
        (-0.18f, 0f),
        (0.18f, 0f),
        (0f, -0.18f),
        (0f, 0.18f),
    ];

    public static float CalculateContribution(
        LightEmitterSnapshot emitter,
        GridPosition target,
        IReadOnlySet<GridPosition> blockingCells)
    {
        ArgumentNullException.ThrowIfNull(blockingCells);
        if (emitter.Position.Z != target.Z ||
            !FacesTarget(emitter.Position, target, emitter.Facing))
        {
            return 0f;
        }

        var deltaX = target.X - emitter.Position.X;
        var deltaY = target.Y - emitter.Position.Y;
        var distance = MathF.Sqrt((deltaX * deltaX) + (deltaY * deltaY));
        if (distance >= emitter.RadiusCells ||
            !HasLineOfSight(emitter.Position, target, blockingCells))
        {
            return 0f;
        }

        var falloff = 1f - (distance / emitter.RadiusCells);
        return emitter.Intensity * falloff * falloff;
    }

    public static float CalculateSoftContribution(
        LightEmitterSnapshot emitter,
        GridPosition target,
        IReadOnlySet<GridPosition> blockingCells)
    {
        ArgumentNullException.ThrowIfNull(blockingCells);
        if (emitter.Position.Z != target.Z ||
            !FacesTarget(emitter.Position, target, emitter.Facing))
        {
            return 0f;
        }

        var deltaX = target.X - emitter.Position.X;
        var deltaY = target.Y - emitter.Position.Y;
        var distance = MathF.Sqrt((deltaX * deltaX) + (deltaY * deltaY));
        if (distance >= emitter.RadiusCells)
        {
            return 0f;
        }

        var visibleSamples = 0;
        foreach (var offset in SoftSourceOffsets)
        {
            if (HasContinuousLineOfSight(
                    emitter.Position.X + 0.5f + offset.X,
                    emitter.Position.Y + 0.5f + offset.Y,
                    target.X + 0.5f,
                    target.Y + 0.5f,
                    emitter.Position.Z,
                    target,
                    blockingCells))
            {
                visibleSamples++;
            }
        }
        if (visibleSamples == 0)
        {
            return 0f;
        }

        var falloff = 1f - (distance / emitter.RadiusCells);
        return emitter.Intensity * falloff * falloff *
            visibleSamples / SoftSourceOffsets.Length;
    }

    public static bool HasLineOfSight(
        GridPosition source,
        GridPosition target,
        IReadOnlySet<GridPosition> blockingCells)
    {
        ArgumentNullException.ThrowIfNull(blockingCells);
        if (source.Z != target.Z)
        {
            return false;
        }

        var x = source.X;
        var y = source.Y;
        var deltaX = Math.Abs(target.X - source.X);
        var deltaY = Math.Abs(target.Y - source.Y);
        var stepX = Math.Sign(target.X - source.X);
        var stepY = Math.Sign(target.Y - source.Y);
        var error = deltaX - deltaY;
        while (x != target.X || y != target.Y)
        {
            var previousX = x;
            var previousY = y;
            var doubledError = error * 2;
            if (doubledError > -deltaY)
            {
                error -= deltaY;
                x += stepX;
            }
            if (doubledError < deltaX)
            {
                error += deltaX;
                y += stepY;
            }

            if (x != previousX && y != previousY &&
                (blockingCells.Contains(new GridPosition(x, previousY, source.Z)) ||
                 blockingCells.Contains(new GridPosition(previousX, y, source.Z))))
            {
                return false;
            }

            var position = new GridPosition(x, y, source.Z);
            if (position == target)
            {
                return true;
            }
            if (blockingCells.Contains(position))
            {
                return false;
            }
        }

        return true;
    }

    private static bool FacesTarget(
        GridPosition source,
        GridPosition target,
        CardinalOrientation? facing) => facing switch
    {
        CardinalOrientation.North => target.Y <= source.Y,
        CardinalOrientation.East => target.X >= source.X,
        CardinalOrientation.South => target.Y >= source.Y,
        CardinalOrientation.West => target.X <= source.X,
        _ => true,
    };

    private static bool HasContinuousLineOfSight(
        float sourceX,
        float sourceY,
        float targetX,
        float targetY,
        int level,
        GridPosition targetCell,
        IReadOnlySet<GridPosition> blockingCells)
    {
        var cellX = (int)MathF.Floor(sourceX);
        var cellY = (int)MathF.Floor(sourceY);
        if (cellX == targetCell.X && cellY == targetCell.Y)
        {
            return true;
        }

        var rayX = targetX - sourceX;
        var rayY = targetY - sourceY;
        var stepX = Math.Sign(rayX);
        var stepY = Math.Sign(rayY);
        var deltaX = stepX == 0 ? float.PositiveInfinity : MathF.Abs(1f / rayX);
        var deltaY = stepY == 0 ? float.PositiveInfinity : MathF.Abs(1f / rayY);
        var maximumX = stepX switch
        {
            > 0 => ((cellX + 1) - sourceX) / rayX,
            < 0 => (sourceX - cellX) / -rayX,
            _ => float.PositiveInfinity,
        };
        var maximumY = stepY switch
        {
            > 0 => ((cellY + 1) - sourceY) / rayY,
            < 0 => (sourceY - cellY) / -rayY,
            _ => float.PositiveInfinity,
        };

        while (cellX != targetCell.X || cellY != targetCell.Y)
        {
            if (MathF.Abs(maximumX - maximumY) < 0.00001f)
            {
                if (BlocksBeforeTarget(cellX + stepX, cellY, level, targetCell, blockingCells) ||
                    BlocksBeforeTarget(cellX, cellY + stepY, level, targetCell, blockingCells))
                {
                    return false;
                }
                cellX += stepX;
                cellY += stepY;
                maximumX += deltaX;
                maximumY += deltaY;
            }
            else if (maximumX < maximumY)
            {
                cellX += stepX;
                maximumX += deltaX;
            }
            else
            {
                cellY += stepY;
                maximumY += deltaY;
            }

            if (BlocksBeforeTarget(cellX, cellY, level, targetCell, blockingCells))
            {
                return false;
            }
        }

        return true;
    }

    private static bool BlocksBeforeTarget(
        int x,
        int y,
        int level,
        GridPosition target,
        IReadOnlySet<GridPosition> blockingCells) =>
        (x != target.X || y != target.Y) &&
        blockingCells.Contains(new GridPosition(x, y, level));
}
