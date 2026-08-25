using Godot;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace GoblinStronghold.GodotClient;

internal static partial class AssetAtlasBaker
{
    private const string BakerVersion = "asset-baker-v2";
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };
    private static readonly UTF8Encoding Utf8WithoutBom = new(false);

    public static AssetBakeResult Bake(string recipeResourcePath)
    {
        using var document = JsonDocument.Parse(
            File.ReadAllBytes(ProjectSettings.GlobalizePath(recipeResourcePath)));
        var kind = document.RootElement.GetProperty("kind").GetString();
        return kind switch
        {
            "connected-atlas" => BakeConnectedAtlas(recipeResourcePath),
            "height-transition-atlas" => BakeHeightTransitionAtlas(recipeResourcePath),
            "cave-wall-atlas" => BakeCaveWallAtlas(recipeResourcePath),
            _ => throw new InvalidDataException(
                $"Unsupported asset recipe kind '{kind}' in {recipeResourcePath}."),
        };
    }

    public static AssetBakeResult BakeConnectedAtlas(string recipeResourcePath)
    {
        var recipePath = ProjectSettings.GlobalizePath(recipeResourcePath);
        var recipeBytes = File.ReadAllBytes(recipePath);
        var recipe = JsonSerializer.Deserialize<ConnectedAtlasRecipe>(recipeBytes, JsonOptions)
            ?? throw new InvalidDataException($"Cannot deserialize asset recipe: {recipeResourcePath}");
        Validate(recipe, recipeResourcePath);

        var sourcePath = ProjectSettings.GlobalizePath(recipe.Source.Path);
        var sourceBytes = File.ReadAllBytes(sourcePath);
        var source = Image.LoadFromFile(sourcePath);
        if (source is null || source.IsEmpty())
        {
            throw new InvalidDataException($"Cannot load source atlas: {recipe.Source.Path}");
        }

        source.Convert(Image.Format.Rgba8);
        if (source.GetWidth() % recipe.Source.Columns != 0 ||
            source.GetHeight() % recipe.Source.Rows != 0)
        {
            throw new InvalidDataException(
                $"Source atlas dimensions {source.GetWidth()}x{source.GetHeight()} are not divisible by " +
                $"{recipe.Source.Columns}x{recipe.Source.Rows}.");
        }

        var outputColumns = recipe.Topologies.Max(item => item.OutputIndex) + 1;
        var output = Image.CreateEmpty(
            outputColumns * recipe.Output.TileSize,
            recipe.Materials.Count * recipe.Output.TileSize,
            false,
            Image.Format.Rgba8);
        output.Fill(Colors.Transparent);
        var sourceCellWidth = source.GetWidth() / recipe.Source.Columns;
        var sourceCellHeight = source.GetHeight() / recipe.Source.Rows;
        var entries = new List<AssetAtlasEntry>();
        for (var materialIndex = 0; materialIndex < recipe.Materials.Count; materialIndex++)
        {
            var material = recipe.Materials[materialIndex];
            foreach (var topology in recipe.Topologies.OrderBy(item => item.OutputIndex))
            {
                var sourceColumn = topology.SourceCell % recipe.Source.Columns;
                var sourceRow = topology.SourceCell / recipe.Source.Columns;
                var tile = source.GetRegion(new Rect2I(
                    sourceColumn * sourceCellWidth,
                    sourceRow * sourceCellHeight,
                    sourceCellWidth,
                    sourceCellHeight));
                Rotate(tile, topology.QuarterTurns);
                tile.Resize(
                    recipe.Output.TileSize,
                    recipe.Output.TileSize,
                    Image.Interpolation.Lanczos);
                ApplyMaterial(tile, material);
                var destination = new Vector2I(
                    topology.OutputIndex * recipe.Output.TileSize,
                    materialIndex * recipe.Output.TileSize);
                output.BlitRect(
                    tile,
                    new Rect2I(Vector2I.Zero, new Vector2I(recipe.Output.TileSize, recipe.Output.TileSize)),
                    destination);
                entries.Add(new AssetAtlasEntry(
                    material.Id,
                    topology.Id,
                    topology.OutputIndex,
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
            outputColumns,
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

    private static void Validate(ConnectedAtlasRecipe recipe, string recipePath)
    {
        if (recipe.SchemaVersion != 1 || recipe.Kind != "connected-atlas" ||
            string.IsNullOrWhiteSpace(recipe.Id) ||
            string.IsNullOrWhiteSpace(recipe.Source.Path) ||
            recipe.Source.Columns <= 0 || recipe.Source.Rows <= 0 ||
            recipe.Output.TileSize <= 0 ||
            string.IsNullOrWhiteSpace(recipe.Output.AtlasPath) ||
            string.IsNullOrWhiteSpace(recipe.Output.ManifestPath) ||
            recipe.Materials.Count == 0 || recipe.Topologies.Count == 0)
        {
            throw new InvalidDataException($"Incomplete connected-atlas recipe: {recipePath}");
        }

        if (recipe.Materials.Select(item => item.Id).Distinct(StringComparer.Ordinal).Count() !=
            recipe.Materials.Count)
        {
            throw new InvalidDataException($"Duplicate material id in recipe: {recipePath}");
        }

        if (recipe.Topologies.Select(item => item.Id).Distinct(StringComparer.Ordinal).Count() !=
                recipe.Topologies.Count ||
            recipe.Topologies.Select(item => item.OutputIndex).Distinct().Count() !=
                recipe.Topologies.Count)
        {
            throw new InvalidDataException($"Duplicate topology id or output index in recipe: {recipePath}");
        }

        var sourceCellCount = checked(recipe.Source.Columns * recipe.Source.Rows);
        if (recipe.Topologies.Any(item => item.SourceCell < 0 || item.SourceCell >= sourceCellCount ||
            item.OutputIndex < 0))
        {
            throw new InvalidDataException($"Topology references an invalid cell in recipe: {recipePath}");
        }

        foreach (var material in recipe.Materials)
        {
            _ = new Color(material.Tint);
            if (material.TintStrength is < 0f or > 1f ||
                material.Saturation is < 0f or > 2f ||
                material.Brightness is < 0.1f or > 2f)
            {
                throw new InvalidDataException($"Invalid material transform '{material.Id}' in {recipePath}");
            }
        }
    }

    private static void Rotate(Image image, int quarterTurns)
    {
        var turns = ((quarterTurns % 4) + 4) % 4;
        for (var turn = 0; turn < turns; turn++)
        {
            image.Rotate90(ClockDirection.Clockwise);
        }
    }

    private static void ApplyMaterial(Image image, ConnectedAtlasMaterial material)
    {
        var tint = new Color(material.Tint);
        for (var y = 0; y < image.GetHeight(); y++)
        {
            for (var x = 0; x < image.GetWidth(); x++)
            {
                var pixel = image.GetPixel(x, y);
                if (pixel.A <= 0.001f)
                {
                    continue;
                }

                var luminance = (pixel.R * 0.2126f) + (pixel.G * 0.7152f) + (pixel.B * 0.0722f);
                var saturated = new Color(
                    luminance + ((pixel.R - luminance) * material.Saturation),
                    luminance + ((pixel.G - luminance) * material.Saturation),
                    luminance + ((pixel.B - luminance) * material.Saturation),
                    pixel.A);
                var tinted = new Color(
                    luminance * tint.R * 1.65f,
                    luminance * tint.G * 1.65f,
                    luminance * tint.B * 1.65f,
                    pixel.A);
                var result = saturated.Lerp(tinted, material.TintStrength);
                image.SetPixel(x, y, new Color(
                    Math.Clamp(result.R * material.Brightness, 0f, 1f),
                    Math.Clamp(result.G * material.Brightness, 0f, 1f),
                    Math.Clamp(result.B * material.Brightness, 0f, 1f),
                    pixel.A));
            }
        }
    }

    private static string ComputeContentHash(byte[] recipeBytes, byte[] sourceBytes)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(Encoding.UTF8.GetBytes(BakerVersion));
        hash.AppendData(recipeBytes);
        hash.AppendData(sourceBytes);
        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }
}

internal sealed class ConnectedAtlasRecipe
{
    public int SchemaVersion { get; set; }

    public string Kind { get; set; } = string.Empty;

    public string Id { get; set; } = string.Empty;

    public ConnectedAtlasSource Source { get; set; } = new();

    public ConnectedAtlasOutput Output { get; set; } = new();

    public List<ConnectedAtlasMaterial> Materials { get; set; } = [];

    public List<ConnectedAtlasTopology> Topologies { get; set; } = [];
}

internal sealed class ConnectedAtlasSource
{
    public string Path { get; set; } = string.Empty;

    public int Columns { get; set; }

    public int Rows { get; set; }
}

internal sealed class ConnectedAtlasOutput
{
    public string AtlasPath { get; set; } = string.Empty;

    public string ManifestPath { get; set; } = string.Empty;

    public int TileSize { get; set; }
}

internal sealed class ConnectedAtlasMaterial
{
    public string Id { get; set; } = string.Empty;

    public string Tint { get; set; } = "ffffff";

    public float TintStrength { get; set; }

    public float Saturation { get; set; } = 1f;

    public float Brightness { get; set; } = 1f;
}

internal sealed class ConnectedAtlasTopology
{
    public string Id { get; set; } = string.Empty;

    public int OutputIndex { get; set; }

    public int SourceCell { get; set; }

    public int QuarterTurns { get; set; }
}

internal sealed record AssetAtlasEntry(
    string Material,
    string Topology,
    int Index,
    int X,
    int Y,
    int Width,
    int Height);

internal sealed record AssetAtlasManifest(
    int SchemaVersion,
    string Recipe,
    string ContentHash,
    int TileSize,
    int Columns,
    int Rows,
    IReadOnlyList<AssetAtlasEntry> Entries);

internal sealed record AssetBakeResult(
    string Recipe,
    string AtlasPath,
    string ManifestPath,
    string ContentHash,
    int EntryCount);
