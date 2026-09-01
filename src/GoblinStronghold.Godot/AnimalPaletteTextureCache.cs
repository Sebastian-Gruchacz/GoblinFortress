using Godot;
using GoblinStronghold.Simulation.Animals;

namespace GoblinStronghold.GodotClient;

internal sealed class AnimalPaletteTextureCache : IDisposable
{
    private readonly Dictionary<CacheKey, ImageTexture> textures = [];

    internal Texture2D Get(
        Texture2D atlas,
        Rect2 region,
        AnimalSpeciesDefinition species)
    {
        var pixelRegion = new Rect2I(
            Mathf.RoundToInt(region.Position.X),
            Mathf.RoundToInt(region.Position.Y),
            Mathf.RoundToInt(region.Size.X),
            Mathf.RoundToInt(region.Size.Y));
        var key = new CacheKey(atlas.GetInstanceId(), pixelRegion, species.Id.Value);
        if (textures.TryGetValue(key, out var cached))
        {
            return cached;
        }

        var image = atlas.GetImage().GetRegion(pixelRegion);
        image.Convert(Image.Format.Rgba8);
        ApplyPalette(image, species.Visual.Palette);
        var texture = ImageTexture.CreateFromImage(image);
        textures.Add(key, texture);
        return texture;
    }

    public void Dispose()
    {
        foreach (var texture in textures.Values)
        {
            texture.Dispose();
        }
        textures.Clear();
    }

    private static void ApplyPalette(
        Image image,
        IReadOnlyDictionary<string, string> palette)
    {
        var edge = Color.FromHtml(palette["edge"]);
        var shadow = Color.FromHtml(palette["shadow"]);
        var midtone = Color.FromHtml(palette["midtone"]);
        var highlight = Color.FromHtml(palette["highlight"]);
        for (var y = 0; y < image.GetHeight(); y++)
        {
            for (var x = 0; x < image.GetWidth(); x++)
            {
                var source = image.GetPixel(x, y);
                if (source.A <= 0.001f)
                {
                    continue;
                }
                var luminance = source.R * 0.2126f + source.G * 0.7152f +
                    source.B * 0.0722f;
                var replacement = Sample(edge, shadow, midtone, highlight, luminance);
                image.SetPixel(x, y, new Color(
                    replacement.R,
                    replacement.G,
                    replacement.B,
                    source.A));
            }
        }
    }

    private static Color Sample(
        Color edge,
        Color shadow,
        Color midtone,
        Color highlight,
        float luminance)
    {
        var normalized = Mathf.Clamp((luminance - 0.02f) / 0.78f, 0f, 1f);
        if (normalized < 0.28f)
        {
            return edge.Lerp(shadow, normalized / 0.28f);
        }
        if (normalized < 0.64f)
        {
            return shadow.Lerp(midtone, (normalized - 0.28f) / 0.36f);
        }
        return midtone.Lerp(highlight, (normalized - 0.64f) / 0.36f);
    }

    private readonly record struct CacheKey(
        ulong AtlasId,
        Rect2I Region,
        string SpeciesId);
}
