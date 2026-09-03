using Godot;
using GoblinStronghold.Simulation.Map;

namespace GoblinStronghold.GodotClient.UI.WorldRendering;

internal static class ReedSleepingMatPainter
{
    private const float TileSize = 20f;

    public static void Paint(
        CanvasItem canvas,
        WorldObjectSnapshot sleepingMat,
        int visibleLevel)
    {
        ArgumentNullException.ThrowIfNull(canvas);
        if (sleepingMat.Anchor.Z != visibleLevel)
        {
            return;
        }

        var rect = new Rect2(
            sleepingMat.Anchor.X * TileSize + 3f,
            sleepingMat.Anchor.Y * TileSize + 5f,
            14f,
            10f);
        canvas.DrawRect(rect, new Color("8f7b43"));
        canvas.DrawLine(rect.Position, rect.Position + new Vector2(rect.Size.X, 0f),
            new Color("76502f"), 2f);
        canvas.DrawLine(rect.End - new Vector2(rect.Size.X, 0f), rect.End,
            new Color("76502f"), 2f);
        for (var offset = 2f; offset < rect.Size.Y; offset += 2.5f)
        {
            canvas.DrawLine(rect.Position + new Vector2(1f, offset),
                rect.Position + new Vector2(rect.Size.X - 1f, offset),
                new Color("c0a95f"), 1f);
        }

        canvas.DrawLine(rect.Position + new Vector2(2f, -2f),
            rect.Position + new Vector2(2f, rect.Size.Y + 2f),
            new Color("6d472b"), 1.5f);
        canvas.DrawLine(rect.Position + new Vector2(rect.Size.X - 2f, -2f),
            rect.Position + new Vector2(rect.Size.X - 2f, rect.Size.Y + 2f),
            new Color("6d472b"), 1.5f);
    }
}
