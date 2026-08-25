using Godot;
using System.Text.Json;

namespace GoblinStronghold.GodotClient;

internal static partial class AssetAtlasBaker
{
    private const int HeightMaskCount = 16;

    private static AssetBakeResult BakeHeightTransitionAtlas(string recipeResourcePath)
    {
        var recipePath = ProjectSettings.GlobalizePath(recipeResourcePath);
        var recipeBytes = File.ReadAllBytes(recipePath);
        var recipe = JsonSerializer.Deserialize<HeightTransitionAtlasRecipe>(recipeBytes, JsonOptions)
            ?? throw new InvalidDataException($"Cannot deserialize asset recipe: {recipeResourcePath}");
        ValidateHeightTransitionRecipe(recipe, recipeResourcePath);

        var sourcePath = ProjectSettings.GlobalizePath(recipe.Source.Path);
        var sourceBytes = File.ReadAllBytes(sourcePath);
        var source = Image.LoadFromFile(sourcePath);
        if (source is null || source.IsEmpty())
        {
            throw new InvalidDataException($"Cannot load source atlas: {recipe.Source.Path}");
        }

        source.Convert(Image.Format.Rgba8);
        var output = Image.CreateEmpty(
            HeightMaskCount * recipe.Output.TileSize,
            recipe.Materials.Count * recipe.Output.TileSize,
            false,
            Image.Format.Rgba8);
        output.Fill(Colors.Transparent);
        var entries = new List<AssetAtlasEntry>();
        for (var materialIndex = 0; materialIndex < recipe.Materials.Count; materialIndex++)
        {
            var material = recipe.Materials[materialIndex];
            var sourceColumn = material.SourceCell % recipe.Source.Columns;
            var sourceRow = material.SourceCell / recipe.Source.Columns;
            var sourceLeft = sourceColumn * source.GetWidth() / recipe.Source.Columns;
            var sourceTop = sourceRow * source.GetHeight() / recipe.Source.Rows;
            var sourceRight = (sourceColumn + 1) * source.GetWidth() / recipe.Source.Columns;
            var sourceBottom = (sourceRow + 1) * source.GetHeight() / recipe.Source.Rows;
            if (sourceRight - sourceLeft <= recipe.SourcePadding * 2 ||
                sourceBottom - sourceTop <= recipe.SourcePadding * 2)
            {
                throw new InvalidDataException(
                    $"Source padding removes the entire '{material.Id}' cell in {recipeResourcePath}.");
            }

            var baseTile = source.GetRegion(new Rect2I(
                sourceLeft + recipe.SourcePadding,
                sourceTop + recipe.SourcePadding,
                sourceRight - sourceLeft - (recipe.SourcePadding * 2),
                sourceBottom - sourceTop - (recipe.SourcePadding * 2)));
            baseTile.Resize(
                recipe.Output.TileSize,
                recipe.Output.TileSize,
                Image.Interpolation.Lanczos);
            for (var heightMask = 0; heightMask < HeightMaskCount; heightMask++)
            {
                var tile = DeformHeightTransition(baseTile, heightMask, recipe);
                var destination = new Vector2I(
                    heightMask * recipe.Output.TileSize,
                    materialIndex * recipe.Output.TileSize);
                output.BlitRect(
                    tile,
                    new Rect2I(Vector2I.Zero, new Vector2I(recipe.Output.TileSize, recipe.Output.TileSize)),
                    destination);
                entries.Add(new AssetAtlasEntry(
                    material.Id,
                    $"height-mask-{heightMask:X2}",
                    heightMask,
                    destination.X,
                    destination.Y,
                    recipe.Output.TileSize,
                    recipe.Output.TileSize));
            }
        }

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

        var contentHash = ComputeContentHash(recipeBytes, sourceBytes);
        var manifest = new AssetAtlasManifest(
            recipe.SchemaVersion,
            recipe.Id,
            contentHash,
            recipe.Output.TileSize,
            HeightMaskCount,
            recipe.Materials.Count,
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
    }

    private static Image DeformHeightTransition(
        Image source,
        int heightMask,
        HeightTransitionAtlasRecipe recipe)
    {
        var size = recipe.Output.TileSize;
        var result = Image.CreateEmpty(size, size, false, Image.Format.Rgba8);
        var north = (heightMask & 1) != 0 ? 1f : 0f;
        var east = (heightMask & 2) != 0 ? 1f : 0f;
        var south = (heightMask & 4) != 0 ? 1f : 0f;
        var west = (heightMask & 8) != 0 ? 1f : 0f;
        var activeEdges = Math.Max(1f, north + east + south + west);
        var gradientX = (east - west) / activeEdges;
        var gradientY = (south - north) / activeEdges;
        var light = new Vector2(-0.72f, -0.69f);
        var lightFactor = ((gradientX * light.X) + (gradientY * light.Y)) * recipe.ShadingStrength;
        for (var y = 0; y < size; y++)
        {
            var normalizedY = y / (float)(size - 1);
            for (var x = 0; x < size; x++)
            {
                var normalizedX = x / (float)(size - 1);
                var height = ((north * (1f - normalizedY)) +
                    (east * normalizedX) +
                    (south * normalizedY) +
                    (west * (1f - normalizedX))) / activeEdges;
                var displacement = (height - 0.5f) * recipe.DeformationStrength * size;
                var sourceX = Math.Clamp(
                    Mathf.RoundToInt(x - (gradientX * displacement)), 0, size - 1);
                var sourceY = Math.Clamp(
                    Mathf.RoundToInt(y - (gradientY * displacement)), 0, size - 1);
                var pixel = source.GetPixel(sourceX, sourceY);
                if (pixel.A <= 0.001f)
                {
                    result.SetPixel(x, y, pixel);
                    continue;
                }

                var highEdge = Math.Max(
                    Math.Max(north * EdgeBand(normalizedY), south * EdgeBand(1f - normalizedY)),
                    Math.Max(west * EdgeBand(normalizedX), east * EdgeBand(1f - normalizedX)));
                var lowEdge = Math.Max(
                    Math.Max(north * EdgeBand(1f - normalizedY), south * EdgeBand(normalizedY)),
                    Math.Max(west * EdgeBand(1f - normalizedX), east * EdgeBand(normalizedX)));
                var brightness = 1f + lightFactor +
                    (highEdge * recipe.EdgeHighlightStrength) -
                    (lowEdge * recipe.EdgeShadowStrength);
                result.SetPixel(x, y, new Color(
                    Math.Clamp(pixel.R * brightness, 0f, 1f),
                    Math.Clamp(pixel.G * brightness, 0f, 1f),
                    Math.Clamp(pixel.B * brightness, 0f, 1f),
                    pixel.A));
            }
        }

        return result;
    }

    private static float EdgeBand(float distanceFromEdge) =>
        MathF.Pow(Math.Clamp(1f - (distanceFromEdge * 7.5f), 0f, 1f), 2f);

    private static void ValidateHeightTransitionRecipe(
        HeightTransitionAtlasRecipe recipe,
        string recipePath)
    {
        if (recipe.SchemaVersion != 1 || recipe.Kind != "height-transition-atlas" ||
            string.IsNullOrWhiteSpace(recipe.Id) || string.IsNullOrWhiteSpace(recipe.Source.Path) ||
            recipe.Source.Columns <= 0 || recipe.Source.Rows <= 0 ||
            recipe.Output.TileSize <= 0 || string.IsNullOrWhiteSpace(recipe.Output.AtlasPath) ||
            string.IsNullOrWhiteSpace(recipe.Output.ManifestPath) || recipe.Materials.Count == 0 ||
            recipe.SourcePadding < 0 ||
            recipe.DeformationStrength is < 0f or > 0.25f ||
            recipe.ShadingStrength is < 0f or > 0.5f ||
            recipe.EdgeHighlightStrength is < 0f or > 0.5f ||
            recipe.EdgeShadowStrength is < 0f or > 0.5f)
        {
            throw new InvalidDataException($"Incomplete height-transition recipe: {recipePath}");
        }

        var sourceCellCount = checked(recipe.Source.Columns * recipe.Source.Rows);
        if (recipe.Materials.Any(item => string.IsNullOrWhiteSpace(item.Id) ||
                item.SourceCell < 0 || item.SourceCell >= sourceCellCount) ||
            recipe.Materials.Select(item => item.Id).Distinct(StringComparer.Ordinal).Count() !=
                recipe.Materials.Count)
        {
            throw new InvalidDataException($"Invalid material source in recipe: {recipePath}");
        }
    }
}

internal sealed class HeightTransitionAtlasRecipe
{
    public int SchemaVersion { get; set; }

    public string Kind { get; set; } = string.Empty;

    public string Id { get; set; } = string.Empty;

    public ConnectedAtlasSource Source { get; set; } = new();

    public ConnectedAtlasOutput Output { get; set; } = new();

    public List<HeightTransitionMaterial> Materials { get; set; } = [];

    public int SourcePadding { get; set; }

    public float DeformationStrength { get; set; }

    public float ShadingStrength { get; set; }

    public float EdgeHighlightStrength { get; set; }

    public float EdgeShadowStrength { get; set; }
}

internal sealed class HeightTransitionMaterial
{
    public string Id { get; set; } = string.Empty;

    public int SourceCell { get; set; }
}
