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
                carveDown: true,
                worldSeed,
                actorId,
                tick,
                designationId),
            WorkDesignationKind.CarveRampUp => TryCarveRamp(
                definition,
                world,
                target,
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
        bool carveDown,
        WorldSeed worldSeed,
        EntityId actorId,
        SimulationTick tick,
        EntityId designationId)
    {
        if (!world.TryCarveVerticalRamp(target, carveDown, tick, out var rock, out var change))
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
