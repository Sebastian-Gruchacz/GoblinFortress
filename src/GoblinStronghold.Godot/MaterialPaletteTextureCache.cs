using Godot;
using GoblinStronghold.Simulation.Resources;

namespace GoblinStronghold.GodotClient;

internal enum MaterialPaletteTextureProfile
{
    CompleteSurface,
    IllustratedTimber,
}

internal sealed class MaterialPaletteTextureCache : IDisposable
{
    private readonly Dictionary<CacheKey, ImageTexture> _textures = [];

    public Texture2D Get(
        Texture2D atlas,
        Rect2 region,
        ResourceVariant materialVariant,
        MaterialPaletteTextureProfile profile)
    {
        if (materialVariant == ResourceVariant.None)
        {
            throw new ArgumentOutOfRangeException(
                nameof(materialVariant),
                "A concrete material variant is required for palette replacement.");
        }

        var pixelRegion = new Rect2I(
            Mathf.RoundToInt(region.Position.X),
            Mathf.RoundToInt(region.Position.Y),
            Mathf.RoundToInt(region.Size.X),
            Mathf.RoundToInt(region.Size.Y));
        var key = new CacheKey(atlas.GetInstanceId(), pixelRegion, materialVariant, profile);
        if (_textures.TryGetValue(key, out var cached))
        {
            return cached;
        }

        var image = atlas.GetImage().GetRegion(pixelRegion);
        image.Convert(Image.Format.Rgba8);
        ApplyPalette(image, MaterialPaletteColors.For(materialVariant), profile);
        var texture = ImageTexture.CreateFromImage(image);
        _textures.Add(key, texture);
        return texture;
    }

    public void Dispose()
    {
        foreach (var texture in _textures.Values)
        {
            texture.Dispose();
        }
        _textures.Clear();
    }

    private static void ApplyPalette(
        Image image,
        MaterialPaletteColors palette,
        MaterialPaletteTextureProfile profile)
    {
        for (var y = 0; y < image.GetHeight(); y++)
        {
            for (var x = 0; x < image.GetWidth(); x++)
            {
                var source = image.GetPixel(x, y);
                if (source.A <= 0.001f ||
                    profile == MaterialPaletteTextureProfile.IllustratedTimber &&
                    !IsIllustratedTimber(source))
                {
                    continue;
                }

                var luminance = (source.R * 0.2126f) +
                    (source.G * 0.7152f) +
                    (source.B * 0.0722f);
                var replacement = SamplePalette(palette, luminance);
                var strength = profile == MaterialPaletteTextureProfile.CompleteSurface
                    ? 0.92f
                    : TimberConfidence(source);
                var result = source.Lerp(replacement, strength);
                image.SetPixel(x, y, new Color(result.R, result.G, result.B, source.A));
            }
        }
    }

    private static Color SamplePalette(MaterialPaletteColors palette, float luminance)
    {
        var normalized = Mathf.Clamp((luminance - 0.06f) / 0.68f, 0f, 1f);
        if (normalized < 0.28f)
        {
            return palette.Edge.Lerp(palette.Shadow, normalized / 0.28f);
        }
        if (normalized < 0.62f)
        {
            return palette.Shadow.Lerp(
                palette.Midtone,
                (normalized - 0.28f) / 0.34f);
        }
        return palette.Midtone.Lerp(
            palette.Highlight,
            (normalized - 0.62f) / 0.38f);
    }

    private static bool IsIllustratedTimber(Color color)
    {
        var maximum = Math.Max(color.R, Math.Max(color.G, color.B));
        var minimum = Math.Min(color.R, Math.Min(color.G, color.B));
        var saturation = maximum <= 0.001f ? 0f : (maximum - minimum) / maximum;
        var hue = Hue(color, maximum, minimum);
        return maximum is >= 0.09f and <= 0.82f &&
            saturation is >= 0.16f and <= 0.78f &&
            hue is >= 0.035f and <= 0.155f;
    }

    private static float TimberConfidence(Color color)
    {
        var maximum = Math.Max(color.R, Math.Max(color.G, color.B));
        var minimum = Math.Min(color.R, Math.Min(color.G, color.B));
        var saturation = maximum <= 0.001f ? 0f : (maximum - minimum) / maximum;
        var hue = Hue(color, maximum, minimum);
        var hueConfidence = 1f - Mathf.Clamp(Math.Abs(hue - 0.095f) / 0.065f, 0f, 1f);
        var saturationConfidence = 1f - Mathf.Clamp(Math.Abs(saturation - 0.46f) / 0.34f, 0f, 1f);
        return 0.58f + (0.34f * hueConfidence * saturationConfidence);
    }

    private static float Hue(Color color, float maximum, float minimum)
    {
        var delta = maximum - minimum;
        if (delta <= 0.001f)
        {
            return 0f;
        }

        float hue;
        if (maximum == color.R)
        {
            hue = ((color.G - color.B) / delta) % 6f;
        }
        else if (maximum == color.G)
        {
            hue = ((color.B - color.R) / delta) + 2f;
        }
        else
        {
            hue = ((color.R - color.G) / delta) + 4f;
        }
        return ((hue / 6f) + 1f) % 1f;
    }

    private readonly record struct CacheKey(
        ulong AtlasId,
        Rect2I Region,
        ResourceVariant MaterialVariant,
        MaterialPaletteTextureProfile Profile);
}
