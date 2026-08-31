using Godot;
using GoblinStronghold.Simulation.Resources;

namespace GoblinStronghold.GodotClient;

internal enum MaterialPaletteTextureProfile
{
    CompleteSurface,
    IllustratedTimber,
}

internal enum MaterialResourceIconShape
{
    Ore,
    Ingot,
}

internal sealed class MaterialPaletteTextureCache : IDisposable
{
    private readonly Dictionary<CacheKey, ImageTexture> _textures = [];
    private readonly Dictionary<MaterialResourceIconShape, ImageTexture> _resourceIconBases = [];
    private ImageTexture? _coalIcon;

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

    public Texture2D GetResourceIcon(
        ResourceVariant materialVariant,
        MaterialResourceIconShape shape)
    {
        if (!_resourceIconBases.TryGetValue(shape, out var source))
        {
            source = CreateSvgTexture(shape switch
            {
                MaterialResourceIconShape.Ore => OreIconSvg,
                MaterialResourceIconShape.Ingot => IngotIconSvg,
                _ => throw new ArgumentOutOfRangeException(nameof(shape)),
            });
            _resourceIconBases.Add(shape, source);
        }

        return Get(
            source,
            new Rect2(Vector2.Zero, source.GetSize()),
            materialVariant,
            MaterialPaletteTextureProfile.CompleteSurface);
    }

    public Texture2D GetCoalIcon() => _coalIcon ??= CreateSvgTexture(CoalIconSvg);

    public void Dispose()
    {
        foreach (var texture in _textures.Values)
        {
            texture.Dispose();
        }
        _textures.Clear();
        foreach (var texture in _resourceIconBases.Values)
        {
            texture.Dispose();
        }
        _resourceIconBases.Clear();
        _coalIcon?.Dispose();
        _coalIcon = null;
    }

    private static ImageTexture CreateSvgTexture(string svg)
    {
        var image = new Image();
        if (image.LoadSvgFromString(svg) != Error.Ok)
        {
            throw new InvalidOperationException("Cannot create a material resource icon.");
        }
        return ImageTexture.CreateFromImage(image);
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

    private const string OreIconSvg = """
        <svg xmlns="http://www.w3.org/2000/svg" width="64" height="64" viewBox="0 0 64 64">
          <ellipse cx="32" cy="53" rx="24" ry="5" fill="#252525" opacity=".42"/>
          <path d="M8 47 L13 31 L25 25 L34 32 L31 49 L19 55 Z" fill="#686868" stroke="#171717" stroke-width="3" stroke-linejoin="round"/>
          <path d="M26 49 L29 27 L42 18 L54 27 L56 44 L45 54 Z" fill="#777777" stroke="#171717" stroke-width="3" stroke-linejoin="round"/>
          <path d="M17 31 L25 27 L30 33 L24 39 L13 39 Z" fill="#adadad"/>
          <path d="M35 28 L42 21 L50 27 L46 34 L38 36 Z" fill="#c9c9c9"/>
          <path d="M36 42 L45 35 L53 39 L48 49 L39 51 Z" fill="#999999"/>
          <path d="M15 47 L22 40 L28 44 L25 51 Z" fill="#d5d5d5"/>
        </svg>
        """;

    private const string IngotIconSvg = """
        <svg xmlns="http://www.w3.org/2000/svg" width="64" height="64" viewBox="0 0 64 64">
          <ellipse cx="32" cy="52" rx="25" ry="5" fill="#252525" opacity=".4"/>
          <path d="M8 41 L15 29 L43 29 L51 41 L44 49 L16 49 Z" fill="#6f6f6f" stroke="#171717" stroke-width="3" stroke-linejoin="round"/>
          <path d="M15 29 L21 34 L45 34 L43 29 Z" fill="#cfcfcf"/>
          <path d="M17 31 L22 20 L48 20 L56 31 L50 39 L23 39 Z" fill="#858585" stroke="#171717" stroke-width="3" stroke-linejoin="round"/>
          <path d="M22 20 L27 25 L50 25 L48 20 Z" fill="#e0e0e0"/>
          <path d="M26 27 L45 27" fill="none" stroke="#b5b5b5" stroke-width="2" stroke-linecap="round"/>
        </svg>
        """;

    private const string CoalIconSvg = """
        <svg xmlns="http://www.w3.org/2000/svg" width="64" height="64" viewBox="0 0 64 64">
          <ellipse cx="32" cy="53" rx="24" ry="5" fill="#050608" opacity=".6"/>
          <path d="M8 46 L13 31 L24 24 L34 31 L31 49 L19 55 Z" fill="#181b20" stroke="#050608" stroke-width="3" stroke-linejoin="round"/>
          <path d="M27 49 L30 26 L42 17 L54 27 L56 44 L45 54 Z" fill="#20242a" stroke="#050608" stroke-width="3" stroke-linejoin="round"/>
          <path d="M16 32 L24 27 L29 33 L23 39 L13 39 Z" fill="#343a43"/>
          <path d="M36 27 L42 21 L49 27 L45 33 L39 35 Z" fill="#4d5560"/>
          <path d="M36 42 L45 35 L52 39 L47 48 L40 50 Z" fill="#2d323a"/>
          <path d="M40 23 L44 21" fill="none" stroke="#727c88" stroke-width="2" stroke-linecap="round"/>
        </svg>
        """;
}
