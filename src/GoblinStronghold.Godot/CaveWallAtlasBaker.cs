using Godot;
using System.Text.Json;

namespace GoblinStronghold.GodotClient;

internal static partial class AssetAtlasBaker
{
    private const int CaveWallMaskCount = 16;
    private const int CaveInnerCornerCount = 4;
    private const int CaveWallAtlasColumns = CaveWallMaskCount + CaveInnerCornerCount;

    private static AssetBakeResult BakeCaveWallAtlas(string recipeResourcePath)
    {
        var recipePath = ProjectSettings.GlobalizePath(recipeResourcePath);
        var recipeBytes = File.ReadAllBytes(recipePath);
        var recipe = JsonSerializer.Deserialize<CaveWallAtlasRecipe>(recipeBytes, JsonOptions)
            ?? throw new InvalidDataException($"Cannot deserialize asset recipe: {recipeResourcePath}");
        ValidateCaveWallRecipe(recipe, recipeResourcePath);

        var sourcePath = ProjectSettings.GlobalizePath(recipe.Source.Path);
        var sourceBytes = File.ReadAllBytes(sourcePath);
        var source = Image.LoadFromFile(sourcePath);
        if (source is null || source.IsEmpty())
        {
            throw new InvalidDataException($"Cannot load source atlas: {recipe.Source.Path}");
        }

        source.Convert(Image.Format.Rgba8);
        var output = Image.CreateEmpty(
            CaveWallAtlasColumns * recipe.Output.TileSize,
            recipe.Materials.Count * recipe.Output.TileSize,
            false,
            Image.Format.Rgba8);
        output.Fill(Colors.Transparent);
        var entries = new List<AssetAtlasEntry>();
        for (var materialIndex = 0; materialIndex < recipe.Materials.Count; materialIndex++)
        {
            var material = recipe.Materials[materialIndex];
            var baseWall = ExtractSourceCell(source, recipe, material.SourceCell);
            var orientedWalls = CreateOrientedWalls(baseWall);
            for (var mask = 0; mask < CaveWallMaskCount; mask++)
            {
                var tile = ComposeCaveWall(orientedWalls, mask, recipe.BlendExponent);
                var destination = new Vector2I(
                    mask * recipe.Output.TileSize,
                    materialIndex * recipe.Output.TileSize);
                output.BlitRect(
                    tile,
                    new Rect2I(Vector2I.Zero, new Vector2I(
                        recipe.Output.TileSize,
                        recipe.Output.TileSize)),
                    destination);
                entries.Add(new AssetAtlasEntry(
                    material.Id,
                    $"open-neighbor-mask-{mask:X2}",
                    mask,
                    destination.X,
                    destination.Y,
                    recipe.Output.TileSize,
                    recipe.Output.TileSize));
            }

            for (var corner = 0; corner < CaveInnerCornerCount; corner++)
            {
                var tile = ComposeCaveInnerCorner(baseWall, corner, recipe.InnerCorners);
                var outputIndex = CaveWallMaskCount + corner;
                var destination = new Vector2I(
                    outputIndex * recipe.Output.TileSize,
                    materialIndex * recipe.Output.TileSize);
                output.BlitRect(
                    tile,
                    new Rect2I(Vector2I.Zero, new Vector2I(
                        recipe.Output.TileSize,
                        recipe.Output.TileSize)),
                    destination);
                entries.Add(new AssetAtlasEntry(
                    material.Id,
                    $"inner-corner-{corner}",
                    outputIndex,
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
            CaveWallAtlasColumns,
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

    private static Image ExtractSourceCell(
        Image source,
        CaveWallAtlasRecipe recipe,
        int sourceCell)
    {
        var column = sourceCell % recipe.Source.Columns;
        var row = sourceCell / recipe.Source.Columns;
        var left = column * source.GetWidth() / recipe.Source.Columns;
        var top = row * source.GetHeight() / recipe.Source.Rows;
        var right = (column + 1) * source.GetWidth() / recipe.Source.Columns;
        var bottom = (row + 1) * source.GetHeight() / recipe.Source.Rows;
        var tile = source.GetRegion(new Rect2I(
            left + recipe.SourcePadding,
            top + recipe.SourcePadding,
            right - left - (recipe.SourcePadding * 2),
            bottom - top - (recipe.SourcePadding * 2)));
        tile.Resize(recipe.Output.TileSize, recipe.Output.TileSize, Image.Interpolation.Lanczos);
        return tile;
    }

    private static Image[] CreateOrientedWalls(Image baseWall)
    {
        var result = new Image[4];
        for (var turns = 0; turns < result.Length; turns++)
        {
            result[turns] = baseWall.GetRegion(new Rect2I(
                Vector2I.Zero,
                new Vector2I(baseWall.GetWidth(), baseWall.GetHeight())));
            Rotate(result[turns], turns);
        }

        return result;
    }

    private static Image ComposeCaveWall(Image[] orientedWalls, int mask, float blendExponent)
    {
        var size = orientedWalls[0].GetWidth();
        var result = Image.CreateEmpty(size, size, false, Image.Format.Rgba8);
        for (var y = 0; y < size; y++)
        {
            var normalizedY = y / (float)(size - 1);
            for (var x = 0; x < size; x++)
            {
                var normalizedX = x / (float)(size - 1);
                Span<float> weights =
                [
                    MathF.Pow(1f - normalizedY, blendExponent),
                    MathF.Pow(normalizedX, blendExponent),
                    MathF.Pow(normalizedY, blendExponent),
                    MathF.Pow(1f - normalizedX, blendExponent),
                ];
                var totalWeight = 0f;
                var red = 0f;
                var green = 0f;
                var blue = 0f;
                for (var edge = 0; edge < 4; edge++)
                {
                    if ((mask & (1 << edge)) == 0)
                    {
                        continue;
                    }

                    var weight = weights[edge];
                    var pixel = orientedWalls[(edge + 2) % 4].GetPixel(x, y);
                    red += pixel.R * weight;
                    green += pixel.G * weight;
                    blue += pixel.B * weight;
                    totalWeight += weight;
                }

                result.SetPixel(x, y, totalWeight <= 0.0001f
                    ? new Color(0.04f, 0.045f, 0.05f, 1f)
                    : new Color(
                        red / totalWeight,
                        green / totalWeight,
                        blue / totalWeight,
                        1f));
            }
        }

        return result;
    }

    private static Image ComposeCaveInnerCorner(
        Image baseWall,
        int corner,
        CaveInnerCornerRecipe settings)
    {
        var size = baseWall.GetWidth();
        var result = Image.CreateEmpty(size, size, false, Image.Format.Rgba8);
        result.Fill(Colors.Transparent);
        var useEast = corner is 1 or 2;
        var useSouth = corner is 2 or 3;
        var baseRadius = size * settings.RadiusFraction;
        for (var y = 0; y < size; y++)
        {
            var distanceY = useSouth ? size - 1 - y : y;
            for (var x = 0; x < size; x++)
            {
                var distanceX = useEast ? size - 1 - x : x;
                var angle = MathF.Atan2(distanceY, distanceX);
                var roughnessEnvelope = MathF.Sin(angle * 2f);
                var edgeNoise =
                    (MathF.Sin(angle * 7f + corner * 0.73f) * settings.Roughness * 0.64f +
                     MathF.Sin(angle * 13f + corner * 1.91f) * settings.Roughness * 0.33f) *
                    roughnessEnvelope;
                var radius = baseRadius * (1f + edgeNoise);
                var distance = MathF.Sqrt(distanceX * distanceX + distanceY * distanceY);
                if (distance > radius + settings.FeatherPixels)
                {
                    continue;
                }

                var depth = Math.Clamp(distance / radius, 0f, 1f);
                var sourceX = Math.Clamp(
                    (int)MathF.Round(angle / (MathF.PI * 0.5f) * (size - 1)),
                    0,
                    size - 1);
                var sourceY = Math.Clamp(
                    (int)MathF.Round((1f - depth) * (size - 1)),
                    0,
                    size - 1);
                var color = baseWall.GetPixel(sourceX, sourceY);
                var alpha = Math.Clamp(
                    (radius + settings.FeatherPixels - distance) / settings.FeatherPixels,
                    0f,
                    1f);
                result.SetPixel(x, y, new Color(color.R, color.G, color.B, alpha));
            }
        }

        return result;
    }

    private static void ValidateCaveWallRecipe(CaveWallAtlasRecipe recipe, string recipePath)
    {
        var sourceCellCount = checked(recipe.Source.Columns * recipe.Source.Rows);
        if (recipe.SchemaVersion != 3 || recipe.Kind != "cave-wall-atlas" ||
            string.IsNullOrWhiteSpace(recipe.Id) || string.IsNullOrWhiteSpace(recipe.Source.Path) ||
            recipe.Source.Columns <= 0 || recipe.Source.Rows <= 0 ||
            recipe.Output.TileSize <= 0 || string.IsNullOrWhiteSpace(recipe.Output.AtlasPath) ||
            string.IsNullOrWhiteSpace(recipe.Output.ManifestPath) || recipe.Materials.Count == 0 ||
            recipe.SourcePadding < 0 || recipe.BlendExponent is < 0.5f or > 8f ||
            recipe.InnerCorners.RadiusFraction is < 0.25f or > 1.15f ||
            recipe.InnerCorners.Roughness is < 0f or > 0.25f ||
            recipe.InnerCorners.FeatherPixels is <= 0f or > 8f ||
            recipe.Materials.Any(material => string.IsNullOrWhiteSpace(material.Id) ||
                material.SourceCell < 0 || material.SourceCell >= sourceCellCount) ||
            recipe.Materials.Select(material => material.Id)
                .Distinct(StringComparer.Ordinal).Count() != recipe.Materials.Count)
        {
            throw new InvalidDataException($"Invalid cave-wall recipe: {recipePath}");
        }
    }
}

internal sealed class CaveWallAtlasRecipe
{
    public int SchemaVersion { get; set; }

    public string Kind { get; set; } = string.Empty;

    public string Id { get; set; } = string.Empty;

    public ConnectedAtlasSource Source { get; set; } = new();

    public ConnectedAtlasOutput Output { get; set; } = new();

    public List<HeightTransitionMaterial> Materials { get; set; } = [];

    public int SourcePadding { get; set; }

    public float BlendExponent { get; set; }

    public CaveInnerCornerRecipe InnerCorners { get; set; } = new();
}

internal sealed class CaveInnerCornerRecipe
{
    public float RadiusFraction { get; set; }

    public float Roughness { get; set; }

    public float FeatherPixels { get; set; }
}
