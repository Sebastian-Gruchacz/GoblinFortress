using System.Text.Json;
using GoblinStronghold.Simulation.Civilizations.Polities;
using GoblinStronghold.Simulation.Construction;
using GoblinStronghold.Simulation.Equipment;
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
    public const int FoodPreservationMigrationVersion = 77;
    public const int CaveFloraHarvestMigrationVersion = 78;
    public const int ReportedCleaningMigrationVersion = 79;
    public const int ConstructionToolsMigrationVersion = 80;
    public const int ConstructionHammerMigrationVersion = 81;
    public const int ConstructionToolLevelsMigrationVersion = 82;
    public const int ConstructionToolFunctionsMigrationVersion = 83;
    public const int GoblinManaMigrationVersion = 84;
    public const int CurrentVersion = 85;

    public static bool IsLoadableVersion(int version) =>
        version is SurfaceGrimeMigrationVersion or
            LocationProfileMigrationVersion or
            RiverModeMigrationVersion or
            ActorSexMigrationVersion or
            PolityIdMigrationVersion or
            RoadModeMigrationVersion or
            WorkTypePrioritiesMigrationVersion or
            FoodPreservationMigrationVersion or
            CaveFloraHarvestMigrationVersion or
            ReportedCleaningMigrationVersion or
            ConstructionToolsMigrationVersion or
            ConstructionHammerMigrationVersion or
            ConstructionToolLevelsMigrationVersion or
            ConstructionToolFunctionsMigrationVersion or
            GoblinManaMigrationVersion or
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
                $"{SimulationSaveFormat.FoodPreservationMigrationVersion} and " +
                $"{SimulationSaveFormat.CaveFloraHarvestMigrationVersion} and " +
                $"{SimulationSaveFormat.ReportedCleaningMigrationVersion} and " +
                $"{SimulationSaveFormat.ConstructionToolsMigrationVersion} and " +
                $"{SimulationSaveFormat.ConstructionHammerMigrationVersion} and " +
                $"{SimulationSaveFormat.ConstructionToolLevelsMigrationVersion} and " +
                $"{SimulationSaveFormat.ConstructionToolFunctionsMigrationVersion} and " +
                $"{SimulationSaveFormat.GoblinManaMigrationVersion} and " +
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
        if (save.FormatVersion == SimulationSaveFormat.FoodPreservationMigrationVersion)
        {
            MigrateFoodPreservation(save);
        }
        if (save.FormatVersion == SimulationSaveFormat.CaveFloraHarvestMigrationVersion)
        {
            MigrateCaveFloraHarvest(save);
        }
        if (save.FormatVersion == SimulationSaveFormat.ReportedCleaningMigrationVersion)
        {
            MigrateReportedCleaning(save);
        }
        if (save.FormatVersion == SimulationSaveFormat.ConstructionToolsMigrationVersion)
        {
            MigrateConstructionTools(save);
        }
        if (save.FormatVersion == SimulationSaveFormat.ConstructionHammerMigrationVersion)
        {
            MigrateConstructionHammer(save);
        }
        if (save.FormatVersion == SimulationSaveFormat.ConstructionToolLevelsMigrationVersion)
        {
            MigrateConstructionToolLevels(save);
        }
        if (save.FormatVersion == SimulationSaveFormat.ConstructionToolFunctionsMigrationVersion)
        {
            MigrateConstructionToolFunctions(save);
        }
        if (save.FormatVersion == SimulationSaveFormat.GoblinManaMigrationVersion)
        {
            MigrateGoblinMana(save);
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
        save.FormatVersion = SimulationSaveFormat.FoodPreservationMigrationVersion;
    }

    private static void MigrateFoodPreservation(SimulationSaveModel save)
    {
        save.CompostNutrients = 0;
        save.FormatVersion = SimulationSaveFormat.CaveFloraHarvestMigrationVersion;
    }

    private static void MigrateCaveFloraHarvest(SimulationSaveModel save)
    {
        save.HarvestedCaveFlora.Clear();
        save.FormatVersion = SimulationSaveFormat.ReportedCleaningMigrationVersion;
    }

    private static void MigrateReportedCleaning(SimulationSaveModel save)
    {
        save.ReportedCleaningPositions = save.Actors
            .Select(actor => new GridPositionSaveModel
            {
                X = actor.X,
                Y = actor.Y,
                Z = actor.Z,
            })
            .DistinctBy(position => (position.X, position.Y, position.Z))
            .ToList();
        save.FormatVersion = SimulationSaveFormat.ConstructionToolsMigrationVersion;
    }

    private static void MigrateConstructionTools(SimulationSaveModel save)
    {
        SynchronizeConstructionCapabilities(save);
        save.FormatVersion = SimulationSaveFormat.ConstructionHammerMigrationVersion;
    }

    private static void MigrateConstructionHammer(SimulationSaveModel save)
    {
        SynchronizeConstructionCapabilities(save);
        save.FormatVersion = SimulationSaveFormat.ConstructionToolLevelsMigrationVersion;
    }

    private static void MigrateConstructionToolLevels(SimulationSaveModel save)
    {
        SynchronizeConstructionCapabilities(save);
        save.FormatVersion = SimulationSaveFormat.ConstructionToolFunctionsMigrationVersion;
    }

    private static void MigrateConstructionToolFunctions(SimulationSaveModel save)
    {
        SynchronizeConstructionCapabilities(save);
        save.FormatVersion = SimulationSaveFormat.GoblinManaMigrationVersion;
    }

    private static void MigrateGoblinMana(SimulationSaveModel save)
    {
        foreach (var actor in save.Actors)
        {
            actor.Mana = 0;
        }
        save.FormatVersion = SimulationSaveFormat.CurrentVersion;
    }

    private static void SynchronizeConstructionCapabilities(SimulationSaveModel save)
    {
        foreach (var site in save.ConstructionSites)
        {
            if (!Enum.IsDefined(site.Kind))
            {
                continue;
            }

            var capabilities = ConstructionBlueprintDefinitions.Get(site.Kind).Capabilities;
            site.RequiredSkills = capabilities.RequiredSkills;
            site.MinimumBuildingLevel = capabilities.MinimumBuildingLevel;
            site.RequiredEquipment = capabilities.RequiredEquipment;
            site.RequiredToolFunction = capabilities.RequiredToolFunction;
            site.MinimumToolLevel = capabilities.MinimumToolLevel;
        }
    }
}
