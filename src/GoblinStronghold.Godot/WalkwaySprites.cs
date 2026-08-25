using Godot;

namespace GoblinStronghold.GodotClient;

internal static class WalkwaySprites
{
    private const int Columns = 16;
    private const int Rows = 3;
    private const string AtlasPath = "res://Assets/Generated/connected-walkways-v1.png";

    public static Texture2D LoadAtlas()
    {
        var image = Image.LoadFromFile(ProjectSettings.GlobalizePath(AtlasPath));
        if (image is null || image.IsEmpty())
        {
            throw new InvalidOperationException($"Cannot load walkway atlas: {AtlasPath}");
        }

        return ImageTexture.CreateFromImage(image);
    }

    public static Rect2 GetRegion(
        Texture2D atlas,
        int connectionMask,
        WalkwayMaterial material = WalkwayMaterial.Bogwood)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(connectionMask, 0);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(connectionMask, Columns - 1);
        var row = (int)material;
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(row, Rows);
        var left = connectionMask * atlas.GetWidth() / Columns;
        var top = row * atlas.GetHeight() / Rows;
        var right = (connectionMask + 1) * atlas.GetWidth() / Columns;
        var bottom = (row + 1) * atlas.GetHeight() / Rows;
        return new Rect2(left, top, right - left, bottom - top);
    }
}

internal enum WalkwayMaterial
{
    Bogwood,
    Oak,
    Pine,
}
