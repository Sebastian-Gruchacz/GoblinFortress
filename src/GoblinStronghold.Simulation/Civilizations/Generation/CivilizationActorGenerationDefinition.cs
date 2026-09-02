namespace GoblinStronghold.Simulation.Civilizations;

public sealed record CivilizationActorGenerationDefinition
{
    public RandomDomain RandomDomain { get; init; } = RandomDomain.GoblinIdentity;

    public List<GoblinSkill> SkillPool { get; init; } = [];

    public List<ulong> SkillSampleKeys { get; init; } = [];

    public List<GoblinTrait> TraitPool { get; init; } = [];

    public List<ulong> TraitSampleKeys { get; init; } = [];

    public PersonalEquipment GuaranteedEquipment { get; init; }

    public PersonalEquipment OptionalEquipment { get; init; }

    public ulong OptionalEquipmentSampleKey { get; init; }

    public int OptionalEquipmentRollMaximumExclusive { get; init; }

    public int OptionalEquipmentSuccessValue { get; init; }

    public int WorkPreferenceMinimum { get; init; }

    public int WorkPreferenceMaximum { get; init; }

    public List<ulong> WorkPreferenceSampleKeys { get; init; } = [];
}
