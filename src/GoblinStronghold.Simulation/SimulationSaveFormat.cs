using System.Text.Json;

namespace GoblinStronghold.Simulation;

public static class SimulationSaveFormat
{
    public const int CurrentVersion = 70;
}

internal static class SimulationSaveReader
{
    public static SimulationSaveModel ReadExact(
        string saveJson,
        JsonSerializerOptions options)
    {
        var save = JsonSerializer.Deserialize<SimulationSaveModel>(saveJson, options)
            ?? throw new InvalidDataException("The save does not contain simulation state.");
        if (save.FormatVersion != SimulationSaveFormat.CurrentVersion)
        {
            throw new InvalidDataException(
                $"Save format version {save.FormatVersion} is obsolete or incompatible; " +
                $"this pre-release build accepts only version " +
                $"{SimulationSaveFormat.CurrentVersion}.");
        }

        return save;
    }
}
