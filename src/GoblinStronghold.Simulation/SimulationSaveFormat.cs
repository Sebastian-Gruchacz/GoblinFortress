using System.Text.Json;

namespace GoblinStronghold.Simulation;

public static class SimulationSaveFormat
{
    public const int SurfaceGrimeMigrationVersion = 70;
    public const int CurrentVersion = 71;

    public static bool IsLoadableVersion(int version) =>
        version is SurfaceGrimeMigrationVersion or CurrentVersion;
}

internal static class SimulationSaveReader
{
    public static SimulationSaveModel ReadExact(
        string saveJson,
        JsonSerializerOptions options)
    {
        var save = JsonSerializer.Deserialize<SimulationSaveModel>(saveJson, options)
            ?? throw new InvalidDataException("The save does not contain simulation state.");
        if (!SimulationSaveFormat.IsLoadableVersion(save.FormatVersion))
        {
            throw new InvalidDataException(
                $"Save format version {save.FormatVersion} is obsolete or incompatible; " +
                $"this pre-release build accepts versions " +
                $"{SimulationSaveFormat.SurfaceGrimeMigrationVersion} and " +
                $"{SimulationSaveFormat.CurrentVersion}.");
        }

        if (save.FormatVersion == SimulationSaveFormat.SurfaceGrimeMigrationVersion)
        {
            MigrateSurfaceGrime(save);
        }

        return save;
    }

    private static void MigrateSurfaceGrime(SimulationSaveModel save)
    {
        save.SurfaceGrime.Clear();
        foreach (var actor in save.Actors)
        {
            actor.CarriedGrime = 0;
        }
        foreach (var animal in save.Animals ?? [])
        {
            animal.CarriedGrime = 0;
        }
        foreach (var villager in save.HumanVillage.Villagers)
        {
            villager.CarriedGrime = 0;
        }
        save.FormatVersion = SimulationSaveFormat.CurrentVersion;
    }
}
