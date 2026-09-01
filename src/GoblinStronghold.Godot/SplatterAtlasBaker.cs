using Godot;
using System.Text.Json;

namespace GoblinStronghold.GodotClient;

internal static partial class AssetAtlasBaker
{
    private static AssetBakeResult BakeSplatterAtlas(string recipeResourcePath)
    {
        var recipePath = ProjectSettings.GlobalizePath(recipeResourcePath);
        var recipeBytes = File.ReadAllBytes(recipePath);
        var recipe = JsonSerializer.Deserialize<SplatterAtlasRecipe>(recipeBytes, JsonOptions)
            ?? throw new InvalidDataException(
                $"Cannot deserialize asset recipe: {recipeResourcePath}");
        Validate(recipe, recipeResourcePath);

        var sourcePath = ProjectSettings.GlobalizePath(recipe.Source.Path);
        var sourceBytes = File.ReadAllBytes(sourcePath);
        var source = Image.LoadFromFile(sourcePath);
        if (source is null || source.IsEmpty())
        {
            throw new InvalidDataException($"Cannot load source atlas: {recipe.Source.Path}");
        }

        source.Convert(Image.Format.Rgba8);
        if (recipe.Source.ColumnCuts[^1] != source.GetWidth() ||
            recipe.Source.RowCuts[^1] != source.GetHeight())
        {
            throw new InvalidDataException(
                $"Splatter grid does not end at the source image bounds " +
                $"{source.GetWidth()}x{source.GetHeight()}: {recipeResourcePath}");
        }

        var output = Image.CreateEmpty(
            recipe.Source.Columns * recipe.Output.TileSize,
            recipe.Source.Rows * recipe.Output.TileSize,
            false,
            Image.Format.Rgba8);
        output.Fill(Colors.Transparent);
        var entries = new List<AssetAtlasEntry>();
        for (var row = 0; row < recipe.Source.Rows; row++)
        {
            for (var column = 0; column < recipe.Source.Columns; column++)
            {
                var sourceStart = new Vector2I(
                    recipe.Source.ColumnCuts[column],
                    recipe.Source.RowCuts[row]);
                var sourceEnd = new Vector2I(
                    recipe.Source.ColumnCuts[column + 1],
                    recipe.Source.RowCuts[row + 1]);
                var tile = source.GetRegion(new Rect2I(
                    sourceStart,
                    sourceEnd - sourceStart));
                var used = GetVisibleRect(tile, recipe.MinimumSourceAlpha);
                if (used.Size.X <= 0 || used.Size.Y <= 0)
                {
                    throw new InvalidDataException(
                        $"Splatter source cell {column},{row} is empty.");
                }

                tile = tile.GetRegion(used);
                var maximumSize = Math.Max(
                    1,
                    (int)Math.Round(recipe.Output.TileSize *
                        recipe.CellCoverage[(row * recipe.Source.Columns) + column]));
                var scale = Math.Min(
                    maximumSize / (float)tile.GetWidth(),
                    maximumSize / (float)tile.GetHeight());
                var targetSize = new Vector2I(
                    Math.Max(1, (int)Math.Round(tile.GetWidth() * scale)),
                    Math.Max(1, (int)Math.Round(tile.GetHeight() * scale)));
                tile.Resize(targetSize.X, targetSize.Y, Image.Interpolation.Lanczos);
                if (recipe.ColorMode == "intensity-tint")
                {
                    ApplySplatterTint(tile, recipe.Tint);
                }

                var destination = new Vector2I(
                    column * recipe.Output.TileSize,
                    row * recipe.Output.TileSize);
                var inset = (Vector2I.One * recipe.Output.TileSize - targetSize) / 2;
                output.BlitRect(
                    tile,
                    new Rect2I(Vector2I.Zero, targetSize),
                    destination + inset);
                entries.Add(new AssetAtlasEntry(
                    recipe.EntryMaterial,
                    $"stage-{row + 1}-variant-{column + 1}",
                    (row * recipe.Source.Columns) + column,
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
            recipe.Source.Columns,
            recipe.Source.Rows,
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

    private static void Validate(SplatterAtlasRecipe recipe, string recipePath)
    {
        if (recipe.SchemaVersion != 1 || recipe.Kind != "splatter-atlas" ||
            string.IsNullOrWhiteSpace(recipe.Id) ||
            string.IsNullOrWhiteSpace(recipe.EntryMaterial) ||
            string.IsNullOrWhiteSpace(recipe.Source.Path) ||
            recipe.Source.Columns <= 0 || recipe.Source.Rows <= 0 ||
            recipe.Output.TileSize < 32 ||
            recipe.MinimumSourceAlpha is < 0.001f or > 0.25f ||
            string.IsNullOrWhiteSpace(recipe.Output.AtlasPath) ||
            string.IsNullOrWhiteSpace(recipe.Output.ManifestPath) ||
            recipe.Source.ColumnCuts.Count != recipe.Source.Columns + 1 ||
            recipe.Source.RowCuts.Count != recipe.Source.Rows + 1 ||
            recipe.Source.ColumnCuts[0] != 0 ||
            recipe.Source.RowCuts[0] != 0 ||
            !IsStrictlyIncreasing(recipe.Source.ColumnCuts) ||
            !IsStrictlyIncreasing(recipe.Source.RowCuts) ||
            recipe.CellCoverage.Count != recipe.Source.Columns * recipe.Source.Rows ||
            recipe.CellCoverage.Any(coverage => coverage is < 0.2f or > 1f) ||
            recipe.ColorMode is not ("preserve" or "intensity-tint") ||
            recipe.Tint.Red is < 0f or > 1f ||
            recipe.Tint.Green is < 0f or > 1f ||
            recipe.Tint.Blue is < 0f or > 1f)
        {
            throw new InvalidDataException($"Incomplete splatter-atlas recipe: {recipePath}");
        }
    }

    private static bool IsStrictlyIncreasing(IReadOnlyList<int> values)
    {
        for (var index = 1; index < values.Count; index++)
        {
            if (values[index] <= values[index - 1])
            {
                return false;
            }
        }

        return true;
    }

    private static void ApplySplatterTint(Image image, SplatterTint tint)
    {
        for (var y = 0; y < image.GetHeight(); y++)
        {
            for (var x = 0; x < image.GetWidth(); x++)
            {
                var pixel = image.GetPixel(x, y);
                var intensity = Math.Max(pixel.R, Math.Max(pixel.G, pixel.B));
                image.SetPixel(x, y, new Color(
                    tint.Red * intensity,
                    tint.Green * intensity,
                    tint.Blue * intensity,
                    pixel.A));
            }
        }
    }

    private static Rect2I GetVisibleRect(Image image, float minimumAlpha)
    {
        var minimum = new Vector2I(image.GetWidth(), image.GetHeight());
        var maximum = new Vector2I(-1, -1);
        for (var y = 0; y < image.GetHeight(); y++)
        {
            for (var x = 0; x < image.GetWidth(); x++)
            {
                if (image.GetPixel(x, y).A < minimumAlpha)
                {
                    continue;
                }

                minimum.X = Math.Min(minimum.X, x);
                minimum.Y = Math.Min(minimum.Y, y);
                maximum.X = Math.Max(maximum.X, x);
                maximum.Y = Math.Max(maximum.Y, y);
            }
        }

        return maximum.X < minimum.X || maximum.Y < minimum.Y
            ? default
            : new Rect2I(minimum, maximum - minimum + Vector2I.One);
    }
}

internal sealed class SplatterAtlasRecipe
{
    public int SchemaVersion { get; set; }

    public string Kind { get; set; } = string.Empty;

    public string Id { get; set; } = string.Empty;

    public string EntryMaterial { get; set; } = string.Empty;

    public SplatterAtlasSource Source { get; set; } = new();

    public ConnectedAtlasOutput Output { get; set; } = new();

    public List<float> CellCoverage { get; set; } = [];

    public float MinimumSourceAlpha { get; set; }

    public string ColorMode { get; set; } = string.Empty;

    public SplatterTint Tint { get; set; } = new();
}

internal sealed class SplatterAtlasSource
{
    public string Path { get; set; } = string.Empty;

    public int Columns { get; set; }

    public int Rows { get; set; }

    public List<int> ColumnCuts { get; set; } = [];

    public List<int> RowCuts { get; set; } = [];
}

internal sealed class SplatterTint
{
    public float Red { get; set; }

    public float Green { get; set; }

    public float Blue { get; set; }
}
