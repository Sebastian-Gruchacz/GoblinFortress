using GoblinStronghold.Simulation.Map;

namespace GoblinStronghold.Simulation.Terrain;

public static class TerrainModificationCommandFactory
{
    public static SimulationCommand CreateDesignation(
        TerrainModificationDefinition definition,
        SimulationTick executeAt,
        ulong sequence,
        GridPosition start,
        GridPosition end)
    {
        ArgumentNullException.ThrowIfNull(definition);

        return definition.LegacyDesignation switch
        {
            WorkDesignationKind.MineRock =>
                SimulationCommand.DesignateRockMining(executeAt, sequence, start, end),
            WorkDesignationKind.CarveRampDown =>
                SimulationCommand.DesignateRampDown(executeAt, sequence, start),
            WorkDesignationKind.CarveRampUp =>
                SimulationCommand.DesignateRampUp(executeAt, sequence, start),
            _ => throw new ArgumentException(
                $"Terrain modification '{definition.Id}' has no command adapter.",
                nameof(definition)),
        };
    }
}
