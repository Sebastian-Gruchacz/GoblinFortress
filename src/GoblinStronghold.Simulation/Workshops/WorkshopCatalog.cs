using System.Collections.ObjectModel;
using System.Text.Json;
using System.Text.Json.Serialization;
using GoblinStronghold.Simulation.ContentPacks;
using GoblinStronghold.Simulation.Resources;

namespace GoblinStronghold.Simulation.Workshops;

public enum WorkshopKind : byte
{
    PrimitiveWorkshop = 1,
    Bloomery = 2,
    SmeltingFurnace = 3,
    CrucibleFurnace = 4,
    CookingFire = 5,
    FittedWorkshop = 6,
}

public sealed record WorkshopConstructionRequirement(
    int Quantity,
    IReadOnlyList<MaterialType> AllowedMaterialTypes,
    int MinimumStrength,
    int MinimumDurability);

public sealed record WorkshopDefinition(
    string Id,
    WorkshopKind Kind,
    int Level,
    int WorkSpeedPercent,
    int MaximumRecipeLevel,
    IReadOnlyList<WorkshopConstructionRequirement> ConstructionRequirements,
    IReadOnlyList<CraftingRecipeKind> AvailableRecipes,
    IReadOnlyList<MaterialType> ServedMaterialTypes,
    IReadOnlyList<string> ServedMaterialIds)
{
    public bool SupportsRecipe(CraftingRecipeKind recipe, int recipeLevel) =>
        recipeLevel <= MaximumRecipeLevel && AvailableRecipes.Contains(recipe);

    public bool SupportsProcessing(MaterialDefinition material) =>
        material.Processing is { } processing &&
        WorkshopCatalog.For(processing.Processor).Kind == Kind &&
        processing.MinimumProcessorLevel <= Level &&
        ServedMaterialTypes.Contains(material.MaterialType) &&
        (ServedMaterialIds.Count == 0 ||
         ServedMaterialIds.Contains(material.Id, StringComparer.OrdinalIgnoreCase));

    public int ScaleWorkTicks(int baseWorkTicks)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(baseWorkTicks);
        return checked((baseWorkTicks * 100 + WorkSpeedPercent - 1) / WorkSpeedPercent);
    }
}

public static class WorkshopCatalog
{
    private const string ContentPath = "content/workshops.json";
    private static readonly Lazy<CatalogState> State = new(Load);

    public static IReadOnlyList<WorkshopDefinition> All => State.Value.All;

    public static WorkshopDefinition Get(WorkshopKind kind) =>
        State.Value.ByKind.TryGetValue(kind, out var workshop)
            ? workshop
            : throw new KeyNotFoundException($"Unknown workshop kind '{kind}'.");

    public static WorkshopDefinition Get(string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        return State.Value.ById.TryGetValue(id, out var workshop)
            ? workshop
            : throw new KeyNotFoundException($"Unknown workshop id '{id}'.");
    }

    public static WorkshopDefinition For(MaterialProcessorKind processor) => processor switch
    {
        MaterialProcessorKind.Bloomery => Get(WorkshopKind.Bloomery),
        MaterialProcessorKind.SmeltingFurnace => Get(WorkshopKind.SmeltingFurnace),
        MaterialProcessorKind.CrucibleFurnace => Get(WorkshopKind.CrucibleFurnace),
        _ => throw new ArgumentOutOfRangeException(nameof(processor), processor, null),
    };

    private static CatalogState Load()
    {
        using var stream = CoreContentPack.Pack.OpenRead(ContentPath);
        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            Converters = { new JsonStringEnumConverter() },
        };
        var document = JsonSerializer.Deserialize<WorkshopCatalogDocument>(stream, options)
            ?? throw new InvalidOperationException("Workshop catalog is empty.");
        if (document.SchemaVersion != 1)
        {
            throw new InvalidOperationException(
                $"Unsupported workshop catalog schema {document.SchemaVersion}.");
        }

        var definitions = document.Workshops.Select(ToDefinition).ToArray();
        ValidateCompleteness(definitions);
        return new CatalogState(
            Array.AsReadOnly(definitions),
            new ReadOnlyDictionary<string, WorkshopDefinition>(definitions.ToDictionary(
                workshop => workshop.Id,
                StringComparer.OrdinalIgnoreCase)),
            new ReadOnlyDictionary<WorkshopKind, WorkshopDefinition>(definitions.ToDictionary(
                workshop => workshop.Kind)));
    }

    private static WorkshopDefinition ToDefinition(WorkshopDefinitionDto source)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(source.Id);
        if (!Enum.IsDefined(source.Kind) || source.Level < 1 ||
            source.WorkSpeedPercent is < 1 or > 1_000 ||
            source.MaximumRecipeLevel < 1 ||
            source.ConstructionRequirements.Count == 0 ||
            source.ConstructionRequirements.Any(requirement =>
                requirement.Quantity < 1 ||
                requirement.AllowedMaterialTypes.Count == 0 ||
                requirement.AllowedMaterialTypes.Any(type => !Enum.IsDefined(type)) ||
                requirement.AllowedMaterialTypes.Distinct().Count() !=
                    requirement.AllowedMaterialTypes.Count ||
                requirement.MinimumStrength is < 0 or > 100 ||
                requirement.MinimumDurability is < 0 or > 100) ||
            source.AvailableRecipes.Any(recipe => !Enum.IsDefined(recipe)) ||
            source.AvailableRecipes.Distinct().Count() != source.AvailableRecipes.Count ||
            source.ServedMaterialTypes.Count == 0 ||
            source.ServedMaterialTypes.Any(type => !Enum.IsDefined(type)) ||
            source.ServedMaterialTypes.Distinct().Count() != source.ServedMaterialTypes.Count ||
            source.ServedMaterialIds.Any(string.IsNullOrWhiteSpace) ||
            source.ServedMaterialIds.Distinct(StringComparer.OrdinalIgnoreCase).Count() !=
                source.ServedMaterialIds.Count)
        {
            throw new InvalidOperationException(
                $"Workshop '{source.Id}' has an invalid definition.");
        }

        foreach (var materialId in source.ServedMaterialIds)
        {
            MaterialCatalog.Get(materialId);
        }

        return new WorkshopDefinition(
            source.Id,
            source.Kind,
            source.Level,
            source.WorkSpeedPercent,
            source.MaximumRecipeLevel,
            Array.AsReadOnly(source.ConstructionRequirements.Select(requirement =>
                new WorkshopConstructionRequirement(
                    requirement.Quantity,
                    Array.AsReadOnly(requirement.AllowedMaterialTypes.ToArray()),
                    requirement.MinimumStrength,
                    requirement.MinimumDurability)).ToArray()),
            Array.AsReadOnly(source.AvailableRecipes.ToArray()),
            Array.AsReadOnly(source.ServedMaterialTypes.ToArray()),
            Array.AsReadOnly(source.ServedMaterialIds.ToArray()));
    }

    private static void ValidateCompleteness(WorkshopDefinition[] definitions)
    {
        if (definitions.Select(item => item.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count() !=
                definitions.Length ||
            definitions.Select(item => item.Kind).Distinct().Count() != definitions.Length ||
            definitions.Select(item => item.Kind).Order().SequenceEqual(
                Enum.GetValues<WorkshopKind>().Order()) is false)
        {
            throw new InvalidOperationException(
                "Workshop catalog must define every workshop kind exactly once.");
        }

        var configuredRecipes = definitions
            .SelectMany(item => item.AvailableRecipes)
            .ToArray();
        if (configuredRecipes.Distinct().Count() != configuredRecipes.Length ||
            !configuredRecipes.Order().SequenceEqual(
                Enum.GetValues<CraftingRecipeKind>().Order()))
        {
            throw new InvalidOperationException(
                "Workshops must assign every crafting recipe exactly once.");
        }

        foreach (var material in MaterialCatalog.All.Where(item => item.Processing is not null))
        {
            var processorKind = material.Processing!.Processor switch
            {
                MaterialProcessorKind.Bloomery => WorkshopKind.Bloomery,
                MaterialProcessorKind.SmeltingFurnace => WorkshopKind.SmeltingFurnace,
                MaterialProcessorKind.CrucibleFurnace => WorkshopKind.CrucibleFurnace,
                _ => throw new ArgumentOutOfRangeException(),
            };
            var workshop = definitions.Single(item => item.Kind == processorKind);
            if (material.Processing.MinimumProcessorLevel > workshop.Level ||
                !workshop.ServedMaterialTypes.Contains(material.MaterialType) ||
                workshop.ServedMaterialIds.Count > 0 &&
                !workshop.ServedMaterialIds.Contains(
                    material.Id,
                    StringComparer.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"No workshop can process material '{material.Id}'.");
            }
        }
    }

    private sealed record CatalogState(
        IReadOnlyList<WorkshopDefinition> All,
        IReadOnlyDictionary<string, WorkshopDefinition> ById,
        IReadOnlyDictionary<WorkshopKind, WorkshopDefinition> ByKind);

    private sealed class WorkshopCatalogDocument
    {
        public int SchemaVersion { get; init; }
        public List<WorkshopDefinitionDto> Workshops { get; init; } = [];
    }

    private sealed class WorkshopDefinitionDto
    {
        public string Id { get; init; } = string.Empty;
        public WorkshopKind Kind { get; init; }
        public int Level { get; init; }
        public int WorkSpeedPercent { get; init; }
        public int MaximumRecipeLevel { get; init; }
        public List<WorkshopConstructionRequirementDto> ConstructionRequirements { get; init; } = [];
        public List<CraftingRecipeKind> AvailableRecipes { get; init; } = [];
        public List<MaterialType> ServedMaterialTypes { get; init; } = [];
        public List<string> ServedMaterialIds { get; init; } = [];
    }

    private sealed class WorkshopConstructionRequirementDto
    {
        public int Quantity { get; init; }
        public List<MaterialType> AllowedMaterialTypes { get; init; } = [];
        public int MinimumStrength { get; init; }
        public int MinimumDurability { get; init; }
    }
}
