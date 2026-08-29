using Godot;

namespace GoblinStronghold.GodotClient;

internal enum StructureWallMaterial
{
    HumanOak = 0,
    GoblinBogwood = 1,
}

internal static class StructureWallSprites
{
    private const int Columns = 16;
    private const int Rows = 2;
    private const string AtlasPath = "res://Assets/Generated/connected-structure-walls-v1.png";

    public static Texture2D LoadAtlas()
        => TextureResources.LoadRequired(AtlasPath, "structure wall atlas");

    public static Rect2 GetRegion(
        Texture2D atlas,
        StructureWallMaterial material,
        int connectionMask)
    {
        if (connectionMask is < 0 or >= Columns || !Enum.IsDefined(material))
        {
            throw new ArgumentOutOfRangeException(nameof(connectionMask));
        }

        var row = (int)material;
        var left = connectionMask * atlas.GetWidth() / Columns;
        var top = row * atlas.GetHeight() / Rows;
        var right = (connectionMask + 1) * atlas.GetWidth() / Columns;
        var bottom = (row + 1) * atlas.GetHeight() / Rows;
        return new Rect2(left, top, right - left, bottom - top);
    }
}
