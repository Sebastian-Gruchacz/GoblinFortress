using Godot;
using GoblinStronghold.Simulation.Map;

namespace GoblinStronghold.GodotClient.UI.WorldRendering;

internal static class StandingTorchPainter
{
    private const float TileSize = 20f;

    public static void Paint(
        CanvasItem canvas,
        WorldObjectSnapshot torch,
        int visibleLevel)
    {
        ArgumentNullException.ThrowIfNull(canvas);
        if (torch.Anchor.Z != visibleLevel)
        {
            return;
        }

        var center = new Vector2(
            (torch.Anchor.X + 0.5f) * TileSize,
            (torch.Anchor.Y + 0.5f) * TileSize);
        var timber = new Color("76502f");
        canvas.DrawLine(center + new Vector2(-6f, 7f), center + new Vector2(-2f, 1f),
            timber, 2f, antialiased: true);
        canvas.DrawLine(center + new Vector2(6f, 7f), center + new Vector2(2f, 1f),
            timber, 2f, antialiased: true);
        canvas.DrawLine(center + new Vector2(0f, 8f), center, timber, 2f,
            antialiased: true);
        canvas.DrawCircle(center, 5f, new Color("36261a"));
        canvas.DrawCircle(center, 3.5f, new Color("8b4b25"));

        var phase = ((float)Time.GetTicksMsec() * 0.012f) +
            (torch.Anchor.X * 0.73f) + (torch.Anchor.Y * 1.17f);
        var flicker = 0.5f + (0.5f * Mathf.Sin(phase));
        canvas.DrawCircle(center, 9f + (flicker * 2f),
            new Color(1f, 0.42f, 0.08f, 0.09f));
        canvas.DrawCircle(center - new Vector2(0f, 1f), 3f + (flicker * 0.7f),
            new Color("ff7622"));
        canvas.DrawCircle(center - new Vector2(0f, 2f), 1.4f,
            new Color("ffe66b"));
    }
}
