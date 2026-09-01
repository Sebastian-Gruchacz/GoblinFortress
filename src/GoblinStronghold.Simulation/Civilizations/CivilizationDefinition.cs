using GoblinStronghold.Simulation.ContentPacks;

namespace GoblinStronghold.Simulation.Civilizations;

public enum CivilizationLegacyRole : byte
{
    PlayerGoblins = 1,
    HumanVillage = 2,
    DeepDwarfClan = 3,
}

public sealed record CivilizationIdentityDefinition(
    ContentId SpeciesId,
    ContentId ControllerId,
    ContentId NameGeneratorId);

public sealed record UndergroundCivilizationGenerationDefinition(
    UndergroundFactionKind LegacyKind,
    int FirstLevel,
    int DepthBandSize,
    int PresencePercent,
    int BaseMinimumPopulation,
    int MinimumPopulationPerBand,
    int BaseMaximumPopulationExclusive,
    int MaximumPopulationPerBand,
    int MinimumFighters,
    int FighterPopulationDivisor,
    int ProvisionsPerCapita,
    int OrePerCapitaBandOffset,
    int BaseFortification,
    int FortificationPerBand);

public sealed record UndergroundCivilizationBehaviorDefinition(
    int LowProvisionPopulationMultiplier,
    int ProvisionGatherPopulationDivisor,
    int OreTargetBandOffset,
    int OreGatherPopulationDivisor,
    int ProvisionConsumptionPopulationDivisor,
    int ConflictIntervalDays,
    int ConflictLossFighterDivisor,
    int HostileRelationPercent,
    int WaryRelationPercent);

public sealed record CivilizationDefinition(
    ContentId Id,
    CivilizationLegacyRole? LegacyRole,
    bool PlayerControllable,
    CivilizationIdentityDefinition Identity,
    UndergroundCivilizationGenerationDefinition? UndergroundGeneration,
    UndergroundCivilizationBehaviorDefinition? UndergroundBehavior);

public interface ICivilizationCatalog
{
    IReadOnlyList<CivilizationDefinition> All { get; }

    CivilizationDefinition Get(ContentId id);

    CivilizationDefinition Get(CivilizationLegacyRole role);

    CivilizationDefinition Get(UndergroundFactionKind kind);
}
