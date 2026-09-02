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
        if (!world.TryExcavateRock(target, tick, out var rock, out var deposit, out var change))
        {
            return null;
        }

        return new TerrainWorkExecutionResult(
            change,
            world.IsTerrainTraversable(target) ? target : actorPosition,
            TerrainWorkYieldPolicy.Create(
                definition,
                rock,
                deposit,
                worldSeed,
                actorId,
                tick,
                designationId));
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
            ? world.TryCarveRamp(
                carveDown ? target : destination,
                carveDown ? destination : target,
                carveDown,
                tick,
                out var rock,
                out var change)
            : world.TryCarveVerticalRamp(
                target,
                carveDown,
                tick,
                out rock,
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
                rock,
                MineralDepositKind.None,
                worldSeed,
                actorId,
                tick,
                designationId));
    }
}
