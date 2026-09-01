using Godot;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace GoblinStronghold.GodotClient;

internal static partial class AssetAtlasBaker
{
    private static AssetBakeResult BakeFloorPatternAtlas(string recipeResourcePath)
    {
        var recipePath = ProjectSettings.GlobalizePath(recipeResourcePath);
        var recipeBytes = File.ReadAllBytes(recipePath);
        var recipe = JsonSerializer.Deserialize<FloorPatternAtlasRecipe>(recipeBytes, JsonOptions)
            ?? throw new InvalidDataException(
                $"Cannot deserialize asset recipe: {recipeResourcePath}");
        Validate(recipe, recipeResourcePath);

        var output = Image.CreateEmpty(
            recipe.Output.TileSize * 2,
            recipe.Output.TileSize,
            false,
            Image.Format.Rgba8);
        output.Fill(Colors.Transparent);
        var entries = new List<AssetAtlasEntry>();
        BakePattern(
            "wood",
            0,
            CreateWoodPattern(recipe.Output.TileSize),
            recipe.Shading.Wood);
        BakePattern(
            "stone",
            1,
            CreateStonePattern(recipe.Output.TileSize),
            recipe.Shading.Stone);

        var atlasPath = ProjectSettings.GlobalizePath(recipe.Output.AtlasPath);
        var manifestPath = ProjectSettings.GlobalizePath(recipe.Output.ManifestPath);
        Directory.CreateDirectory(Path.GetDirectoryName(atlasPath)
            ?? throw new InvalidDataException("Output atlas path has no directory."));
        Directory.CreateDirectory(Path.GetDirectoryName(manifestPath)
            ?? throw new InvalidDataException("Output manifest path has no directory."));
        var saveError = output.SavePng(atlasPath);
        if (saveError != Error.Ok)
        {
            throw new IOException($"Godot failed to save {recipe.Output.AtlasPath}: {saveError}");
        }

        var contentHash = ComputeProceduralContentHash(recipeBytes);
        var manifest = new AssetAtlasManifest(
            recipe.SchemaVersion,
            recipe.Id,
            contentHash,
            recipe.Output.TileSize,
            2,
            1,
            entries);
        var manifestJson = JsonSerializer.Serialize(manifest, JsonOptions)
            .Replace("\r", string.Empty, StringComparison.Ordinal);
        File.WriteAllText(manifestPath, manifestJson + "\n", Utf8WithoutBom);
        return new AssetBakeResult(
            recipe.Id,
            recipe.Output.AtlasPath,
            recipe.Output.ManifestPath,
            contentHash,
            entries.Count);

        void BakePattern(
            string id,
            int column,
            string svg,
            FloorPatternShadingProfile shading)
        {
            var tile = new Image();
            if (tile.LoadSvgFromString(svg) != Error.Ok)
            {
                throw new InvalidDataException($"Cannot render floor pattern '{id}'.");
            }
            ApplyFloorTileShading(tile, shading, recipe.Shading.EdgeWidthFraction);
            var destination = new Vector2I(column * recipe.Output.TileSize, 0);
            output.BlitRect(
                tile,
                new Rect2I(Vector2I.Zero, Vector2I.One * recipe.Output.TileSize),
                destination);
            entries.Add(new AssetAtlasEntry(
                "neutral",
                id,
                column,
                destination.X,
                destination.Y,
                recipe.Output.TileSize,
                recipe.Output.TileSize));
        }
    }

    private static void Validate(FloorPatternAtlasRecipe recipe, string recipePath)
    {
        if (recipe.SchemaVersion != 1 || recipe.Kind != "floor-pattern-atlas" ||
            string.IsNullOrWhiteSpace(recipe.Id) || recipe.Output.TileSize < 20 ||
            recipe.Output.TileSize % 20 != 0 ||
            string.IsNullOrWhiteSpace(recipe.Output.AtlasPath) ||
            string.IsNullOrWhiteSpace(recipe.Output.ManifestPath) ||
            recipe.Shading.EdgeWidthFraction is < 0.01f or > 0.5f ||
            !IsValid(recipe.Shading.Wood) || !IsValid(recipe.Shading.Stone))
        {
            throw new InvalidDataException($"Incomplete floor-pattern-atlas recipe: {recipePath}");
        }

        static bool IsValid(FloorPatternShadingProfile profile) =>
            profile.OverallDarkening is >= 0f and <= 0.5f &&
            profile.EdgeDarkening is >= 0f and <= 0.5f;
    }

    private static void ApplyFloorTileShading(
        Image image,
        FloorPatternShadingProfile profile,
        float edgeWidthFraction)
    {
        var edgeWidth = Math.Max(1f, image.GetWidth() * edgeWidthFraction);
        for (var y = 0; y < image.GetHeight(); y++)
        {
            for (var x = 0; x < image.GetWidth(); x++)
            {
                var pixel = image.GetPixel(x, y);
                if (pixel.A <= 0.001f)
                {
                    continue;
                }

                var edgeDistance = Math.Min(
                    Math.Min(x, image.GetWidth() - 1 - x),
                    Math.Min(y, image.GetHeight() - 1 - y));
                var edgeBlend = Mathf.Clamp(edgeDistance / edgeWidth, 0f, 1f);
                var smoothEdgeBlend = edgeBlend * edgeBlend * (3f - 2f * edgeBlend);
                var darkening = profile.OverallDarkening +
                    profile.EdgeDarkening * (1f - smoothEdgeBlend);
                var brightness = 1f - darkening;
                image.SetPixel(x, y, new Color(
                    pixel.R * brightness,
                    pixel.G * brightness,
                    pixel.B * brightness,
                    pixel.A));
            }
        }
    }

    private static string CreateWoodPattern(int size)
    {
        var plankSize = size / 5;
        var inset = plankSize / 4;
        var strokeWidth = Math.Max(1, size / 40);
        var svg = new StringBuilder($"""
            <svg xmlns="http://www.w3.org/2000/svg" width="{size}" height="{size}" viewBox="0 0 {size} {size}">
              <rect width="{size}" height="{size}" fill="#4a4a4a"/>
            """);
        for (var y = 0; y < size; y += plankSize)
        {
            for (var x = 0; x < size; x += plankSize)
            {
                var points = x / plankSize % 2 == 0
                    ? $"{x},{y + plankSize - inset} {x + inset},{y + plankSize} " +
                      $"{x + plankSize},{y + inset} {x + plankSize - inset},{y}"
                    : $"{x},{y + inset} {x + plankSize - inset},{y + plankSize} " +
                      $"{x + plankSize},{y + plankSize - inset} {x + inset},{y}";
                svg.Append($"<polygon points=\"{points}\" fill=\"#888888\" ");
                svg.Append($"stroke=\"#242424\" stroke-width=\"{strokeWidth}\"/>\n");
            }
        }
        svg.Append("</svg>");
        return svg.ToString();
    }

    private static string CreateStonePattern(int size)
    {
        var slabSize = size / 5;
        var gap = Math.Max(1, size / 80);
        var shades = new[] { "4a4a4a", "888888", "c6c6c6" };
        var svg = new StringBuilder($"""
            <svg xmlns="http://www.w3.org/2000/svg" width="{size}" height="{size}" viewBox="0 0 {size} {size}">
              <rect width="{size}" height="{size}" fill="#242424"/>
            """);
        for (var y = 0; y < size; y += slabSize)
        {
            for (var x = 0; x < size; x += slabSize)
            {
                var shade = shades[((x + y) / slabSize) % shades.Length];
                svg.Append($"<rect x=\"{x + gap}\" y=\"{y + gap}\" ");
                svg.Append($"width=\"{slabSize - gap * 2}\" height=\"{slabSize - gap * 2}\" ");
                svg.Append($"fill=\"#{shade}\"/>\n");
            }
        }
        svg.Append("</svg>");
        return svg.ToString();
    }

    private static string ComputeProceduralContentHash(byte[] recipeBytes)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(Encoding.UTF8.GetBytes(BakerVersion));
        hash.AppendData(recipeBytes);
        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }
}

internal sealed class FloorPatternAtlasRecipe
{
    public int SchemaVersion { get; set; }

    public string Kind { get; set; } = string.Empty;

    public string Id { get; set; } = string.Empty;

    public ConnectedAtlasOutput Output { get; set; } = new();

    public FloorPatternShadingSettings Shading { get; set; } = new();
}

internal sealed class FloorPatternShadingSettings
{
    public float EdgeWidthFraction { get; set; } = 0.1f;

    public FloorPatternShadingProfile Wood { get; set; } = new();

    public FloorPatternShadingProfile Stone { get; set; } = new();
}

internal sealed class FloorPatternShadingProfile
{
    public float OverallDarkening { get; set; }

    public float EdgeDarkening { get; set; }
}
