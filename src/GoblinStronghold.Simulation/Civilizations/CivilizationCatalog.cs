using System.Collections.ObjectModel;
using System.Text.Json;
using System.Text.Json.Serialization;
using GoblinStronghold.Simulation.ContentPacks;

namespace GoblinStronghold.Simulation.Civilizations;

public sealed class CivilizationCatalog : ICivilizationCatalog
{
    private const string ContentPath = "content/civilizations.json";
    private readonly IReadOnlyDictionary<ContentId, CivilizationDefinition> byId;
    private readonly IReadOnlyDictionary<CivilizationLegacyRole, CivilizationDefinition> byRole;
    private readonly IReadOnlyDictionary<UndergroundFactionKind, CivilizationDefinition>
        byUndergroundKind;

    public CivilizationCatalog(IEnumerable<CivilizationDefinition> definitions)
    {
        ArgumentNullException.ThrowIfNull(definitions);
        var all = definitions.ToArray();
        Validate(all);
        All = Array.AsReadOnly(all);
        byId = new ReadOnlyDictionary<ContentId, CivilizationDefinition>(
            all.ToDictionary(definition => definition.Id));
        byRole = new ReadOnlyDictionary<CivilizationLegacyRole, CivilizationDefinition>(
            all.Where(definition => definition.LegacyRole is not null)
                .ToDictionary(
                    definition => definition.LegacyRole!.Value,
                    definition => definition));
        byUndergroundKind = new ReadOnlyDictionary<
            UndergroundFactionKind,
            CivilizationDefinition>(all
                .Where(definition => definition.UndergroundGeneration is not null)
                .ToDictionary(
                    definition => definition.UndergroundGeneration!.LegacyKind,
                    definition => definition));
    }

    public static CivilizationCatalog Core { get; } = LoadCore();
    private static CivilizationCatalog current = Core;

    public static CivilizationCatalog Current => Volatile.Read(ref current);

    public IReadOnlyList<CivilizationDefinition> All { get; }

    public CivilizationDefinition Get(ContentId id) =>
        byId.TryGetValue(id, out var definition)
            ? definition
            : throw new KeyNotFoundException($"Unknown civilization ID '{id}'.");

    public CivilizationDefinition Get(CivilizationLegacyRole role) =>
        byRole.TryGetValue(role, out var definition)
            ? definition
            : throw new KeyNotFoundException($"Unknown civilization role '{role}'.");

    public CivilizationDefinition Get(UndergroundFactionKind kind) =>
        byUndergroundKind.TryGetValue(kind, out var definition)
            ? definition
            : throw new KeyNotFoundException(
                $"Unknown underground civilization kind '{kind}'.");

    public static CivilizationCatalog Compose(IEnumerable<ContentPack> externalPacks)
    {
        ArgumentNullException.ThrowIfNull(externalPacks);
        var definitions = Core.All.ToDictionary(
            definition => definition.Id,
            definition => definition);
        foreach (var pack in externalPacks.Where(pack =>
                     pack.Manifest.Type == "content" && pack.Contains(ContentPath)))
        {
            foreach (var definition in ReadDocument(pack))
            {
                if (definition.Id.PackageId != ContentId.CoreNamespace &&
                    !string.Equals(
                        definition.Id.PackageId,
                        pack.Manifest.Id,
                        StringComparison.Ordinal))
                {
                    throw new InvalidDataException(
                        $"Content pack '{pack.Manifest.Id}' cannot define civilization " +
                        $"owned by '{definition.Id.PackageId}'.");
                }
                definitions[definition.Id] = definition;
            }
        }
        return new CivilizationCatalog(definitions.Values);
    }

    public static void Activate(CivilizationCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        Volatile.Write(ref current, catalog);
    }

    public static void ResetToCore() => Activate(Core);

    private static CivilizationCatalog LoadCore() =>
        new(ReadDocument(CoreContentPack.Pack));

    private static IReadOnlyList<CivilizationDefinition> ReadDocument(ContentPack pack)
    {
        using var stream = pack.OpenRead(ContentPath);
        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            Converters =
            {
                new JsonStringEnumConverter(),
                new ContentIdJsonConverter(),
            },
        };
        CivilizationCatalogDocument document;
        try
        {
            document = JsonSerializer.Deserialize<CivilizationCatalogDocument>(
                stream,
                options) ?? throw new InvalidDataException(
                    $"Civilization catalog in '{pack.Manifest.Id}' is empty.");
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException(
                $"Civilization catalog in '{pack.Manifest.Id}' is invalid.",
                exception);
        }
        if (document.SchemaVersion != 1)
        {
            throw new InvalidDataException(
                $"Unsupported civilization catalog schema {document.SchemaVersion}.");
        }
        return document.Civilizations;
    }

    private static void Validate(IReadOnlyList<CivilizationDefinition> definitions)
    {
        var legacyRoles = definitions
            .Where(definition => definition.LegacyRole is not null)
            .Select(definition => definition.LegacyRole!.Value)
            .ToArray();
        var undergroundKinds = definitions
            .Where(definition => definition.UndergroundGeneration is not null)
            .Select(definition => definition.UndergroundGeneration!.LegacyKind)
            .ToArray();
        if (definitions.Count == 0 ||
            definitions.Select(definition => definition.Id).Distinct().Count() !=
                definitions.Count ||
            legacyRoles.Distinct().Count() != legacyRoles.Length ||
            undergroundKinds.Distinct().Count() != undergroundKinds.Length ||
            !legacyRoles.Order().SequenceEqual(
                Enum.GetValues<CivilizationLegacyRole>().Order()) ||
            definitions.Count(definition => definition.PlayerControllable) != 1 ||
            definitions.Any(IsInvalid))
        {
            throw new InvalidDataException(
                "The civilization catalog is incomplete or contains invalid definitions.");
        }
    }

    private static bool IsInvalid(CivilizationDefinition definition)
    {
        var generation = definition.UndergroundGeneration;
        var behavior = definition.UndergroundBehavior;
        return !ContentId.TryParse(definition.Id.Value, out _) ||
            definition.LegacyRole is { } role && !Enum.IsDefined(role) ||
            definition.PlayerControllable !=
                (definition.LegacyRole == CivilizationLegacyRole.PlayerGoblins) ||
            definition.Identity is null ||
            !ContentId.TryParse(definition.Identity.SpeciesId.Value, out _) ||
            !ContentId.TryParse(definition.Identity.ControllerId.Value, out _) ||
            !ContentId.TryParse(definition.Identity.NameGeneratorId.Value, out _) ||
            (generation is null) != (behavior is null) ||
            generation is not null && (
                !Enum.IsDefined(generation.LegacyKind) ||
                generation.FirstLevel >= 0 ||
                generation.DepthBandSize < 1 ||
                generation.PresencePercent is < 0 or > 100 ||
                generation.BaseMinimumPopulation < 1 ||
                generation.MinimumPopulationPerBand < 0 ||
                generation.BaseMaximumPopulationExclusive <=
                    generation.BaseMinimumPopulation ||
                generation.MaximumPopulationPerBand <
                    generation.MinimumPopulationPerBand ||
                generation.MinimumFighters < 1 ||
                generation.FighterPopulationDivisor < 1 ||
                generation.ProvisionsPerCapita < 0 ||
                generation.OrePerCapitaBandOffset < 0 ||
                generation.BaseFortification < 0 ||
                generation.FortificationPerBand < 0) ||
            behavior is not null && (
                behavior.LowProvisionPopulationMultiplier < 1 ||
                behavior.ProvisionGatherPopulationDivisor < 1 ||
                behavior.OreTargetBandOffset < 0 ||
                behavior.OreGatherPopulationDivisor < 1 ||
                behavior.ProvisionConsumptionPopulationDivisor < 1 ||
                behavior.ConflictIntervalDays < 1 ||
                behavior.ConflictLossFighterDivisor < 1 ||
                behavior.HostileRelationPercent is < 0 or > 100 ||
                behavior.WaryRelationPercent is < 0 or > 100 ||
                behavior.HostileRelationPercent + behavior.WaryRelationPercent > 100);
    }

    private sealed class CivilizationCatalogDocument
    {
        public int SchemaVersion { get; init; }
        public List<CivilizationDefinition> Civilizations { get; init; } = [];
    }
}
