using System.Collections.ObjectModel;
using System.Text.Json;
using System.Text.Json.Serialization;
using GoblinStronghold.Simulation.ContentPacks;
using GoblinStronghold.Simulation.Resources;
using GoblinStronghold.Simulation.Workshops;

namespace GoblinStronghold.Simulation;

public sealed record CraftingMaterialRequirement(
    ResourceKind Resource,
    ResourceVariant Variant,
    int Quantity,
    FoodKind FoodKind = FoodKind.None)
{
    public bool Matches(ResourceKind resource, FoodKind foodKind, ResourceVariant variant) =>
        Resource == resource &&
        (FoodKind == FoodKind.None || FoodKind == foodKind) &&
        (Variant == ResourceVariant.None || Variant == variant);
}

public sealed record CraftingOutputDefinition(
    ResourceKind Resource,
    ResourceVariant Variant,
    int Quantity,
    FoodKind FoodKind = FoodKind.None);

public sealed record CraftingRecipeDefinition(
    string Id,
    CraftingRecipeKind Kind,
    WorkshopKind Workshop,
    int Level,
    int BaseWorkTicks,
    IReadOnlyList<CraftingMaterialRequirement> Materials,
    CraftingOutputDefinition Output)
{
    public ContentId StableId => ContentId.Parse(Id);
}

public static class CraftingRecipeCatalog
{
    private const string ContentPath = "content/crafting-recipes.json";
    private static readonly Lazy<CatalogState> State = new(Load);

    public static IReadOnlyList<CraftingRecipeDefinition> All => State.Value.All;

    public static CraftingRecipeDefinition Get(CraftingRecipeKind kind) =>
        State.Value.ByKind.TryGetValue(kind, out var recipe)
            ? recipe
            : throw new KeyNotFoundException($"Unknown crafting recipe '{kind}'.");

    public static CraftingRecipeDefinition Get(string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        var lookupId = ContentId.Parse(id);
        return State.Value.ByStableId.TryGetValue(lookupId.Value, out var recipe)
            ? recipe
            : throw new KeyNotFoundException($"Unknown crafting recipe id '{id}'.");
    }

    public static CraftingMaterialRequirement? FindMaterial(
        CraftingRecipeKind recipe,
        ResourceKind resource,
        FoodKind foodKind,
        ResourceVariant variant) => Get(recipe).Materials
        .SingleOrDefault(material => material.Matches(resource, foodKind, variant));

    public static CraftingMaterialRequirement? FindMaterial(
        CraftingRecipeKind recipe,
        ResourceKind resource,
        ResourceVariant variant) => FindMaterial(
        recipe,
        resource,
        FoodKind.None,
        variant);

    public static int GetRecipeLevel(CraftingRecipeKind recipe) => Get(recipe).Level;

    public static int GetWorkTicks(CraftingRecipeKind recipe)
    {
        var definition = Get(recipe);
        return WorkshopCatalog.Get(definition.Workshop).ScaleWorkTicks(
            definition.BaseWorkTicks);
    }

    private static CatalogState Load()
    {
        using var stream = CoreContentPack.Pack.OpenRead(ContentPath);
        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            Converters = { new JsonStringEnumConverter() },
        };
        var document = JsonSerializer.Deserialize<CraftingRecipeCatalogDocument>(stream, options)
            ?? throw new InvalidOperationException("Crafting recipe catalog is empty.");
        if (document.SchemaVersion != 1)
        {
            throw new InvalidOperationException(
                $"Unsupported crafting recipe catalog schema {document.SchemaVersion}.");
        }

        var definitions = document.Recipes.Select(ToDefinition).ToArray();
        ValidateCompleteness(definitions);
        return new CatalogState(
            Array.AsReadOnly(definitions),
            new ReadOnlyDictionary<string, CraftingRecipeDefinition>(definitions.ToDictionary(
                recipe => recipe.StableId.Value,
                StringComparer.OrdinalIgnoreCase)),
            new ReadOnlyDictionary<CraftingRecipeKind, CraftingRecipeDefinition>(
                definitions.ToDictionary(recipe => recipe.Kind)));
    }

    private static CraftingRecipeDefinition ToDefinition(CraftingRecipeDefinitionDto source)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(source.Id);
        if (!Enum.IsDefined(source.Kind) || !Enum.IsDefined(source.Workshop) ||
            source.Level < 1 || source.BaseWorkTicks < 1 || source.Materials.Count == 0 ||
            source.Materials.Any(material =>
                !Enum.IsDefined(material.Resource) || material.Resource == ResourceKind.Any ||
                !Enum.IsDefined(material.Variant) ||
                !Enum.IsDefined(material.FoodKind) ||
                (material.Resource == ResourceKind.Food) !=
                    (material.FoodKind != FoodKind.None) ||
                material.FoodKind != FoodKind.None &&
                    material.Variant != ResourceVariant.None ||
                material.Quantity < 1) ||
            source.Materials.Select(material => (
                    material.Resource,
                    material.FoodKind,
                    material.Variant))
                .Distinct().Count() !=
                source.Materials.Count ||
            source.Materials.GroupBy(material => (
                    material.Resource,
                    material.FoodKind)).Any(group =>
                group.Count() > 1 && group.Any(material =>
                    material.Variant == ResourceVariant.None)) ||
            !Enum.IsDefined(source.Output.Resource) ||
            source.Output.Resource == ResourceKind.Any ||
            !Enum.IsDefined(source.Output.Variant) ||
            !Enum.IsDefined(source.Output.FoodKind) ||
            (source.Output.Resource == ResourceKind.Food
                ? source.Output.FoodKind == FoodKind.None ||
                  source.Output.Variant != ResourceVariant.None
                : source.Output.FoodKind != FoodKind.None ||
                  source.Output.Variant == ResourceVariant.None) ||
            source.Output.Quantity < 1)
        {
            throw new InvalidOperationException(
                $"Crafting recipe '{source.Id}' has an invalid definition.");
        }

        return new CraftingRecipeDefinition(
            source.Id,
            source.Kind,
            source.Workshop,
            source.Level,
            source.BaseWorkTicks,
            Array.AsReadOnly(source.Materials.Select(material =>
                new CraftingMaterialRequirement(
                    material.Resource,
                    material.Variant,
                    material.Quantity,
                    material.FoodKind)).ToArray()),
            new CraftingOutputDefinition(
                source.Output.Resource,
                source.Output.Variant,
                source.Output.Quantity,
                source.Output.FoodKind));
    }

    private static void ValidateCompleteness(CraftingRecipeDefinition[] definitions)
    {
        if (definitions.Select(item => item.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count() !=
                definitions.Length ||
            definitions.Select(item => item.Kind).Distinct().Count() != definitions.Length ||
            !definitions.Select(item => item.Kind).Order().SequenceEqual(
                Enum.GetValues<CraftingRecipeKind>().Order()))
        {
            throw new InvalidOperationException(
                "Crafting recipe catalog must define every recipe kind exactly once.");
        }

        foreach (var recipe in definitions)
        {
            var workshop = WorkshopCatalog.Get(recipe.Workshop);
            if (!workshop.SupportsRecipe(recipe.Kind, recipe.Level))
            {
                throw new InvalidOperationException(
                    $"Workshop '{workshop.Id}' does not support recipe '{recipe.Id}'.");
            }
        }

        foreach (var material in MaterialCatalog.All.Where(item => item.Processing is not null))
        {
            var processing = material.Processing!;
            var recipe = definitions.SingleOrDefault(item =>
                item.Output.Resource == material.ResourceKind &&
                item.Output.Variant == material.Variant);
            var expectedInputs = processing.Inputs.Select(input =>
            {
                var inputMaterial = MaterialCatalog.Get(input.MaterialId);
                return new CraftingMaterialRequirement(
                    inputMaterial.ResourceKind,
                    inputMaterial.Variant ?? ResourceVariant.None,
                    input.Quantity);
            }).ToArray();
            if (recipe is null ||
                recipe.Workshop != WorkshopCatalog.For(processing.Processor).Kind ||
                recipe.Level < processing.MinimumProcessorLevel ||
                recipe.Output.Quantity != processing.OutputQuantity ||
                !recipe.Materials.OrderBy(item => item.Resource).ThenBy(item => item.Variant)
                    .SequenceEqual(expectedInputs.OrderBy(item => item.Resource)
                        .ThenBy(item => item.Variant)))
            {
                throw new InvalidOperationException(
                    $"Crafting recipes do not match processing contract '{material.Id}'.");
            }
        }
    }

    private sealed record CatalogState(
        IReadOnlyList<CraftingRecipeDefinition> All,
        IReadOnlyDictionary<string, CraftingRecipeDefinition> ByStableId,
        IReadOnlyDictionary<CraftingRecipeKind, CraftingRecipeDefinition> ByKind);

    private sealed class CraftingRecipeCatalogDocument
    {
        public int SchemaVersion { get; init; }
        public List<CraftingRecipeDefinitionDto> Recipes { get; init; } = [];
    }

    private sealed class CraftingRecipeDefinitionDto
    {
        public string Id { get; init; } = string.Empty;
        public CraftingRecipeKind Kind { get; init; }
        public WorkshopKind Workshop { get; init; }
        public int Level { get; init; }
        public int BaseWorkTicks { get; init; }
        public List<CraftingMaterialRequirementDto> Materials { get; init; } = [];
        public CraftingOutputDefinitionDto Output { get; init; } = new();
    }

    private sealed class CraftingMaterialRequirementDto
    {
        public ResourceKind Resource { get; init; }
        public ResourceVariant Variant { get; init; }
        public FoodKind FoodKind { get; init; }
        public int Quantity { get; init; }
    }

    private sealed class CraftingOutputDefinitionDto
    {
        public ResourceKind Resource { get; init; }
        public ResourceVariant Variant { get; init; }
        public FoodKind FoodKind { get; init; }
        public int Quantity { get; init; }
    }
}
