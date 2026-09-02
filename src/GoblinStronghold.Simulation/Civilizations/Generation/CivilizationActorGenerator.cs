namespace GoblinStronghold.Simulation.Civilizations;

internal sealed class CivilizationActorGenerator(
    WorldSeed worldSeed,
    CivilizationActorGenerationDefinition definition)
{
    public GoblinSkill CreateSkills(EntityId actorId) =>
        definition.SkillSampleKeys.Aggregate(
            GoblinSkill.None,
            (skills, sampleKey) => skills | definition.SkillPool[SampleIndex(
                actorId,
                sampleKey,
                definition.SkillPool.Count)]);

    public GoblinTrait CreateTraits(EntityId actorId) =>
        definition.TraitSampleKeys.Aggregate(
            GoblinTrait.None,
            (traits, sampleKey) => traits | definition.TraitPool[SampleIndex(
                actorId,
                sampleKey,
                definition.TraitPool.Count)]);

    public PersonalEquipment CreateEquipment(EntityId actorId)
    {
        var roll = DeterministicRandom.NextInt(
            worldSeed,
            definition.RandomDomain,
            actorId,
            SimulationTick.Zero,
            definition.OptionalEquipmentSampleKey,
            minimumInclusive: 0,
            definition.OptionalEquipmentRollMaximumExclusive);
        return roll == definition.OptionalEquipmentSuccessValue
            ? definition.GuaranteedEquipment | definition.OptionalEquipment
            : definition.GuaranteedEquipment;
    }

    public GoblinWorkPreferences CreateWorkPreferences(EntityId actorId) => new(
        CreateWorkPreference(actorId, definition.WorkPreferenceSampleKeys[0]),
        CreateWorkPreference(actorId, definition.WorkPreferenceSampleKeys[1]),
        CreateWorkPreference(actorId, definition.WorkPreferenceSampleKeys[2]));

    private int CreateWorkPreference(EntityId actorId, ulong sampleKey) =>
        DeterministicRandom.NextInt(
            worldSeed,
            definition.RandomDomain,
            actorId,
            SimulationTick.Zero,
            sampleKey,
            definition.WorkPreferenceMinimum,
            definition.WorkPreferenceMaximum + 1);

    private int SampleIndex(EntityId actorId, ulong sampleKey, int count) =>
        DeterministicRandom.NextInt(
            worldSeed,
            definition.RandomDomain,
            actorId,
            SimulationTick.Zero,
            sampleKey,
            minimumInclusive: 0,
            count);
}
