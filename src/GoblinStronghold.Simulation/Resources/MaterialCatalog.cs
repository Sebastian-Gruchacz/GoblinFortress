using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization;
using GoblinStronghold.Simulation.ContentPacks;

namespace GoblinStronghold.Simulation.Resources;

public enum MaterialType : byte
{
    Wood = 1,
    Stone = 2,
    Fuel = 3,
    Ore = 4,
    Metal = 5,
    Gem = 6,
    PlantFiber = 7,
    Bone = 8,
    Hide = 9,
    Venom = 10,
    Silk = 11,
    Chitin = 12,
    Soil = 13,
    Sand = 14,
}

public enum MaterialAcquisitionStrategy : byte
{
    Felling = 1,
    Harvesting = 2,
    Quarrying = 3,
    Mining = 4,
    Butchering = 5,
    Processing = 6,
    Digging = 7,
}

public enum MaterialToolKind : byte
{
    None = 0,
    Axe = 1,
    Pickaxe = 2,
    Knife = 3,
    Shovel = 4,
}

public enum MaterialProcessingStrategy : byte
{
    Smelting = 1,
}

public enum MaterialProcessorKind : byte
{
    Bloomery = 1,
    SmeltingFurnace = 2,
    CrucibleFurnace = 3,
}

public enum MaterialUse : byte
{
    Construction = 1,
    Furniture = 2,
    ToolHead = 3,
    ToolHandle = 4,
    Weapon = 5,
    Armor = 6,
    Container = 7,
    Decoration = 8,
    Sculpture = 9,
    Fuel = 10,
}

public sealed record MaterialPalette(
    string Edge,
    IReadOnlyList<string> KeyColors);

public sealed record MaterialOccurrence(
    IReadOnlyList<string> Climates,
    int MinimumDepthBelowSurface,
    int? MaximumDepthBelowSurface);

public sealed record MaterialAcquisition(
    MaterialAcquisitionStrategy Strategy,
    MaterialToolKind RequiredTool,
    int MinimumToolLevel,
    int MinimumSkillLevel,
    int WorkMultiplier);

public sealed record MaterialIngredient(string MaterialId, int Quantity);

public sealed record MaterialProcessing(
    MaterialProcessingStrategy Strategy,
    MaterialProcessorKind Processor,
    int MinimumProcessorLevel,
    IReadOnlyList<MaterialIngredient> Inputs,
    int OutputQuantity);

public sealed record MaterialDefinition(
    string Id,
    ResourceKind ResourceKind,
    ResourceVariant? Variant,
    MaterialType MaterialType,
    double UnitWeight,
    int Strength,
    int Hardness,
    int Durability,
    int Flexibility,
    int Value,
    int AcquisitionDifficulty,
    MaterialOccurrence? Occurrence,
    MaterialAcquisition Acquisition,
    MaterialProcessing? Processing,
    IReadOnlyList<MaterialUse> Uses,
    MaterialPalette Palette)
{
    public ContentId StableId => ContentId.Parse(Id);
}

public static class MaterialCatalog
{
    private const string ContentPath = "content/materials.json";
    private static readonly Lazy<CatalogState> State = new(Load);

    public static IReadOnlyList<MaterialDefinition> All => State.Value.All;

    public static MaterialDefinition Get(string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        var lookupId = ContentId.Parse(id);
        return State.Value.ByStableId.TryGetValue(lookupId.Value, out var material)
            ? material
            : throw new KeyNotFoundException($"Unknown material id '{id}'.");
    }

    public static MaterialDefinition Get(ResourceVariant variant) =>
        State.Value.ByVariant.TryGetValue(variant, out var material)
            ? material
            : throw new KeyNotFoundException($"No material defines variant '{variant}'.");

    public static MaterialDefinition Get(
        ResourceKind resource,
        ResourceVariant variant = ResourceVariant.None) =>
        State.Value.ByResourceIdentity.TryGetValue((resource, variant), out var material)
            ? material
            : throw new KeyNotFoundException(
                $"No material defines resource identity '{resource}/{variant}'.");

    public static bool TryGet(
        ResourceVariant variant,
        [NotNullWhen(true)] out MaterialDefinition? material) =>
        State.Value.ByVariant.TryGetValue(variant, out material);

    public static bool TryGet(
        ResourceKind resource,
        ResourceVariant variant,
        [NotNullWhen(true)] out MaterialDefinition? material) =>
        State.Value.ByResourceIdentity.TryGetValue((resource, variant), out material);

    public static IReadOnlyList<MaterialDefinition> Supporting(MaterialUse use) =>
        Array.AsReadOnly(All.Where(material => material.Uses.Contains(use)).ToArray());

    private static CatalogState Load()
    {
        using var stream = CoreContentPack.Pack.OpenRead(ContentPath);
        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            Converters = { new JsonStringEnumConverter() },
        };
        var document = JsonSerializer.Deserialize<MaterialCatalogDocument>(stream, options)
            ?? throw new InvalidOperationException("Material catalog is empty.");
        if (document.SchemaVersion != 2)
        {
            throw new InvalidOperationException(
                $"Unsupported material catalog schema {document.SchemaVersion}.");
        }

        var definitions = document.Materials.Select(ToDefinition).ToArray();
        ValidateUniqueIdentities(definitions);
        ValidateProcessingReferences(definitions);

        return new CatalogState(
            Array.AsReadOnly(definitions),
            new ReadOnlyDictionary<string, MaterialDefinition>(definitions.ToDictionary(
                item => item.StableId.Value,
                StringComparer.OrdinalIgnoreCase)),
            new ReadOnlyDictionary<ResourceVariant, MaterialDefinition>(
                definitions
                    .Where(item => item.Variant is not null)
                    .ToDictionary(item => item.Variant!.Value)),
            new ReadOnlyDictionary<(ResourceKind, ResourceVariant), MaterialDefinition>(
                definitions.ToDictionary(
                    item => (item.ResourceKind, item.Variant ?? ResourceVariant.None))));
    }

    private static void ValidateUniqueIdentities(MaterialDefinition[] definitions)
    {
        var duplicateId = definitions
            .GroupBy(item => item.Id, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicateId is not null)
        {
            throw new InvalidOperationException(
                $"Material id '{duplicateId.Key}' is duplicated.");
        }
        var duplicateVariant = definitions
            .Where(item => item.Variant is not null)
            .GroupBy(item => item.Variant!.Value)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicateVariant is not null)
        {
            throw new InvalidOperationException(
                $"Material variant '{duplicateVariant.Key}' is duplicated.");
        }
        var duplicateResource = definitions
            .GroupBy(item => (item.ResourceKind, item.Variant ?? ResourceVariant.None))
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicateResource is not null)
        {
            throw new InvalidOperationException(
                $"Material resource identity '{duplicateResource.Key}' is duplicated.");
        }
    }

    private static void ValidateProcessingReferences(MaterialDefinition[] definitions)
    {
        var ids = definitions.Select(item => item.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var material in definitions.Where(item => item.Processing is not null))
        {
            foreach (var input in material.Processing!.Inputs)
            {
                if (!ids.Contains(input.MaterialId) ||
                    string.Equals(input.MaterialId, material.Id,
                        StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException(
                        $"Material '{material.Id}' has invalid processing input " +
                        $"'{input.MaterialId}'.");
                }
            }
        }
    }

    private static MaterialDefinition ToDefinition(MaterialDefinitionDto source)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(source.Id);
        if (source.Variant == ResourceVariant.None ||
            !ResourceMatchesType(source.ResourceKind, source.MaterialType))
        {
            throw new InvalidOperationException(
                $"Material '{source.Id}' has an invalid resource/type identity.");
        }
        if (source.UnitWeight <= 0)
        {
            throw new InvalidOperationException(
                $"Material '{source.Id}' must have positive unitWeight.");
        }
        ValidatePercentage(source.Id, "strength", source.Strength);
        ValidatePercentage(source.Id, "hardness", source.Hardness);
        ValidatePercentage(source.Id, "durability", source.Durability);
        ValidatePercentage(source.Id, "flexibility", source.Flexibility);
        ValidatePercentage(source.Id, "value", source.Value);
        ValidatePercentage(
            source.Id, "acquisitionDifficulty", source.AcquisitionDifficulty);

        var occurrence = ToOccurrence(source.Id, source.Occurrence);
        var acquisition = ToAcquisition(source.Id, source.Acquisition, occurrence);
        var processing = ToProcessing(source.Id, source.Processing, acquisition.Strategy);
        if (source.Uses is null ||
            source.Uses.Any(use => !Enum.IsDefined(use)) ||
            source.Uses.Distinct().Count() != source.Uses.Count)
        {
            throw new InvalidOperationException(
                $"Material '{source.Id}' must define only unique supported uses.");
        }
        if (source.Palette is null || source.Palette.KeyColors is null ||
            source.Palette.KeyColors.Count != 3)
        {
            throw new InvalidOperationException(
                $"Material '{source.Id}' must define exactly three key colors.");
        }
        ValidateColor(source.Id, source.Palette.Edge);
        foreach (var color in source.Palette.KeyColors)
        {
            ValidateColor(source.Id, color);
        }

        return new MaterialDefinition(
            source.Id,
            source.ResourceKind,
            source.Variant,
            source.MaterialType,
            source.UnitWeight,
            source.Strength,
            source.Hardness,
            source.Durability,
            source.Flexibility,
            source.Value,
            source.AcquisitionDifficulty,
            occurrence,
            acquisition,
            processing,
            Array.AsReadOnly(source.Uses.ToArray()),
            new MaterialPalette(
                source.Palette.Edge,
                Array.AsReadOnly(source.Palette.KeyColors.ToArray())));
    }

    private static MaterialOccurrence? ToOccurrence(
        string id,
        MaterialOccurrenceDto? source)
    {
        if (source is null)
        {
            return null;
        }
        if (source.Climates is null || source.Climates.Count == 0 ||
            source.Climates.Any(string.IsNullOrWhiteSpace) ||
            source.MinimumDepthBelowSurface < 0 ||
            source.MaximumDepthBelowSurface < source.MinimumDepthBelowSurface)
        {
            throw new InvalidOperationException(
                $"Material '{id}' has an invalid natural occurrence.");
        }
        return new MaterialOccurrence(
            Array.AsReadOnly(source.Climates.ToArray()),
            source.MinimumDepthBelowSurface,
            source.MaximumDepthBelowSurface);
    }

    private static MaterialAcquisition ToAcquisition(
        string id,
        MaterialAcquisitionDto? source,
        MaterialOccurrence? occurrence)
    {
        if (source is null || !Enum.IsDefined(source.Strategy) ||
            !Enum.IsDefined(source.RequiredTool) || source.WorkMultiplier < 1 ||
            source.MinimumSkillLevel < 0 || source.MinimumSkillLevel > 100 ||
            (source.RequiredTool == MaterialToolKind.None
                ? source.MinimumToolLevel != 0
                : source.MinimumToolLevel < 1) ||
            (source.Strategy == MaterialAcquisitionStrategy.Processing
                ? occurrence is not null
                : source.Strategy != MaterialAcquisitionStrategy.Butchering &&
                    occurrence is null))
        {
            throw new InvalidOperationException(
                $"Material '{id}' has an invalid acquisition strategy.");
        }
        return new MaterialAcquisition(
            source.Strategy,
            source.RequiredTool,
            source.MinimumToolLevel,
            source.MinimumSkillLevel,
            source.WorkMultiplier);
    }

    private static MaterialProcessing? ToProcessing(
        string id,
        MaterialProcessingDto? source,
        MaterialAcquisitionStrategy acquisition)
    {
        if ((acquisition == MaterialAcquisitionStrategy.Processing) != (source is not null))
        {
            throw new InvalidOperationException(
                $"Material '{id}' must pair processing acquisition with a recipe.");
        }
        if (source is null)
        {
            return null;
        }
        if (!Enum.IsDefined(source.Strategy) || !Enum.IsDefined(source.Processor) ||
            source.MinimumProcessorLevel < 1 || source.OutputQuantity < 1 ||
            source.Inputs is null || source.Inputs.Count == 0 ||
            source.Inputs.Any(input =>
                string.IsNullOrWhiteSpace(input.MaterialId) || input.Quantity < 1) ||
            source.Inputs.Select(input => input.MaterialId)
                .Distinct(StringComparer.OrdinalIgnoreCase).Count() != source.Inputs.Count)
        {
            throw new InvalidOperationException(
                $"Material '{id}' has an invalid processing recipe.");
        }
        return new MaterialProcessing(
            source.Strategy,
            source.Processor,
            source.MinimumProcessorLevel,
            Array.AsReadOnly(source.Inputs
                .Select(input => new MaterialIngredient(input.MaterialId, input.Quantity))
                .ToArray()),
            source.OutputQuantity);
    }

    private static void ValidatePercentage(string id, string name, int value)
    {
        if (value is < 0 or > 100)
        {
            throw new InvalidOperationException(
                $"Material '{id}' has {name} outside 0..100.");
        }
    }

    private static bool ResourceMatchesType(
        ResourceKind resource,
        MaterialType type) => type switch
    {
        MaterialType.Wood => resource == ResourceKind.Wood,
        MaterialType.Stone => resource == ResourceKind.Stone,
        MaterialType.Fuel => resource == ResourceKind.Coal,
        MaterialType.Ore => resource == ResourceKind.Ore,
        MaterialType.Metal or MaterialType.Gem => resource == ResourceKind.Materials,
        MaterialType.PlantFiber => resource == ResourceKind.Reeds,
        MaterialType.Bone => resource == ResourceKind.Bone,
        MaterialType.Hide => resource == ResourceKind.Hide,
        MaterialType.Soil => resource == ResourceKind.Earth,
        MaterialType.Sand => resource == ResourceKind.Sand,
        MaterialType.Venom or MaterialType.Silk or MaterialType.Chitin =>
            resource == ResourceKind.Materials,
        _ => false,
    };

    private static void ValidateColor(string id, string color)
    {
        if (color is null || color.Length != 7 || color[0] != '#' ||
            color.AsSpan(1).ContainsAnyExcept(
                "0123456789abcdefABCDEF".AsSpan()))
        {
            throw new InvalidOperationException(
                $"Material '{id}' has invalid color '{color}'. Use #RRGGBB.");
        }
    }

    private sealed record CatalogState(
        IReadOnlyList<MaterialDefinition> All,
        IReadOnlyDictionary<string, MaterialDefinition> ByStableId,
        IReadOnlyDictionary<ResourceVariant, MaterialDefinition> ByVariant,
        IReadOnlyDictionary<(ResourceKind, ResourceVariant), MaterialDefinition>
            ByResourceIdentity);

    private sealed class MaterialCatalogDocument
    {
        public int SchemaVersion { get; init; }
        public List<MaterialDefinitionDto> Materials { get; init; } = [];
    }

    private sealed class MaterialDefinitionDto
    {
        public string Id { get; init; } = string.Empty;
        public ResourceKind ResourceKind { get; init; }
        public ResourceVariant? Variant { get; init; }
        public MaterialType MaterialType { get; init; }
        public double UnitWeight { get; init; }
        public int Strength { get; init; }
        public int Hardness { get; init; }
        public int Durability { get; init; }
        public int Flexibility { get; init; }
        public int Value { get; init; }
        public int AcquisitionDifficulty { get; init; }
        public MaterialOccurrenceDto? Occurrence { get; init; }
        public MaterialAcquisitionDto? Acquisition { get; init; }
        public MaterialProcessingDto? Processing { get; init; }
        public List<MaterialUse> Uses { get; init; } = [];
        public MaterialPaletteDto? Palette { get; init; }
    }

    private sealed class MaterialOccurrenceDto
    {
        public List<string> Climates { get; init; } = [];
        public int MinimumDepthBelowSurface { get; init; }
        public int? MaximumDepthBelowSurface { get; init; }
    }

    private sealed class MaterialAcquisitionDto
    {
        public MaterialAcquisitionStrategy Strategy { get; init; }
        public MaterialToolKind RequiredTool { get; init; }
        public int MinimumToolLevel { get; init; }
        public int MinimumSkillLevel { get; init; }
        public int WorkMultiplier { get; init; }
    }

    private sealed class MaterialProcessingDto
    {
        public MaterialProcessingStrategy Strategy { get; init; }
        public MaterialProcessorKind Processor { get; init; }
        public int MinimumProcessorLevel { get; init; }
        public List<MaterialIngredientDto> Inputs { get; init; } = [];
        public int OutputQuantity { get; init; }
    }

    private sealed class MaterialIngredientDto
    {
        public string MaterialId { get; init; } = string.Empty;
        public int Quantity { get; init; }
    }

    private sealed class MaterialPaletteDto
    {
        public string Edge { get; init; } = string.Empty;
        public List<string> KeyColors { get; init; } = [];
    }
}
