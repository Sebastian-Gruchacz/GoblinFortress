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

public sealed record CivilizationVitalsDefinition(
    int MaximumHealth,
    int MaximumMana = 0);

public sealed record CivilizationCombatDefinition(
    int MinimumMeleeDamage,
    int MeleeDamageVariance);

public sealed record CivilizationPerceptionDefinition(
    int DayVisionRadius,
    int NightVisionRadius,
    int StructureVisionRadius);

public sealed record CivilizationSpatialBehaviorDefinition(
    int MovementIntervalTicks,
    int ActivityRadius,
    int MaximumExplorers);

public sealed record CivilizationNeedsDefinition(
    int MaximumHunger,
    int HungerPerTick,
    int EatThreshold,
    int FoodSeekThreshold,
    int CriticalHungerThreshold,
    int StarvationHungerThreshold,
    int StarvationDamagePerTick,
    int MaximumThirst,
    int ThirstPerTick,
    int DrinkThreshold,
    int DehydrationThirstThreshold,
    int DehydrationDamagePerTick,
    int MaximumFatigue,
    int FatiguePerTick,
    int RestThreshold);

public sealed record CivilizationPopulationNeedsDefinition(
    int MaximumNeed,
    int DailyHungerIncrease,
    int DailyThirstIncrease,
    int MealRelief,
    int DrinkRelief,
    int MaximumFatigue,
    int RestThreshold,
    int WorkFatiguePerMove,
    int DayRestRecoveryPerMove,
    int NightRestRecoveryPerMove,
    int HungerDamageDivisor,
    int ThirstDamageDivisor);

public sealed record CivilizationAgingDefinition(
    int HealthyYears,
    int DeclineMinimumSeasons,
    int DeclineMaximumSeasons,
    int TerminalHealthPermille,
    int InitialMinimumAgeYears,
    int InitialMaximumAgeYearsExclusive);

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
    CivilizationVitalsDefinition? Vitals,
    CivilizationCombatDefinition? Combat,
    CivilizationPerceptionDefinition? Perception,
    CivilizationSpatialBehaviorDefinition? SpatialBehavior,
    CivilizationNeedsDefinition? Needs,
    CivilizationPopulationNeedsDefinition? PopulationNeeds,
    CivilizationAgingDefinition? Aging,
    CivilizationActorGenerationDefinition? ActorGeneration,
    UndergroundCivilizationGenerationDefinition? UndergroundGeneration,
    UndergroundCivilizationBehaviorDefinition? UndergroundBehavior);

public interface ICivilizationCatalog
{
    IReadOnlyList<CivilizationDefinition> All { get; }

    CivilizationDefinition Get(ContentId id);

    CivilizationDefinition Get(CivilizationLegacyRole role);

    CivilizationDefinition Get(UndergroundFactionKind kind);
}
