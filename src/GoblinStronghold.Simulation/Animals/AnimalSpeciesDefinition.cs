using GoblinStronghold.Simulation.ContentPacks;
using GoblinStronghold.Simulation.Resources;

namespace GoblinStronghold.Simulation.Animals;

public enum AnimalHabitatKind : byte
{
    FertileGround = 1,
    Wetland = 2,
    Cave = 3,
}

public enum AnimalDisposition : byte
{
    Passive = 1,
    Territorial = 2,
    Aggressive = 3,
}

public enum AnimalEcologyProfile : byte
{
    MarshHare = 1,
    SwampBoar = 2,
    CaveSpider = 3,
}

public enum AnimalEnemySelectorKind : byte
{
    Species = 1,
    EntityType = 2,
    Group = 3,
}

public enum AnimalSpawnMode : byte
{
    InitialSingleLevel = 1,
    InitialEachDepth = 2,
    MaintainEachDepth = 3,
}

public sealed record AnimalVitalStatistics(
    int MaximumHealth,
    int MaximumFatigue,
    int MovementFatigue,
    int RestRecovery);

public sealed record AnimalHabitatDefinition(
    AnimalHabitatKind Kind,
    int MinimumDepthBelowSurface);

public sealed record AnimalAttackDefinition(
    int BaseDamage,
    int DamagePerDepth);

public sealed record AnimalByproductYield(
    ResourceKind Resource,
    ResourceVariant Variant,
    int Quantity);

public sealed record AnimalEnemySelector(
    AnimalEnemySelectorKind Kind,
    ContentId Id);

public sealed record AnimalBehaviorDefinition(
    ContentId ModelId,
    AnimalDisposition Disposition,
    int Aggression,
    int DetectionRadius,
    int ForageHungerThreshold,
    int StarvationHungerThreshold,
    int RoamingInterval,
    IReadOnlyList<AnimalEnemySelector> Enemies);

public sealed record AnimalSpawnDefinition(
    AnimalSpawnMode Mode,
    int Order,
    int MinimumDepth,
    int? MaximumDepth,
    int MinimumPopulation,
    int MapCellsPerAnimal,
    bool ScalePopulationWithDepth,
    int? PopulationIncreaseDepth,
    int PopulationIncrease);

public sealed record AnimalVisualDefinition(
    ContentId RendererId,
    ContentId? AtlasId,
    ContentId? SpriteId,
    IReadOnlyDictionary<string, string> Palette);

public sealed record AnimalHarvestDefinition(
    int RawMeat,
    int Hide,
    int Bone,
    int ForagingExperience,
    int HunterMeleeDamage,
    IReadOnlyList<AnimalByproductYield> Byproducts);

public sealed record AnimalSpeciesDefinition(
    ContentId Id,
    AnimalKind LegacyKind,
    AnimalVitalStatistics Vitals,
    AnimalHabitatDefinition Habitat,
    AnimalBehaviorDefinition Behavior,
    AnimalSpawnDefinition Spawn,
    AnimalEcologyProfile EcologyProfile,
    AnimalAttackDefinition Attack,
    AnimalHarvestDefinition Harvest,
    int DebugVisionRadius,
    AnimalVisualDefinition Visual);

public interface IAnimalSpeciesCatalog
{
    IReadOnlyList<AnimalSpeciesDefinition> All { get; }

    AnimalSpeciesDefinition Get(AnimalKind kind);

    AnimalSpeciesDefinition Get(ContentId id);
}
