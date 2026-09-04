using GoblinStronghold.Simulation.Map;
using GoblinStronghold.Simulation.Resources;

namespace GoblinStronghold.Simulation.Terrain.Jobs;

internal readonly record struct TerrainYieldStack(
    ResourceKind Resource,
    ResourceVariant Variant,
    int Quantity);

internal sealed record TerrainWorkYield(
    IReadOnlyList<TerrainYieldStack> Stacks,
    int BuildingExperience);

internal static class TerrainWorkYieldPolicy
{
    private const ulong DepositSampleKey = 0x4F52454445504F53UL;
    private const ulong RampStoneSampleKey = 0x52414D5053544F4EUL;

    public static TerrainWorkYield Create(
        TerrainModificationDefinition definition,
        RockKind rock,
        MineralDepositKind deposit,
        WorldSeed worldSeed,
        EntityId actorId,
        SimulationTick tick,
        EntityId designationId) => Create(
        definition,
        new CaveCell(rock, CaveCellKind.SolidRock, deposit),
        worldSeed,
        actorId,
        tick,
        designationId);

    public static TerrainWorkYield Create(
        TerrainModificationDefinition definition,
        CaveCell material,
        WorldSeed worldSeed,
        EntityId actorId,
        SimulationTick tick,
        EntityId designationId)
    {
        ArgumentNullException.ThrowIfNull(definition);

        if (material.IsLooseMaterial)
        {
            return CreateLooseMaterialYield(
                definition.Work.Yield,
                material.LooseMaterial,
                worldSeed,
                actorId,
                tick,
                designationId);
        }

        return definition.LegacyDesignation switch
        {
            WorkDesignationKind.MineRock => CreateMiningYield(
                definition.Work.Yield,
                material.Rock,
                material.Deposit,
                worldSeed,
                actorId,
                tick,
                designationId),
            WorkDesignationKind.CarveRampDown or WorkDesignationKind.CarveRampUp =>
                CreateRampYield(
                    definition.Work.Yield,
                    material.Rock,
                    worldSeed,
                    actorId,
                    tick,
                    designationId),
            _ => throw new ArgumentException(
                $"Terrain modification '{definition.Id}' has no yield policy.",
                nameof(definition)),
        };
    }

    private static TerrainWorkYield CreateLooseMaterialYield(
        TerrainYieldDefinition yieldDefinition,
        LooseMaterialKind material,
        WorldSeed worldSeed,
        EntityId actorId,
        SimulationTick tick,
        EntityId designationId)
    {
        var quantity = DeterministicRandom.NextInt(
            worldSeed,
            RandomDomain.Stone,
            actorId,
            tick,
            sampleKey: designationId.Value,
            minimumInclusive: yieldDefinition.MinimumQuantity,
            maximumExclusive: yieldDefinition.MaximumQuantityExclusive);
        var (resource, variant) = material switch
        {
            LooseMaterialKind.Soil => (ResourceKind.Earth, ResourceVariant.Soil),
            LooseMaterialKind.Sand => (ResourceKind.Sand, ResourceVariant.Sand),
            _ => throw new ArgumentOutOfRangeException(nameof(material)),
        };
        return new TerrainWorkYield(
            [new TerrainYieldStack(resource, variant, quantity)],
            Math.Max(
                yieldDefinition.MinimumBuildingExperience,
                quantity * yieldDefinition.BuildingExperiencePerUnit));
    }

    private static TerrainWorkYield CreateMiningYield(
        TerrainYieldDefinition yieldDefinition,
        RockKind rock,
        MineralDepositKind deposit,
        WorldSeed worldSeed,
        EntityId actorId,
        SimulationTick tick,
        EntityId designationId)
    {
        var stoneQuantity = DeterministicRandom.NextInt(
            worldSeed,
            RandomDomain.Stone,
            actorId,
            tick,
            sampleKey: designationId.Value,
            minimumInclusive: yieldDefinition.MinimumQuantity,
            maximumExclusive: yieldDefinition.MaximumQuantityExclusive);
        var stacks = new List<TerrainYieldStack>(deposit == MineralDepositKind.None ? 1 : 2)
        {
            new(
                yieldDefinition.Resource,
                ResolveVariant(yieldDefinition, rock),
                stoneQuantity),
        };
        if (deposit != MineralDepositKind.None)
        {
            var depositDefinition = yieldDefinition.Deposits[deposit];
            stacks.Add(new TerrainYieldStack(
                depositDefinition.Resource,
                depositDefinition.Variant,
                DeterministicRandom.NextInt(
                    worldSeed,
                    RandomDomain.Stone,
                    actorId,
                    tick,
                    sampleKey: designationId.Value ^ DepositSampleKey,
                    minimumInclusive: depositDefinition.MinimumQuantity,
                    maximumExclusive: depositDefinition.MaximumQuantityExclusive)));
        }

        return new TerrainWorkYield(
            stacks,
            Math.Max(
                yieldDefinition.MinimumBuildingExperience,
                stoneQuantity * yieldDefinition.BuildingExperiencePerUnit));
    }

    private static TerrainWorkYield CreateRampYield(
        TerrainYieldDefinition yieldDefinition,
        RockKind rock,
        WorldSeed worldSeed,
        EntityId actorId,
        SimulationTick tick,
        EntityId designationId)
    {
        var quantity = DeterministicRandom.NextInt(
            worldSeed,
            RandomDomain.Stone,
            actorId,
            tick,
            sampleKey: designationId.Value ^ RampStoneSampleKey,
            minimumInclusive: yieldDefinition.MinimumQuantity,
            maximumExclusive: yieldDefinition.MaximumQuantityExclusive);
        return new TerrainWorkYield(
            [new TerrainYieldStack(
                yieldDefinition.Resource,
                ResolveVariant(yieldDefinition, rock),
                quantity)],
            Math.Max(
                yieldDefinition.MinimumBuildingExperience,
                quantity * yieldDefinition.BuildingExperiencePerUnit));
    }

    private static ResourceVariant ResolveVariant(
        TerrainYieldDefinition definition,
        RockKind rock) => definition.VariantFromRock ? StoneVariant(rock) : definition.Variant;

    private static ResourceVariant StoneVariant(RockKind rock) => rock switch
    {
        RockKind.Granite => ResourceVariant.Granite,
        RockKind.Basalt => ResourceVariant.Basalt,
        RockKind.Obsidian => ResourceVariant.Obsidian,
        _ => ResourceVariant.Sandstone,
    };
}
