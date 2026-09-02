using System.Text.Json;
using GoblinStronghold.Simulation.Civilizations.Polities;
using GoblinStronghold.Simulation.Map;
using GoblinStronghold.Simulation.Map.Generation;

namespace GoblinStronghold.Simulation;

public static class SimulationSaveFormat
{
    public const int SurfaceGrimeMigrationVersion = 70;
    public const int LocationProfileMigrationVersion = 71;
    public const int RiverModeMigrationVersion = 72;
    public const int ActorSexMigrationVersion = 73;
    public const int PolityIdMigrationVersion = 74;
    public const int RoadModeMigrationVersion = 75;
    public const int WorkTypePrioritiesMigrationVersion = 76;
    public const int CurrentVersion = 77;

    public static bool IsLoadableVersion(int version) =>
        version is SurfaceGrimeMigrationVersion or
            LocationProfileMigrationVersion or
            RiverModeMigrationVersion or
            ActorSexMigrationVersion or
            PolityIdMigrationVersion or
            RoadModeMigrationVersion or
            WorkTypePrioritiesMigrationVersion or
            CurrentVersion;
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
                $"{SimulationSaveFormat.LocationProfileMigrationVersion} and " +
                $"{SimulationSaveFormat.RiverModeMigrationVersion} and " +
                $"{SimulationSaveFormat.ActorSexMigrationVersion} and " +
                $"{SimulationSaveFormat.PolityIdMigrationVersion} and " +
                $"{SimulationSaveFormat.RoadModeMigrationVersion} and " +
                $"{SimulationSaveFormat.WorkTypePrioritiesMigrationVersion} and " +
                $"{SimulationSaveFormat.CurrentVersion}.");
        }

        if (save.FormatVersion == SimulationSaveFormat.SurfaceGrimeMigrationVersion)
        {
            MigrateSurfaceGrime(save);
        }
        if (save.FormatVersion == SimulationSaveFormat.LocationProfileMigrationVersion)
        {
            MigrateLocationProfile(save);
        }
        if (save.FormatVersion == SimulationSaveFormat.RiverModeMigrationVersion)
        {
            MigrateRiverMode(save);
        }
        if (save.FormatVersion == SimulationSaveFormat.ActorSexMigrationVersion)
        {
            MigrateActorSex(save);
        }
        if (save.FormatVersion == SimulationSaveFormat.PolityIdMigrationVersion)
        {
            MigratePolityIds(save);
        }
        if (save.FormatVersion == SimulationSaveFormat.RoadModeMigrationVersion)
        {
            MigrateRoadMode(save);
        }
        if (save.FormatVersion == SimulationSaveFormat.WorkTypePrioritiesMigrationVersion)
        {
            MigrateWorkTypePriorities(save);
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
        save.FormatVersion = SimulationSaveFormat.LocationProfileMigrationVersion;
    }

    private static void MigrateLocationProfile(SimulationSaveModel save)
    {
        save.MapProfileId = SwampMapGenerator.DefaultProfileId.Value;
        save.FormatVersion = SimulationSaveFormat.RiverModeMigrationVersion;
    }

    private static void MigrateRiverMode(SimulationSaveModel save)
    {
        save.MapRiverMode = RiverGenerationMode.SingleChannel;
        save.FormatVersion = SimulationSaveFormat.ActorSexMigrationVersion;
    }

    private static void MigrateActorSex(SimulationSaveModel save)
    {
        foreach (var actor in save.Actors)
        {
            actor.Sex = ActorSex.Sexless;
        }
        foreach (var villager in save.HumanVillage.Villagers)
        {
            villager.Sex = villager.Id % 2 == 0 ? ActorSex.Male : ActorSex.Female;
        }
        save.FormatVersion = SimulationSaveFormat.PolityIdMigrationVersion;
    }

    private static void MigratePolityIds(SimulationSaveModel save)
    {
        save.PlayerPolityId = CorePolityIds.PlayerTribe.Value;
        save.HumanVillage.PolityId = CorePolityIds.HumanVillage.Value;
        foreach (var actor in save.Actors)
        {
            actor.PolityId = CorePolityIds.PlayerTribe.Value;
        }
        foreach (var faction in save.UndergroundFactions)
        {
            faction.PolityId = CorePolityIds.CaveDwarfClan(faction.Id).Value;
        }
        save.FormatVersion = SimulationSaveFormat.RoadModeMigrationVersion;
    }

    private static void MigrateRoadMode(SimulationSaveModel save)
    {
        save.MapRoadMode = RoadGenerationMode.Absent;
        save.FormatVersion = SimulationSaveFormat.WorkTypePrioritiesMigrationVersion;
    }

    private static void MigrateWorkTypePriorities(SimulationSaveModel save)
    {
        save.WorkTypePriorities.Clear();
        save.DiscardMisplacedLegacySurfaceGrime = true;
        save.FormatVersion = SimulationSaveFormat.CurrentVersion;
    }
}
