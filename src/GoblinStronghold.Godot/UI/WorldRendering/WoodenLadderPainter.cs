using Godot;
using GoblinStronghold.Simulation.Map;
using GoblinStronghold.Simulation.Resources;

namespace GoblinStronghold.GodotClient.UI.WorldRendering;

internal static class WoodenLadderPainter
{
    private const float TileSize = 20f;

    public static void Paint(
        CanvasItem canvas,
        WorldObjectSnapshot ladder,
        int visibleLevel)
    {
        ArgumentNullException.ThrowIfNull(canvas);
        if (visibleLevel != ladder.Anchor.Z && visibleLevel != ladder.Anchor.Z + 1)
        {
            return;
        }

        var palette = ladder.MaterialVariant == ResourceVariant.None
            ? new MaterialPaletteColors(
                new Color("342318"),
                new Color("5a3b26"),
                new Color("8b6038"),
                new Color("c18a50"))
            : MaterialPaletteColors.For(ladder.MaterialVariant);
        var direction = ladder.Orientation switch
        {
            CardinalOrientation.North => Vector2.Up,
            CardinalOrientation.East => Vector2.Right,
            CardinalOrientation.South => Vector2.Down,
            CardinalOrientation.West => Vector2.Left,
            _ => Vector2.Up,
        };
        var side = new Vector2(-direction.Y, direction.X) * 3.2f;
        var position = visibleLevel == ladder.Anchor.Z
            ? ladder.Anchor
            : new GridPosition(
                ladder.Anchor.X + Mathf.RoundToInt(direction.X),
                ladder.Anchor.Y + Mathf.RoundToInt(direction.Y),
                ladder.Anchor.Z + 1);
        var center = new Vector2(
            (position.X + 0.5f) * TileSize,
            (position.Y + 0.5f) * TileSize);
        var inward = visibleLevel == ladder.Anchor.Z ? direction : -direction;
        var start = center - inward * 7f;
        var end = center + inward * 7f;
        canvas.DrawLine(start - side, end - side, palette.Shadow, 2.4f);
        canvas.DrawLine(start + side, end + side, palette.Shadow, 2.4f);
        for (var offset = -5f; offset <= 5f; offset += 5f)
        {
            var rungCenter = center + inward * offset;
            canvas.DrawLine(
                rungCenter - side,
                rungCenter + side,
                palette.Highlight,
                1.8f);
        }
    }
}
