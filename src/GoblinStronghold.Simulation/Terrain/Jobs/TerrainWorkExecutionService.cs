using GoblinStronghold.Simulation.Map;

namespace GoblinStronghold.Simulation.Terrain.Jobs;

internal sealed record TerrainWorkExecutionResult(
    WorldChangeEvent WorldChange,
    GridPosition OutputPosition,
    TerrainWorkYield Yield);

internal static class TerrainWorkExecutionService
{
    public static TerrainWorkExecutionResult? TryExecute(
        TerrainModificationDefinition definition,
        WorldMapState world,
        GridPosition target,
        GridPosition actorPosition,
        WorldSeed worldSeed,
        EntityId actorId,
        SimulationTick tick,
        EntityId designationId) => TryExecute(
            definition,
            world,
            target,
            rampDestination: null,
            actorPosition,
            worldSeed,
            actorId,
            tick,
            designationId);

    public static TerrainWorkExecutionResult? TryExecute(
        TerrainModificationDefinition definition,
        WorldMapState world,
        GridPosition target,
        GridPosition? rampDestination,
        GridPosition actorPosition,
        WorldSeed worldSeed,
        EntityId actorId,
        SimulationTick tick,
        EntityId designationId)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(world);

        return definition.LegacyDesignation switch
        {
            WorkDesignationKind.MineRock => TryExcavateRock(
                definition,
                world,
                target,
                actorPosition,
                worldSeed,
                actorId,
                tick,
                designationId),
            WorkDesignationKind.StripFloor => TryStripFloor(
                definition,
                world,
                target,
                actorPosition,
                worldSeed,
                actorId,
                tick,
                designationId),
            WorkDesignationKind.CarveRampDown => TryCarveRamp(
                definition,
                world,
                target,
                rampDestination,
                carveDown: true,
                worldSeed,
                actorId,
                tick,
                designationId),
            WorkDesignationKind.CarveRampUp => TryCarveRamp(
                definition,
                world,
                target,
                rampDestination,
                carveDown: false,
                worldSeed,
                actorId,
                tick,
                designationId),
            _ => throw new ArgumentException(
                $"Terrain modification '{definition.Id}' has no execution service.",
                nameof(definition)),
        };
    }

    private static TerrainWorkExecutionResult? TryExcavateRock(
        TerrainModificationDefinition definition,
        WorldMapState world,
        GridPosition target,
        GridPosition actorPosition,
        WorldSeed worldSeed,
        EntityId actorId,
        SimulationTick tick,
        EntityId designationId)
    {
        if (!world.TryExcavateNaturalSolid(target, tick, out var material, out var change))
        {
            return null;
        }

        return new TerrainWorkExecutionResult(
            change,
            world.IsTerrainTraversable(target) ? target : actorPosition,
            TerrainWorkYieldPolicy.Create(
                definition,
                material,
                worldSeed,
                actorId,
                tick,
                designationId));
    }

    private static TerrainWorkExecutionResult? TryStripFloor(
        TerrainModificationDefinition definition,
        WorldMapState world,
        GridPosition target,
        GridPosition actorPosition,
        WorldSeed worldSeed,
        EntityId actorId,
        SimulationTick tick,
        EntityId designationId)
    {
        var material = world.GetFloorStrippingCell(target);
        if (!world.TryStripFloor(target, tick, out var resource, out var variant, out var change))
        {
            return null;
        }

        var generated = TerrainWorkYieldPolicy.Create(
            definition,
            material,
            worldSeed,
            actorId,
            tick,
            designationId);
        var quantity = generated.Stacks.Sum(stack => stack.Quantity);
        return new TerrainWorkExecutionResult(
            change,
            actorPosition,
            new TerrainWorkYield(
                [new TerrainYieldStack(resource, variant, quantity)],
                generated.BuildingExperience));
    }

    private static TerrainWorkExecutionResult? TryCarveRamp(
        TerrainModificationDefinition definition,
        WorldMapState world,
        GridPosition target,
        GridPosition? rampDestination,
        bool carveDown,
        WorldSeed worldSeed,
        EntityId actorId,
        SimulationTick tick,
        EntityId designationId)
    {
        var carved = rampDestination is { } destination
            ? world.TryCarveNaturalRamp(
                carveDown ? target : destination,
                carveDown ? destination : target,
                carveDown,
                tick,
                out var material,
                out var change)
            : world.TryCarveNaturalRamp(
                carveDown ? target : target with { Z = target.Z + 1 },
                carveDown ? target with { Z = target.Z - 1 } : target,
                carveDown,
                tick,
                out material,
                out change);
        if (!carved)
        {
            return null;
        }

        return new TerrainWorkExecutionResult(
            change,
            target,
            TerrainWorkYieldPolicy.Create(
                definition,
                material with { Deposit = MineralDepositKind.None },
                worldSeed,
                actorId,
                tick,
                designationId));
    }
}
