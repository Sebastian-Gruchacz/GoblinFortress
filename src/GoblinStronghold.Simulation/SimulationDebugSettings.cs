namespace GoblinStronghold.Simulation;

public readonly record struct SimulationDebugSettings(
    bool RevealFogFromNonPlayerUnits)
{
    public static SimulationDebugSettings Disabled { get; } = new(
        RevealFogFromNonPlayerUnits: false);

    public static SimulationDebugSettings ForCurrentBuild { get; } = Disabled;
}
