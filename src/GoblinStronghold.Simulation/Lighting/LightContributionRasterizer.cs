using GoblinStronghold.Simulation.Map;

namespace GoblinStronghold.Simulation.Lighting;

public readonly record struct LightContributionRaster(
    IReadOnlyList<float> RemainingDarkness,
    long EmitterEvaluations);

public static class LightContributionRasterizer
{
    public static LightContributionRaster Rasterize(
        int level,
        int minimumX,
        int minimumY,
        int maximumX,
        int maximumY,
        IReadOnlyList<LightEmitterSnapshot> emitters,
        IReadOnlySet<GridPosition> blockingCells,
        double animationElapsed)
    {
        ArgumentNullException.ThrowIfNull(emitters);
        ArgumentNullException.ThrowIfNull(blockingCells);
        if (maximumX <= minimumX || maximumY <= minimumY)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maximumX),
                "The raster bounds must have a positive area.");
        }

        var width = maximumX - minimumX;
        var height = maximumY - minimumY;
        var remainingDarkness = Enumerable.Repeat(1f, checked(width * height)).ToArray();
        long evaluations = 0;
        foreach (var emitter in emitters.Where(emitter => emitter.Position.Z == level))
        {
            var definition = LightEmitterCatalog.Get(emitter.Handle.DefinitionId);
            var phase = (animationElapsed / 6d) * Math.Tau * 7d +
                (emitter.Handle.InstanceId % 997UL) * 0.173d;
            var flicker = 1f - definition.FlickerAmount +
                definition.FlickerAmount * (0.5f + 0.5f * MathF.Sin((float)phase));
            var animatedEmitter = emitter with { Intensity = emitter.Intensity * flicker };
            var radius = emitter.RadiusCells;
            var emitterMinimumX = Math.Max(
                minimumX,
                (int)MathF.Floor(emitter.Position.X - radius));
            var emitterMinimumY = Math.Max(
                minimumY,
                (int)MathF.Floor(emitter.Position.Y - radius));
            var emitterMaximumX = Math.Min(
                maximumX,
                (int)MathF.Ceiling(emitter.Position.X + radius) + 1);
            var emitterMaximumY = Math.Min(
                maximumY,
                (int)MathF.Ceiling(emitter.Position.Y + radius) + 1);
            for (var y = emitterMinimumY; y < emitterMaximumY; y++)
            {
                for (var x = emitterMinimumX; x < emitterMaximumX; x++)
                {
                    var contribution = LightOcclusionPolicy.CalculateSoftContribution(
                        animatedEmitter,
                        new GridPosition(x, y, level),
                        blockingCells);
                    var index = checked(((y - minimumY) * width) + x - minimumX);
                    remainingDarkness[index] *= 1f - Math.Clamp(contribution, 0f, 1f);
                    evaluations = checked(evaluations + 1);
                }
            }
        }

        return new LightContributionRaster(remainingDarkness, evaluations);
    }
}
