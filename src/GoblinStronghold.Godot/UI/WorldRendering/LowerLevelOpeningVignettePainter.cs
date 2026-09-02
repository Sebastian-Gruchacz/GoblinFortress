using Godot;
using GoblinStronghold.Simulation.Map;
using GoblinStronghold.Simulation.Presentation;

namespace GoblinStronghold.GodotClient.UI.WorldRendering;

internal static class LowerLevelOpeningVignettePainter
{
    private const float FeatherPixels = 5.5f;
    private const float MaximumDarkening = 0.46f;
    private static readonly float[][] VignetteTiles = BuildVignetteTiles();

    public static void Paint(
        Image target,
        PresentationChunkKey key,
        LowerLevelExposureIndex exposure)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(exposure);

        var chunkSize = exposure.ChunkSize;
        for (var localY = 0; localY < chunkSize; localY++)
        {
            for (var localX = 0; localX < chunkSize; localX++)
            {
                var position = new GridPosition(
                    (key.X * chunkSize) + localX,
                    (key.Y * chunkSize) + localY,
                    key.Level);
                if (!exposure.IsContinuouslyExposed(position))
                {
                    continue;
                }

                var neighborMask = LowerLevelOpeningVignettePolicy.CreateNeighborMask(
                    position,
                    exposure.IsContinuouslyExposed);
                if (neighborMask == LowerLevelOpeningVignettePolicy.AllNeighbors)
                {
                    continue;
                }

                PaintCell(target, localX, localY, neighborMask);
            }
        }
    }

    private static void PaintCell(
        Image target,
        int cellX,
        int cellY,
        byte neighborMask)
    {
        var vignette = VignetteTiles[neighborMask];
        var pixelsPerCell = LowerLevelChunkTextureCache.PixelsPerCell;
        var minimumX = cellX * pixelsPerCell;
        var minimumY = cellY * pixelsPerCell;
        for (var pixelY = 0; pixelY < pixelsPerCell; pixelY++)
        {
            for (var pixelX = 0; pixelX < pixelsPerCell; pixelX++)
            {
                var multiplier = vignette[(pixelY * pixelsPerCell) + pixelX];
                if (multiplier >= 1f)
                {
                    continue;
                }

                var x = minimumX + pixelX;
                var y = minimumY + pixelY;
                var source = target.GetPixel(x, y);
                if (source.A <= 0f)
                {
                    continue;
                }

                target.SetPixel(x, y, new Color(
                    source.R * multiplier,
                    source.G * multiplier,
                    source.B * multiplier,
                    source.A));
            }
        }
    }

    private static float[][] BuildVignetteTiles()
    {
        var result = new float[byte.MaxValue + 1][];
        for (var mask = 0; mask <= byte.MaxValue; mask++)
        {
            var tile = new float[LowerLevelChunkTextureCache.PixelsPerCell *
                LowerLevelChunkTextureCache.PixelsPerCell];
            for (var pixelY = 0;
                 pixelY < LowerLevelChunkTextureCache.PixelsPerCell;
                 pixelY++)
            {
                for (var pixelX = 0;
                     pixelX < LowerLevelChunkTextureCache.PixelsPerCell;
                     pixelX++)
                {
                    var strength = LowerLevelOpeningVignettePolicy.ResolveStrength(
                        (byte)mask,
                        pixelX,
                        pixelY,
                        LowerLevelChunkTextureCache.PixelsPerCell,
                        FeatherPixels);
                    var smoothStrength = strength * strength * (3f - (2f * strength));
                    tile[(pixelY * LowerLevelChunkTextureCache.PixelsPerCell) + pixelX] =
                        1f - (MaximumDarkening * smoothStrength);
                }
            }
            result[mask] = tile;
        }
        return result;
    }
}
