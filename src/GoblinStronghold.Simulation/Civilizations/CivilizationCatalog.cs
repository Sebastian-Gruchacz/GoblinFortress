using System.Collections.ObjectModel;
using System.Text.Json;
using System.Text.Json.Serialization;
using GoblinStronghold.Simulation.Civilizations.Naming;
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
        : this(definitions, NameGeneratorCatalog.Current)
    {
    }

    private CivilizationCatalog(
        IEnumerable<CivilizationDefinition> definitions,
        NameGeneratorCatalog nameGenerators)
    {
        ArgumentNullException.ThrowIfNull(definitions);
        ArgumentNullException.ThrowIfNull(nameGenerators);
        var all = definitions.ToArray();
        Validate(all, nameGenerators);
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

    public static CivilizationCatalog Compose(IEnumerable<ContentPack> externalPacks) =>
        Compose(externalPacks, NameGeneratorCatalog.Compose(externalPacks));

    public static CivilizationCatalog Compose(
        IEnumerable<ContentPack> externalPacks,
        NameGeneratorCatalog nameGenerators)
    {
        ArgumentNullException.ThrowIfNull(externalPacks);
        ArgumentNullException.ThrowIfNull(nameGenerators);
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
        return new CivilizationCatalog(definitions.Values, nameGenerators);
    }

    public static void Activate(CivilizationCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        Volatile.Write(ref current, catalog);
    }

    public static void ResetToCore() => Activate(Core);

    private static CivilizationCatalog LoadCore() =>
        new(ReadDocument(CoreContentPack.Pack), NameGeneratorCatalog.Core);

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
        if (document.SchemaVersion != 8)
        {
            throw new InvalidDataException(
                $"Unsupported civilization catalog schema {document.SchemaVersion}.");
        }
        return document.Civilizations;
    }

    private static void Validate(
        IReadOnlyList<CivilizationDefinition> definitions,
        NameGeneratorCatalog nameGenerators)
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
            definitions.Any(definition => IsInvalid(definition, nameGenerators)))
        {
            throw new InvalidDataException(
                "The civilization catalog is incomplete or contains invalid definitions.");
        }
    }

    private static bool IsInvalid(
        CivilizationDefinition definition,
        NameGeneratorCatalog nameGenerators)
    {
        var generation = definition.UndergroundGeneration;
        var behavior = definition.UndergroundBehavior;
        var vitals = definition.Vitals;
        var combat = definition.Combat;
        var perception = definition.Perception;
        var spatialBehavior = definition.SpatialBehavior;
        var needs = definition.Needs;
        var populationNeeds = definition.PopulationNeeds;
        var aging = definition.Aging;
        var actorGeneration = definition.ActorGeneration;
        return !ContentId.TryParse(definition.Id.Value, out _) ||
            definition.LegacyRole is { } role && !Enum.IsDefined(role) ||
            definition.PlayerControllable !=
                (definition.LegacyRole == CivilizationLegacyRole.PlayerGoblins) ||
            definition.Identity is null ||
            !ContentId.TryParse(definition.Identity.SpeciesId.Value, out _) ||
            !ContentId.TryParse(definition.Identity.ControllerId.Value, out _) ||
            !ContentId.TryParse(definition.Identity.NameGeneratorId.Value, out _) ||
            !nameGenerators.Contains(definition.Identity.NameGeneratorId) ||
            (definition.LegacyRole is CivilizationLegacyRole.PlayerGoblins or
                CivilizationLegacyRole.HumanVillage) && vitals is null ||
            vitals is not null && vitals.MaximumHealth < 1 ||
            (definition.LegacyRole is CivilizationLegacyRole.PlayerGoblins or
                CivilizationLegacyRole.HumanVillage) && combat is null ||
            combat is not null && (
                combat.MinimumMeleeDamage < 1 ||
                combat.MeleeDamageVariance < 0) ||
            (definition.LegacyRole is CivilizationLegacyRole.PlayerGoblins or
                CivilizationLegacyRole.HumanVillage) && perception is null ||
            perception is not null && (
                perception.DayVisionRadius < 1 ||
                perception.NightVisionRadius < 1 ||
                perception.StructureVisionRadius < 0) ||
            (definition.LegacyRole is CivilizationLegacyRole.PlayerGoblins or
                CivilizationLegacyRole.HumanVillage) && spatialBehavior is null ||
            spatialBehavior is not null && (
                spatialBehavior.MovementIntervalTicks < 1 ||
                spatialBehavior.ActivityRadius < 0 ||
                spatialBehavior.MaximumExplorers < 0) ||
            definition.LegacyRole == CivilizationLegacyRole.PlayerGoblins && needs is null ||
            needs is not null && IsInvalid(needs) ||
            definition.LegacyRole == CivilizationLegacyRole.HumanVillage &&
                populationNeeds is null ||
            populationNeeds is not null && IsInvalid(populationNeeds) ||
            definition.LegacyRole == CivilizationLegacyRole.PlayerGoblins && aging is null ||
            aging is not null && (
                aging.HealthyYears < 1 ||
                aging.DeclineMinimumSeasons < 1 ||
                aging.DeclineMaximumSeasons < aging.DeclineMinimumSeasons ||
                aging.TerminalHealthPermille is < 1 or > 1_000 ||
                aging.InitialMinimumAgeYears < 0 ||
                aging.InitialMaximumAgeYearsExclusive <= aging.InitialMinimumAgeYears ||
                aging.InitialMaximumAgeYearsExclusive > aging.HealthyYears) ||
            definition.LegacyRole == CivilizationLegacyRole.PlayerGoblins &&
                actorGeneration is null ||
            actorGeneration is not null && IsInvalid(actorGeneration) ||
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

    private static bool IsInvalid(CivilizationActorGenerationDefinition generation) =>
        !Enum.IsDefined(generation.RandomDomain) ||
        !IsValidSingleFlagPool(generation.SkillPool) ||
        generation.SkillSampleKeys.Count == 0 ||
        generation.SkillSampleKeys.Distinct().Count() !=
            generation.SkillSampleKeys.Count ||
        !IsValidSingleFlagPool(generation.TraitPool) ||
        generation.TraitSampleKeys.Count == 0 ||
        generation.TraitSampleKeys.Distinct().Count() !=
            generation.TraitSampleKeys.Count ||
        !HasOnlyKnownFlags(
            generation.GuaranteedEquipment,
            PersonalEquipment.WoodenBucket) ||
        generation.GuaranteedEquipment == PersonalEquipment.None ||
        !HasOnlyKnownFlags(
            generation.OptionalEquipment,
            PersonalEquipment.WoodenBucket) ||
        generation.OptionalEquipment == PersonalEquipment.None ||
        (generation.GuaranteedEquipment & generation.OptionalEquipment) != 0 ||
        generation.OptionalEquipmentRollMaximumExclusive < 1 ||
        generation.OptionalEquipmentSuccessValue < 0 ||
        generation.OptionalEquipmentSuccessValue >=
            generation.OptionalEquipmentRollMaximumExclusive ||
        generation.WorkPreferenceMinimum < GoblinWorkPreferences.Minimum ||
        generation.WorkPreferenceMaximum > GoblinWorkPreferences.Maximum ||
        generation.WorkPreferenceMaximum < generation.WorkPreferenceMinimum ||
        generation.WorkPreferenceSampleKeys.Count != 3 ||
        generation.WorkPreferenceSampleKeys.Distinct().Count() != 3;

    private static bool IsValidSingleFlagPool<T>(IReadOnlyCollection<T> values)
        where T : struct, Enum => values.Count > 0 &&
        values.Distinct().Count() == values.Count &&
        values.All(value =>
        {
            var numeric = Convert.ToUInt64(value);
            return Enum.IsDefined(value) && numeric != 0 && (numeric & (numeric - 1)) == 0;
        });

    private static bool HasOnlyKnownFlags<T>(T value, T highestKnownFlag)
        where T : struct, Enum
    {
        var mask = (Convert.ToUInt64(highestKnownFlag) << 1) - 1;
        return (Convert.ToUInt64(value) & ~mask) == 0;
    }

    private static bool IsInvalid(CivilizationNeedsDefinition needs) =>
        needs.MaximumHunger < 1 ||
        needs.HungerPerTick < 0 ||
        needs.EatThreshold is < 1 || needs.EatThreshold > needs.MaximumHunger ||
        needs.FoodSeekThreshold is < 1 ||
        needs.FoodSeekThreshold > needs.MaximumHunger ||
        needs.CriticalHungerThreshold < needs.FoodSeekThreshold ||
        needs.CriticalHungerThreshold > needs.MaximumHunger ||
        needs.StarvationHungerThreshold < needs.CriticalHungerThreshold ||
        needs.StarvationHungerThreshold > needs.MaximumHunger ||
        needs.StarvationDamagePerTick < 1 ||
        needs.MaximumThirst < 1 ||
        needs.ThirstPerTick < 0 ||
        needs.DrinkThreshold is < 1 || needs.DrinkThreshold > needs.MaximumThirst ||
        needs.DehydrationThirstThreshold < needs.DrinkThreshold ||
        needs.DehydrationThirstThreshold > needs.MaximumThirst ||
        needs.DehydrationDamagePerTick < 1 ||
        needs.MaximumFatigue < 1 ||
        needs.FatiguePerTick < 0 ||
        needs.RestThreshold is < 1 || needs.RestThreshold > needs.MaximumFatigue;

    private static bool IsInvalid(CivilizationPopulationNeedsDefinition needs) =>
        needs.MaximumNeed < 1 ||
        needs.DailyHungerIncrease < 0 ||
        needs.DailyThirstIncrease < 0 ||
        needs.MealRelief is < 1 || needs.MealRelief > needs.MaximumNeed ||
        needs.DrinkRelief is < 1 || needs.DrinkRelief > needs.MaximumNeed ||
        needs.MaximumFatigue < 1 ||
        needs.RestThreshold is < 1 || needs.RestThreshold > needs.MaximumFatigue ||
        needs.WorkFatiguePerMove < 0 ||
        needs.DayRestRecoveryPerMove < 1 ||
        needs.NightRestRecoveryPerMove < 1 ||
        needs.HungerDamageDivisor < 1 ||
        needs.ThirstDamageDivisor < 1;

    private sealed class CivilizationCatalogDocument
    {
        public int SchemaVersion { get; init; }
        public List<CivilizationDefinition> Civilizations { get; init; } = [];
    }
}
