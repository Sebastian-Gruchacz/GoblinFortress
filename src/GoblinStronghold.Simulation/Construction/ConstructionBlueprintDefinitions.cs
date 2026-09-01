using System.Collections.ObjectModel;
using System.Text.Json;
using System.Text.Json.Serialization;
using GoblinStronghold.Simulation.ContentPacks;
using GoblinStronghold.Simulation.Map;
using GoblinStronghold.Simulation.Planning;
using GoblinStronghold.Simulation.Resources;
using GoblinStronghold.Simulation.Workshops;

namespace GoblinStronghold.Simulation.Construction;

public enum ConstructionFootprintKind : byte
{
    Single = 1,
    Linear = 2,
    Area = 3,
    FixedRectangle = 4,
}

public enum ConstructionQuantityMode : byte
{
    Fixed = 1,
    PerFootprintCell = 2,
    WorkshopRequirements = 3,
}

public enum ConstructionPlanningMode : byte
{
    SimplePlacement = 1,
    BuildingBlueprint = 2,
    CellDesignation = 3,
}

public sealed record ConstructionBlueprintDefinition(
    string Id,
    ConstructionKind Kind,
    ConstructionPlanningMode PlanningMode,
    WorldToolPlacementMode PlacementMode,
    IReadOnlyList<string> MenuPath,
    ConstructionFootprintKind Footprint,
    int FootprintWidth,
    int FootprintHeight,
    ResourceKind RequiredResource,
    ResourceVariant RequiredVariant,
    ConstructionQuantityMode QuantityMode,
    int MaterialQuantity,
    bool RetainsMaterialIdentity,
    int WorkTicks,
    bool WorkTicksPerFootprintCell,
    ConstructionCapabilityRequirements Capabilities,
    WorkshopKind? Workshop)
{
    public ContentId StableId => ContentId.Parse(Id);

    public IReadOnlyList<GridPosition> GetFootprint(GridPosition anchor, GridPosition end) =>
        Footprint switch
        {
            ConstructionFootprintKind.Single => [anchor],
            ConstructionFootprintKind.Linear => SimulationCommand.GetLinearCells(anchor, end),
            ConstructionFootprintKind.Area => SimulationCommand.GetAreaCells(anchor, end),
            ConstructionFootprintKind.FixedRectangle => Enumerable.Range(0, FootprintHeight)
                .SelectMany(y => Enumerable.Range(0, FootprintWidth)
                    .Select(x => new GridPosition(anchor.X + x, anchor.Y + y, anchor.Z)))
                .ToArray(),
            _ => throw new InvalidOperationException(
                $"Unsupported construction footprint '{Footprint}'."),
        };

    public int GetRequiredQuantity(int footprintCellCount) => QuantityMode switch
    {
        ConstructionQuantityMode.Fixed => MaterialQuantity,
        ConstructionQuantityMode.PerFootprintCell =>
            checked(MaterialQuantity * footprintCellCount),
        ConstructionQuantityMode.WorkshopRequirements => WorkshopCatalog.Get(
            Workshop ?? throw new InvalidOperationException(
                $"Construction blueprint '{Id}' has no workshop.")).ConstructionRequirements
            .Sum(item => item.Quantity),
        _ => throw new InvalidOperationException(
            $"Unsupported construction quantity mode '{QuantityMode}'."),
    };

    public int GetWorkTicks(int footprintCellCount) => WorkTicksPerFootprintCell
        ? checked(WorkTicks * footprintCellCount)
        : WorkTicks;
}

public static class ConstructionBlueprintDefinitions
{
    private const string ContentPath = "content/construction-blueprints.json";
    private static readonly Lazy<CatalogState> State = new(Load);

    public static IReadOnlyList<ConstructionBlueprintDefinition> All => State.Value.All;

    public static ConstructionBlueprintDefinition Get(ConstructionKind kind) =>
        State.Value.ByKind.TryGetValue(kind, out var definition)
            ? definition
            : throw new KeyNotFoundException($"Unknown construction kind '{kind}'.");

    public static ConstructionBlueprintDefinition Get(string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        var stableId = ContentId.Parse(id);
        return State.Value.ByStableId.TryGetValue(stableId.Value, out var definition)
            ? definition
            : throw new KeyNotFoundException($"Unknown construction blueprint id '{id}'.");
    }

    public static IReadOnlyList<string> GetMenuChildren(params string[] parentPath)
    {
        ValidateMenuPath(parentPath);
        return Array.AsReadOnly(All
            .Where(definition => HasMenuPrefix(definition, parentPath) &&
                definition.MenuPath.Count > parentPath.Length)
            .Select(definition => definition.MenuPath[parentPath.Length])
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray());
    }

    public static IReadOnlyList<ConstructionBlueprintDefinition> GetMenuBlueprints(
        params string[] menuPath)
    {
        ValidateMenuPath(menuPath);
        return Array.AsReadOnly(All.Where(definition =>
            definition.MenuPath.Count == menuPath.Length &&
            HasMenuPrefix(definition, menuPath)).ToArray());
    }

    private static CatalogState Load()
    {
        using var stream = CoreContentPack.Pack.OpenRead(ContentPath);
        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            Converters = { new JsonStringEnumConverter() },
        };
        var document = JsonSerializer.Deserialize<CatalogDocument>(stream, options)
            ?? throw new InvalidOperationException("Construction blueprint catalog is empty.");
        if (document.SchemaVersion != 1)
        {
            throw new InvalidOperationException(
                $"Unsupported construction blueprint schema {document.SchemaVersion}.");
        }

        var definitions = document.Blueprints.Select(ToDefinition).ToArray();
        ValidateCompleteness(definitions);
        return new CatalogState(
            Array.AsReadOnly(definitions),
            new ReadOnlyDictionary<string, ConstructionBlueprintDefinition>(
                definitions.ToDictionary(
                    definition => definition.StableId.Value,
                    StringComparer.OrdinalIgnoreCase)),
            new ReadOnlyDictionary<ConstructionKind, ConstructionBlueprintDefinition>(
                definitions.ToDictionary(definition => definition.Kind)));
    }

    private static ConstructionBlueprintDefinition ToDefinition(DefinitionDto source)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(source.Id);
        var capabilities = source.Capabilities;
        if (!Enum.IsDefined(source.Kind) || !Enum.IsDefined(source.PlanningMode) ||
            !Enum.IsDefined(source.PlacementMode) ||
            source.MenuPath.Count == 0 || source.MenuPath.Any(segment =>
                segment.Contains(':') || !ContentId.TryParse(segment, out _)) ||
            !Enum.IsDefined(source.Footprint) ||
            !Enum.IsDefined(source.RequiredResource) ||
            source.RequiredResource == ResourceKind.Any ||
            !Enum.IsDefined(source.RequiredVariant) ||
            !Enum.IsDefined(source.QuantityMode) || source.MaterialQuantity < 1 ||
            source.WorkTicks < 1 || capabilities.MinimumBuildingLevel < 0 ||
            !Enum.IsDefined(capabilities.RequiredSkills) ||
            !Enum.IsDefined(capabilities.RequiredEquipment) ||
            source.Footprint == ConstructionFootprintKind.FixedRectangle &&
                (source.FootprintWidth < 1 || source.FootprintHeight < 1) ||
            source.Footprint != ConstructionFootprintKind.FixedRectangle &&
                (source.FootprintWidth != 0 || source.FootprintHeight != 0) ||
            source.QuantityMode == ConstructionQuantityMode.WorkshopRequirements &&
                source.Workshop is null ||
            source.QuantityMode != ConstructionQuantityMode.WorkshopRequirements &&
                source.Workshop is not null ||
            source.Workshop is not null && !Enum.IsDefined(source.Workshop.Value))
        {
            throw new InvalidOperationException(
                $"Construction blueprint '{source.Id}' has an invalid definition.");
        }

        return new ConstructionBlueprintDefinition(
            source.Id,
            source.Kind,
            source.PlanningMode,
            source.PlacementMode,
            Array.AsReadOnly(source.MenuPath.ToArray()),
            source.Footprint,
            source.FootprintWidth,
            source.FootprintHeight,
            source.RequiredResource,
            source.RequiredVariant,
            source.QuantityMode,
            source.MaterialQuantity,
            source.RetainsMaterialIdentity,
            source.WorkTicks,
            source.WorkTicksPerFootprintCell,
            new ConstructionCapabilityRequirements(
                capabilities.RequiredSkills,
                capabilities.MinimumBuildingLevel,
                capabilities.RequiredEquipment),
            source.Workshop);
    }

    private static void ValidateCompleteness(ConstructionBlueprintDefinition[] definitions)
    {
        if (definitions.Select(item => item.Id)
                .Distinct(StringComparer.OrdinalIgnoreCase).Count() != definitions.Length ||
            definitions.Select(item => item.Kind).Distinct().Count() != definitions.Length ||
            !definitions.Select(item => item.Kind).Order().SequenceEqual(
                Enum.GetValues<ConstructionKind>().Order()))
        {
            throw new InvalidOperationException(
                "Construction catalog must define every construction kind exactly once.");
        }

        foreach (var definition in definitions.Where(item => item.Workshop is not null))
        {
            _ = WorkshopCatalog.Get(definition.Workshop!.Value);
        }

        foreach (var definition in definitions)
        {
            var compatiblePlacement = definition.PlacementMode switch
            {
                WorldToolPlacementMode.Point =>
                    definition.Footprint == ConstructionFootprintKind.Single,
                WorldToolPlacementMode.Line =>
                    definition.Footprint == ConstructionFootprintKind.Linear,
                WorldToolPlacementMode.Area =>
                    definition.Footprint == ConstructionFootprintKind.Area,
                WorldToolPlacementMode.FixedFootprint =>
                    definition.Footprint == ConstructionFootprintKind.FixedRectangle,
                WorldToolPlacementMode.InferredConnection =>
                    definition.Footprint == ConstructionFootprintKind.Single,
                _ => false,
            };
            if (!compatiblePlacement)
            {
                throw new InvalidOperationException(
                    $"Construction blueprint '{definition.Id}' has incompatible placement " +
                    $"and footprint modes.");
            }
        }
    }

    private static bool HasMenuPrefix(
        ConstructionBlueprintDefinition definition,
        IReadOnlyList<string> path) => path.Count <= definition.MenuPath.Count &&
        path.Select((segment, index) => string.Equals(
            segment,
            definition.MenuPath[index],
            StringComparison.OrdinalIgnoreCase)).All(matches => matches);

    private static void ValidateMenuPath(IReadOnlyList<string> path)
    {
        ArgumentNullException.ThrowIfNull(path);
        if (path.Any(segment => string.IsNullOrWhiteSpace(segment) ||
            segment.Contains(':') || !ContentId.TryParse(segment, out _)))
        {
            throw new ArgumentException("Menu path contains an invalid stable segment.", nameof(path));
        }
    }

    private sealed record CatalogState(
        IReadOnlyList<ConstructionBlueprintDefinition> All,
        IReadOnlyDictionary<string, ConstructionBlueprintDefinition> ByStableId,
        IReadOnlyDictionary<ConstructionKind, ConstructionBlueprintDefinition> ByKind);

    private sealed class CatalogDocument
    {
        public int SchemaVersion { get; init; }
        public List<DefinitionDto> Blueprints { get; init; } = [];
    }

    private sealed class DefinitionDto
    {
        public string Id { get; init; } = string.Empty;
        public ConstructionKind Kind { get; init; }
        public ConstructionPlanningMode PlanningMode { get; init; }
        public WorldToolPlacementMode PlacementMode { get; init; }
        public List<string> MenuPath { get; init; } = [];
        public ConstructionFootprintKind Footprint { get; init; }
        public int FootprintWidth { get; init; }
        public int FootprintHeight { get; init; }
        public ResourceKind RequiredResource { get; init; }
        public ResourceVariant RequiredVariant { get; init; }
        public ConstructionQuantityMode QuantityMode { get; init; }
        public int MaterialQuantity { get; init; }
        public bool RetainsMaterialIdentity { get; init; }
        public int WorkTicks { get; init; }
        public bool WorkTicksPerFootprintCell { get; init; }
        public CapabilityDto Capabilities { get; init; } = new();
        public WorkshopKind? Workshop { get; init; }
    }

    private sealed class CapabilityDto
    {
        public GoblinSkill RequiredSkills { get; init; }
        public int MinimumBuildingLevel { get; init; }
        public PersonalEquipment RequiredEquipment { get; init; }
    }
}
