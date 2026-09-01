using Godot;

namespace GoblinStronghold.GodotClient;

internal static class SurfaceGrimeSprites
{
    private const string AtlasPath = "res://Assets/World/blood-splatter-atlas-v1.png";
    private const int Columns = 4;
    private const int Rows = 4;

    public static Texture2D LoadAtlas()
    {
        var source = TextureResources.LoadRequired(AtlasPath, "surface grime mask atlas");
        var image = source.GetImage();
        for (var y = 0; y < image.GetHeight(); y++)
        {
            for (var x = 0; x < image.GetWidth(); x++)
            {
                var sourceColor = image.GetPixel(x, y);
                var intensity = Math.Max(sourceColor.R, Math.Max(sourceColor.G, sourceColor.B));
                image.SetPixel(x, y, new Color(
                    0.34f * intensity,
                    0.31f * intensity,
                    0.25f * intensity,
                    sourceColor.A));
            }
        }

        return ImageTexture.CreateFromImage(image);
    }

    public static Rect2 GetRegion(Texture2D atlas, int volume, int variant)
    {
        var column = Math.Abs(variant) % Columns;
        var row = Math.Clamp((volume - 1) / 12, 0, Rows - 1);
        var cellWidth = atlas.GetWidth() / (float)Columns;
        var cellHeight = atlas.GetHeight() / (float)Rows;
        return new Rect2(column * cellWidth, row * cellHeight, cellWidth, cellHeight);
    }
}
