using Godot;
using GoblinStronghold.Simulation.Map;
using GoblinStronghold.Simulation.Resources;

namespace GoblinStronghold.GodotClient.UI.WorldRendering;

internal static class WoodenWatchtowerPainter
{
    private const float TileSize = 20f;

    public static void Paint(
        CanvasItem canvas,
        WorldObjectSnapshot watchtower,
        int visibleLevel)
    {
        ArgumentNullException.ThrowIfNull(canvas);
        if (visibleLevel != watchtower.Anchor.Z && visibleLevel != watchtower.Anchor.Z + 1)
        {
            return;
        }

        var palette = watchtower.MaterialVariant == ResourceVariant.None
            ? new MaterialPaletteColors(
                new Color("2b1b12"),
                new Color("563820"),
                new Color("79502f"),
                new Color("b27b46"))
            : MaterialPaletteColors.For(watchtower.MaterialVariant);
        var platform = new Rect2(
            watchtower.Anchor.X * TileSize + 2f,
            watchtower.Anchor.Y * TileSize + 2f,
            (TileSize * 2f) - 4f,
            (TileSize * 2f) - 4f);
        if (visibleLevel == watchtower.Anchor.Z)
        {
            var inner = platform.Grow(-5f);
            canvas.DrawLine(inner.Position, inner.End, palette.Shadow, 3f);
            canvas.DrawLine(
                inner.Position + new Vector2(inner.Size.X, 0f),
                inner.Position + new Vector2(0f, inner.Size.Y),
                palette.Shadow,
                3f);
            foreach (var corner in new[]
                     {
                         platform.Position + new Vector2(4f, 4f),
                         platform.Position + new Vector2(platform.Size.X - 4f, 4f),
                         platform.Position + new Vector2(4f, platform.Size.Y - 4f),
                         platform.End - new Vector2(4f, 4f),
                     })
            {
                canvas.DrawCircle(corner, 3f, palette.Shadow);
                canvas.DrawCircle(corner, 1.5f, palette.Highlight);
            }
            return;
        }

        canvas.DrawRect(platform, palette.Edge);
        canvas.DrawRect(platform.Grow(-2f), palette.Midtone);
        for (var offset = 7f; offset < platform.Size.X - 2f; offset += 7f)
        {
            canvas.DrawLine(
                platform.Position + new Vector2(offset, 2f),
                platform.Position + new Vector2(offset, platform.Size.Y - 2f),
                palette.Highlight,
                1.5f);
        }

        foreach (var corner in new[]
                 {
                     platform.Position + new Vector2(4f, 4f),
                     platform.Position + new Vector2(platform.Size.X - 4f, 4f),
                     platform.Position + new Vector2(4f, platform.Size.Y - 4f),
                     platform.End - new Vector2(4f, 4f),
                 })
        {
            canvas.DrawCircle(corner, 3f, palette.Shadow);
            canvas.DrawCircle(corner, 1.5f, palette.Highlight);
        }
    }
}
