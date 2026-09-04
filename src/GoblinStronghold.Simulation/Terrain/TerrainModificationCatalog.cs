using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization;
using GoblinStronghold.Simulation.ContentPacks;
using GoblinStronghold.Simulation.Map;
using GoblinStronghold.Simulation.Planning;
using GoblinStronghold.Simulation.Resources;

namespace GoblinStronghold.Simulation.Terrain;

public sealed record TerrainDepositYieldDefinition(
    MineralDepositKind Deposit,
    ResourceKind Resource,
    ResourceVariant Variant,
    int MinimumQuantity,
    int MaximumQuantityExclusive);

public sealed record TerrainYieldDefinition(
    ResourceKind Resource,
    ResourceVariant Variant,
    bool VariantFromRock,
    int MinimumQuantity,
    int MaximumQuantityExclusive,
    int MinimumBuildingExperience,
    int BuildingExperiencePerUnit,
    IReadOnlyDictionary<MineralDepositKind, TerrainDepositYieldDefinition> Deposits);

public sealed record TerrainWorkDefinition(
    int BaseTicksMultiplier,
    TerrainYieldDefinition Yield);

public sealed record TerrainModificationDefinition(
    string Id,
    WorkDesignationKind LegacyDesignation,
    WorldToolPlacementMode PlacementMode,
    IReadOnlyList<string> MenuPath,
    TerrainWorkDefinition Work)
{
    public ContentId StableId => ContentId.Parse(Id);
}

public static class TerrainModificationCatalog
{
    private const string ContentPath = "content/terrain-modifications.json";
    private static readonly WorkDesignationKind[] RequiredLegacyDefinitions =
    [
        WorkDesignationKind.MineRock,
        WorkDesignationKind.CarveRampDown,
        WorkDesignationKind.CarveRampUp,
        WorkDesignationKind.StripFloor,
    ];
    private static readonly Lazy<CatalogState> State = new(Load);

    public static IReadOnlyList<TerrainModificationDefinition> All => State.Value.All;

    public static TerrainModificationDefinition Get(WorkDesignationKind designation) =>
        State.Value.ByLegacyDesignation.TryGetValue(designation, out var definition)
            ? definition
            : throw new KeyNotFoundException(
                $"Work designation '{designation}' is not a terrain modification.");

    public static TerrainModificationDefinition Get(string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        var stableId = ContentId.Parse(id);
        return State.Value.ByStableId.TryGetValue(stableId.Value, out var definition)
            ? definition
            : throw new KeyNotFoundException($"Unknown terrain modification id '{id}'.");
    }

    public static bool TryGet(
        WorkDesignationKind designation,
        [NotNullWhen(true)] out TerrainModificationDefinition? definition) =>
        State.Value.ByLegacyDesignation.TryGetValue(designation, out definition);

    private static CatalogState Load()
    {
        using var stream = CoreContentPack.Pack.OpenRead(ContentPath);
        var definitions = LoadDefinitions(stream).ToArray();
        return new CatalogState(
            Array.AsReadOnly(definitions),
            new ReadOnlyDictionary<string, TerrainModificationDefinition>(
                definitions.ToDictionary(
                    definition => definition.StableId.Value,
                    StringComparer.OrdinalIgnoreCase)),
            new ReadOnlyDictionary<WorkDesignationKind, TerrainModificationDefinition>(
                definitions.ToDictionary(definition => definition.LegacyDesignation)));
    }

    internal static IReadOnlyList<TerrainModificationDefinition> LoadDefinitions(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            Converters = { new JsonStringEnumConverter() },
        };
        var document = JsonSerializer.Deserialize<CatalogDocument>(stream, options)
            ?? throw new InvalidOperationException("Terrain modification catalog is empty.");
        if (document.SchemaVersion != 2)
        {
            throw new InvalidOperationException(
                $"Unsupported terrain modification schema {document.SchemaVersion}.");
        }

        var definitions = document.Modifications.Select(ToDefinition).ToArray();
        ValidateCompleteness(definitions);
        return Array.AsReadOnly(definitions);
    }

    private static TerrainModificationDefinition ToDefinition(DefinitionDto source)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(source.Id);
        if (!Enum.IsDefined(source.LegacyDesignation) ||
            !Enum.IsDefined(source.PlacementMode) ||
            source.MenuPath.Count == 0 || source.MenuPath.Any(segment =>
                segment.Contains(':') || !ContentId.TryParse(segment, out _)))
        {
            throw new InvalidOperationException(
                $"Terrain modification '{source.Id}' has an invalid definition.");
        }

        return new TerrainModificationDefinition(
            source.Id,
            source.LegacyDesignation,
            source.PlacementMode,
            Array.AsReadOnly(source.MenuPath.ToArray()),
            ToWorkDefinition(source.Id, source.Work));
    }

    private static TerrainWorkDefinition ToWorkDefinition(string id, WorkDto source)
    {
        var yield = source.Yield;
        if (source.BaseTicksMultiplier <= 0 ||
            !Enum.IsDefined(yield.Resource) || yield.Resource == ResourceKind.Any ||
            !Enum.IsDefined(yield.Variant) ||
            yield.VariantFromRock && yield.Resource != ResourceKind.Stone ||
            yield.MinimumQuantity <= 0 ||
            yield.MaximumQuantityExclusive <= yield.MinimumQuantity ||
            yield.MinimumBuildingExperience < 0 || yield.BuildingExperiencePerUnit < 0)
        {
            throw new InvalidOperationException(
                $"Terrain modification '{id}' has invalid work or yield values.");
        }

        var deposits = yield.Deposits.Select(item =>
        {
            if (!Enum.IsDefined(item.Deposit) || item.Deposit == MineralDepositKind.None ||
                !Enum.IsDefined(item.Resource) || item.Resource == ResourceKind.Any ||
                !Enum.IsDefined(item.Variant) || item.MinimumQuantity <= 0 ||
                item.MaximumQuantityExclusive <= item.MinimumQuantity)
            {
                throw new InvalidOperationException(
                    $"Terrain modification '{id}' has an invalid deposit yield.");
            }

            return new TerrainDepositYieldDefinition(
                item.Deposit,
                item.Resource,
                item.Variant,
                item.MinimumQuantity,
                item.MaximumQuantityExclusive);
        }).ToArray();
        if (deposits.Select(item => item.Deposit).Distinct().Count() != deposits.Length)
        {
            throw new InvalidOperationException(
                $"Terrain modification '{id}' defines a deposit more than once.");
        }

        return new TerrainWorkDefinition(
            source.BaseTicksMultiplier,
            new TerrainYieldDefinition(
                yield.Resource,
                yield.Variant,
                yield.VariantFromRock,
                yield.MinimumQuantity,
                yield.MaximumQuantityExclusive,
                yield.MinimumBuildingExperience,
                yield.BuildingExperiencePerUnit,
                new ReadOnlyDictionary<MineralDepositKind, TerrainDepositYieldDefinition>(
                    deposits.ToDictionary(item => item.Deposit))));
    }

    private static void ValidateCompleteness(TerrainModificationDefinition[] definitions)
    {
        if (definitions.Select(item => item.Id)
                .Distinct(StringComparer.OrdinalIgnoreCase).Count() != definitions.Length ||
            definitions.Select(item => item.LegacyDesignation).Distinct().Count() !=
                definitions.Length ||
            !definitions.Select(item => item.LegacyDesignation).Order().SequenceEqual(
                RequiredLegacyDefinitions.Order()))
        {
            throw new InvalidOperationException(
                "Terrain catalog must define every supported legacy terrain action exactly once.");
        }

        foreach (var definition in definitions)
        {
            var expectedPlacement = definition.LegacyDesignation is
                WorkDesignationKind.MineRock or WorkDesignationKind.StripFloor
                ? WorldToolPlacementMode.Area
                : WorldToolPlacementMode.Point;
            if (definition.PlacementMode != expectedPlacement)
            {
                throw new InvalidOperationException(
                    $"Terrain modification '{definition.Id}' has an incompatible placement mode.");
            }
            IEnumerable<MineralDepositKind> expectedDeposits =
                definition.LegacyDesignation == WorkDesignationKind.MineRock
                ? Enum.GetValues<MineralDepositKind>()
                    .Where(deposit => deposit != MineralDepositKind.None)
                    .Order()
                : [];
            if (!definition.Work.Yield.Deposits.Keys.Order().SequenceEqual(expectedDeposits))
            {
                throw new InvalidOperationException(
                    $"Terrain modification '{definition.Id}' has an incomplete deposit table.");
            }
        }
    }

    private sealed record CatalogState(
        IReadOnlyList<TerrainModificationDefinition> All,
        IReadOnlyDictionary<string, TerrainModificationDefinition> ByStableId,
        IReadOnlyDictionary<WorkDesignationKind, TerrainModificationDefinition>
            ByLegacyDesignation);

    private sealed class CatalogDocument
    {
        public int SchemaVersion { get; init; }
        public List<DefinitionDto> Modifications { get; init; } = [];
    }

    private sealed class DefinitionDto
    {
        public string Id { get; init; } = string.Empty;
        public WorkDesignationKind LegacyDesignation { get; init; }
        public WorldToolPlacementMode PlacementMode { get; init; }
        public List<string> MenuPath { get; init; } = [];
        public WorkDto Work { get; init; } = new();
    }

    private sealed class WorkDto
    {
        public int BaseTicksMultiplier { get; init; }
        public YieldDto Yield { get; init; } = new();
    }

    private sealed class YieldDto
    {
        public ResourceKind Resource { get; init; }
        public ResourceVariant Variant { get; init; }
        public bool VariantFromRock { get; init; }
        public int MinimumQuantity { get; init; }
        public int MaximumQuantityExclusive { get; init; }
        public int MinimumBuildingExperience { get; init; }
        public int BuildingExperiencePerUnit { get; init; }
        public List<DepositYieldDto> Deposits { get; init; } = [];
    }

    private sealed class DepositYieldDto
    {
        public MineralDepositKind Deposit { get; init; }
        public ResourceKind Resource { get; init; }
        public ResourceVariant Variant { get; init; }
        public int MinimumQuantity { get; init; }
        public int MaximumQuantityExclusive { get; init; }
    }
}
