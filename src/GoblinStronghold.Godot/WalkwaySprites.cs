using Godot;

namespace GoblinStronghold.GodotClient;

internal static class WalkwaySprites
{
    private const int Columns = 3;
    private const int Rows = 2;
    private const string AtlasPath = "res://Assets/World/wooden-walkway-atlas-v2.png";

    public static Texture2D LoadAtlas()
    {
        var image = Image.LoadFromFile(ProjectSettings.GlobalizePath(AtlasPath));
        if (image is null || image.IsEmpty())
        {
            throw new InvalidOperationException($"Cannot load walkway atlas: {AtlasPath}");
        }

        return ImageTexture.CreateFromImage(image);
    }

    public static WalkwaySpritePlacement Resolve(int connectionMask)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(connectionMask, 0);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(connectionMask, 15);

        return connectionMask switch
        {
            0 => new(WalkwaySprite.Isolated, 0f),
            1 => new(WalkwaySprite.End, MathF.PI),
            2 => new(WalkwaySprite.End, -MathF.PI / 2f),
            4 => new(WalkwaySprite.End, 0f),
            8 => new(WalkwaySprite.End, MathF.PI / 2f),
            5 => new(WalkwaySprite.Straight, 0f),
            10 => new(WalkwaySprite.Straight, MathF.PI / 2f),
            3 => new(WalkwaySprite.Corner, 0f),
            6 => new(WalkwaySprite.Corner, MathF.PI / 2f),
            12 => new(WalkwaySprite.Corner, MathF.PI),
            9 => new(WalkwaySprite.Corner, -MathF.PI / 2f),
            14 => new(WalkwaySprite.Tee, 0f),
            13 => new(WalkwaySprite.Tee, MathF.PI / 2f),
            11 => new(WalkwaySprite.Tee, MathF.PI),
            7 => new(WalkwaySprite.Tee, -MathF.PI / 2f),
            15 => new(WalkwaySprite.Cross, 0f),
            _ => throw new InvalidOperationException($"Unsupported walkway mask: {connectionMask}"),
        };
    }

    public static Rect2 GetRegion(Texture2D atlas, WalkwaySprite sprite)
    {
        var index = (int)sprite;
        var column = index % Columns;
        var row = index / Columns;
        var left = column * atlas.GetWidth() / Columns;
        var top = row * atlas.GetHeight() / Rows;
        var right = (column + 1) * atlas.GetWidth() / Columns;
        var bottom = (row + 1) * atlas.GetHeight() / Rows;
        return new Rect2(left, top, right - left, bottom - top);
    }
}

internal enum WalkwaySprite
{
    Isolated,
    End,
    Straight,
    Corner,
    Tee,
    Cross,
}

internal readonly record struct WalkwaySpritePlacement(
    WalkwaySprite Sprite,
    float Rotation);
