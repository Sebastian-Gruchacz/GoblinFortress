using Godot;
using GoblinStronghold.Simulation.Map;

namespace GoblinStronghold.GodotClient.UI.WorldRendering;

internal static class CookingFirePainter
{
    private const float TileSize = 20f;

    public static void Paint(
        CanvasItem canvas,
        WorldObjectSnapshot cookingFire,
        int visibleLevel)
    {
        if (cookingFire.Anchor.Z != visibleLevel)
        {
            return;
        }

        var center = new Vector2(
            (cookingFire.Anchor.X + 0.5f) * TileSize,
            (cookingFire.Anchor.Y + 0.5f) * TileSize);
        var stone = new Color("776b5c");
        for (var index = 0; index < 8; index++)
        {
            var angle = index * MathF.Tau / 8f;
            canvas.DrawCircle(
                center + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * 6.5f,
                2.1f,
                stone.Darkened(index % 2 == 0 ? 0.05f : 0.18f));
        }

        canvas.DrawLine(center + new Vector2(-6f, -4f), center + new Vector2(6f, 4f),
            new Color("51321f"), 2.5f);
        canvas.DrawLine(center + new Vector2(6f, -4f), center + new Vector2(-6f, 4f),
            new Color("51321f"), 2.5f);
        canvas.DrawCircle(center, 4.5f, new Color("d44d1d"));
        canvas.DrawCircle(center + new Vector2(1f, -2f), 2.8f, new Color("ff9b2f"));
        canvas.DrawLine(center + new Vector2(-8f, -7f), center + new Vector2(8f, -7f),
            new Color("34261c"), 1.5f);
        canvas.DrawCircle(center + new Vector2(2f, -7f), 4f, new Color("40362d"));
    }
}
