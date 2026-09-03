using Godot;
using GoblinStronghold.Simulation;
using GoblinStronghold.Simulation.Map;

namespace GoblinStronghold.GodotClient.UI.WorldRendering;

internal static class GoblinRuinPainter
{
    private const float TileSize = 20f;

    public static HashSet<GridPosition> Paint(
        CanvasItem canvas,
        WorldObjectSnapshot ruin,
        int visibleLevel)
    {
        ArgumentNullException.ThrowIfNull(canvas);

        var stone = new Color("5a5b52");
        var stoneDark = new Color("30352f");
        var moss = new Color("52603b");
        var timber = new Color("76502f");
        var reed = new Color("a08a4e");
        var absoluteParts = ruin.GetAbsoluteParts()
            .Where(item => item.Position.Z == visibleLevel)
            .ToArray();
        foreach (var (position, part) in absoluteParts)
        {
            var rect = CellRect(position.X, position.Y);
            var decorationKey = DecorationKey(ruin, position);
            switch (part.Kind)
            {
                case WorldObjectPartKind.Floor:
                    canvas.DrawRect(rect.Grow(-0.7f), stone.Darkened(0.14f));
                    canvas.DrawLine(rect.Position + new Vector2(3f, 6f),
                        rect.Position + new Vector2(12f, 4f), stoneDark, 1f);
                    canvas.DrawLine(rect.Position + new Vector2(12f, 4f),
                        rect.Position + new Vector2(16f, 10f), stoneDark, 1f);
                    if (decorationKey % 4 == 0)
                    {
                        PaintFloorRepair(canvas, rect, timber, decorationKey);
                    }
                    break;
                case WorldObjectPartKind.Wall:
                    canvas.DrawRect(rect.Grow(-1f), stoneDark);
                    canvas.DrawRect(rect.Grow(-3f), stone);
                    canvas.DrawLine(rect.Position + new Vector2(3f, 8f),
                        rect.End - new Vector2(3f, 8f), moss, 2f);
                    if (decorationKey % 5 == 1)
                    {
                        PaintReedPatch(canvas, rect, reed, timber, decorationKey);
                    }
                    else if (decorationKey % 3 == 0)
                    {
                        canvas.DrawLine(rect.Position + new Vector2(4f, 3f),
                            rect.End - new Vector2(4f, 3f), timber, 2f);
                    }
                    break;
                case WorldObjectPartKind.Door:
                    canvas.DrawRect(rect.Grow(-2f), new Color("322b20"));
                    for (var offset = 4f; offset <= 16f; offset += 4f)
                    {
                        canvas.DrawLine(rect.Position + new Vector2(offset, 2f),
                            rect.Position + new Vector2(offset, 18f), reed, 2f);
                    }
                    canvas.DrawLine(rect.Position + new Vector2(2f, 7f),
                        rect.Position + new Vector2(18f, 12f), timber, 1.5f);
                    break;
            }
        }

        var blocked = absoluteParts
            .Where(item => item.Part.Channel == SpatialOccupancyChannel.Solid)
            .Select(item => item.Position)
            .ToHashSet();
        var openFloorPositions = absoluteParts
            .Where(item => item.Part.Kind == WorldObjectPartKind.Floor &&
                !blocked.Contains(item.Position))
            .Select(item => item.Position)
            .OrderBy(item => item.Y)
            .ThenBy(item => item.X)
            .ToArray();
        if (absoluteParts.Length > 0)
        {
            PaintHearths(canvas, ruin, visibleLevel, timber, absoluteParts, openFloorPositions);
        }

        var scaffoldWall = absoluteParts
            .Where(item => item.Part.Kind == WorldObjectPartKind.Wall)
            .OrderBy(item => DecorationKey(ruin, item.Position, 71))
            .Select(item => (GridPosition?)item.Position)
            .FirstOrDefault();
        if (scaffoldWall is { } wall)
        {
            PaintScaffold(canvas, CellRect(wall.X, wall.Y), timber);
        }

        return absoluteParts.Select(item => item.Position).ToHashSet();
    }

    public static void PaintCompost(
        CanvasItem canvas,
        WorldObjectSnapshot compost,
        int visibleLevel)
    {
        ArgumentNullException.ThrowIfNull(canvas);
        if (compost.Anchor.Z != visibleLevel)
        {
            return;
        }

        var center = CellRect(compost.Anchor.X, compost.Anchor.Y).GetCenter();
        canvas.DrawCircle(center, 8f, new Color("33291d"));
        canvas.DrawCircle(center + new Vector2(-2f, 1f), 6f, new Color("594a2b"));
        canvas.DrawCircle(center + new Vector2(3f, 2f), 4f, new Color("6c6332"));
        canvas.DrawLine(center + new Vector2(-5f, -2f), center + new Vector2(5f, 4f),
            new Color("c8b98a"), 2f);
        canvas.DrawCircle(center + new Vector2(-5f, -2f), 2f, new Color("d8c99a"));
        canvas.DrawCircle(center + new Vector2(5f, 4f), 2f, new Color("d8c99a"));
        canvas.DrawCircle(center + new Vector2(2f, -5f), 2f, new Color("789246"));
    }

    private static void PaintHearths(
        CanvasItem canvas,
        WorldObjectSnapshot ruin,
        int visibleLevel,
        Color timber,
        IReadOnlyList<(GridPosition Position, WorldObjectPartSnapshot Part)> absoluteParts,
        IReadOnlyList<GridPosition> openFloorPositions)
    {
        var maximumX = absoluteParts.Max(item => item.Position.X);
        var maximumY = absoluteParts.Max(item => item.Position.Y);
        if (maximumX - ruin.Anchor.X < 5 || maximumY - ruin.Anchor.Y < 4)
        {
            return;
        }

        var emberPosition = openFloorPositions
            .Select(position => (GridPosition?)position)
            .LastOrDefault();
        if (emberPosition is { } ember)
        {
            PaintHearth(canvas, ember, timber, isCookingFire: false);
        }
    }

    private static uint DecorationKey(
        WorldObjectSnapshot ruin,
        GridPosition position,
        int salt = 0)
    {
        return unchecked((uint)(
            (position.X * 73856093) ^
            (position.Y * 19349663) ^
            ((int)ruin.Id.Value * 83492791) ^
            salt));
    }

    private static void PaintFloorRepair(
        CanvasItem canvas,
        Rect2 rect,
        Color timber,
        uint decorationKey)
    {
        var horizontal = (decorationKey & 1) == 0;
        for (var offset = 0f; offset < 3f; offset++)
        {
            var shift = 6f + (offset * 3f);
            var start = horizontal
                ? rect.Position + new Vector2(2f, shift)
                : rect.Position + new Vector2(shift, 2f);
            var end = horizontal
                ? rect.Position + new Vector2(18f, shift - 1f)
                : rect.Position + new Vector2(shift - 1f, 18f);
            canvas.DrawLine(start, end, timber.Darkened(offset * 0.06f), 2f);
        }
    }

    private static void PaintReedPatch(
        CanvasItem canvas,
        Rect2 rect,
        Color reed,
        Color timber,
        uint decorationKey)
    {
        var horizontal = (decorationKey & 1) == 0;
        var patch = rect.Grow(-4f);
        for (var offset = 1.5f; offset < 12f; offset += 3f)
        {
            var start = horizontal
                ? patch.Position + new Vector2(0f, offset)
                : patch.Position + new Vector2(offset, 0f);
            var end = horizontal
                ? patch.Position + new Vector2(patch.Size.X, offset)
                : patch.Position + new Vector2(offset, patch.Size.Y);
            canvas.DrawLine(start, end, reed, 1.5f);
        }

        if (horizontal)
        {
            canvas.DrawLine(patch.Position + new Vector2(3f, 0f),
                patch.Position + new Vector2(3f, patch.Size.Y), timber, 1f);
            canvas.DrawLine(patch.End - new Vector2(3f, patch.Size.Y),
                patch.End - new Vector2(3f, 0f), timber, 1f);
        }
        else
        {
            canvas.DrawLine(patch.Position + new Vector2(0f, 3f),
                patch.Position + new Vector2(patch.Size.X, 3f), timber, 1f);
            canvas.DrawLine(patch.End - new Vector2(patch.Size.X, 3f),
                patch.End - new Vector2(0f, 3f), timber, 1f);
        }
    }

    private static void PaintScaffold(CanvasItem canvas, Rect2 rect, Color timber)
    {
        var inset = rect.Grow(-2f);
        canvas.DrawLine(inset.Position + new Vector2(1f, 2f),
            inset.End - new Vector2(1f, 2f), timber, 2f);
        canvas.DrawLine(inset.Position + new Vector2(1f, inset.Size.Y - 2f),
            inset.Position + new Vector2(inset.Size.X - 1f, 2f), timber, 2f);
        canvas.DrawCircle(inset.Position + new Vector2(3f, 3f), 1.5f,
            new Color("b79a61"));
        canvas.DrawCircle(inset.End - new Vector2(3f, 3f), 1.5f,
            new Color("b79a61"));
    }

    private static void PaintHearth(
        CanvasItem canvas,
        GridPosition position,
        Color timber,
        bool isCookingFire)
    {
        var center = CellRect(position.X, position.Y).GetCenter();
        canvas.DrawCircle(center, isCookingFire ? 7f : 5f,
            new Color(0.34f, 0.17f, 0.07f, 0.7f));
        canvas.DrawLine(center + new Vector2(-6f, 4f), center + new Vector2(6f, -3f),
            timber, 3f);
        canvas.DrawLine(center + new Vector2(-5f, -3f), center + new Vector2(6f, 4f),
            timber, 3f);
        canvas.DrawCircle(center + new Vector2(0f, -2f), isCookingFire ? 4f : 2.5f,
            new Color(isCookingFire ? "e86b24" : "a73e20"));
        canvas.DrawCircle(center + new Vector2(1f, -3f), isCookingFire ? 2f : 1f,
            new Color("ffd15a"));
        if (!isCookingFire)
        {
            return;
        }

        canvas.DrawLine(center + new Vector2(-7f, -7f), center + new Vector2(-7f, 5f),
            timber, 1.5f);
        canvas.DrawLine(center + new Vector2(7f, -7f), center + new Vector2(7f, 5f),
            timber, 1.5f);
        canvas.DrawLine(center + new Vector2(-8f, -5f), center + new Vector2(8f, -5f),
            new Color("282623"), 1.5f);
        canvas.DrawCircle(center + new Vector2(0f, -4f), 3f, new Color("242526"));
        canvas.DrawCircle(center + new Vector2(0f, -4f), 1.5f, new Color("544331"));
    }

    private static Rect2 CellRect(int x, int y) => new(
        x * TileSize,
        y * TileSize,
        TileSize,
        TileSize);
}
