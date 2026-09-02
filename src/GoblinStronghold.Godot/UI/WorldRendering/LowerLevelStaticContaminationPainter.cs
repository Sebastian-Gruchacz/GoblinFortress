using Godot;
using GoblinStronghold.Simulation.Map;

namespace GoblinStronghold.GodotClient.UI.WorldRendering;

internal static class LowerLevelStaticContaminationPainter
{
    public static void PaintCell(
        Image target,
        Vector2I origin,
        GridPosition position,
        int bloodVolume,
        int grimeVolume)
    {
        ArgumentNullException.ThrowIfNull(target);
        PaintMarks(
            target,
            origin,
            position,
            count: Math.Min(4, (bloodVolume + 15) / 16),
            salt: 17,
            new Color(0.24f, 0.035f, 0.025f, 0.72f));
        PaintMarks(
            target,
            origin,
            position,
            count: Math.Min(4, (grimeVolume + 11) / 12),
            salt: 43,
            new Color(0.23f, 0.21f, 0.17f, 0.56f));
    }

    private static void PaintMarks(
        Image target,
        Vector2I origin,
        GridPosition position,
        int count,
        int salt,
        Color color)
    {
        var state = unchecked((uint)(
            (position.X * 73_856_093) ^
            (position.Y * 19_349_663) ^
            (position.Z * 83_492_791) ^
            salt));
        for (var index = 0; index < count; index++)
        {
            state = Next(state);
            var drawableSize = LowerLevelChunkTextureCache.PixelsPerCell - 2;
            var x = 1 + (int)(state % drawableSize);
            state = Next(state);
            var y = 1 + (int)(state % drawableSize);
            var pixel = origin + new Vector2I(x, y);
            target.SetPixel(
                pixel.X,
                pixel.Y,
                target.GetPixel(pixel.X, pixel.Y).Blend(color));
            if (index >= 2 && x < LowerLevelChunkTextureCache.PixelsPerCell - 2)
            {
                target.SetPixel(
                    pixel.X + 1,
                    pixel.Y,
                    target.GetPixel(pixel.X + 1, pixel.Y).Blend(color));
            }
        }
    }

    private static uint Next(uint state)
    {
        state ^= state << 13;
        state ^= state >> 17;
        state ^= state << 5;
        return state;
    }
}
