using System.Text.Json;
using GoblinStronghold.Simulation.Map;
using GoblinStronghold.Simulation.Resources;

namespace GoblinStronghold.Simulation;

internal interface ISimulationSaveMigration
{
    int SourceVersion { get; }

    int TargetVersion { get; }

    void MigrateModel(SimulationSaveModel save);

    void MigrateWorldState(SimulationSaveModel save, WorldMapState world);
}

internal sealed class SimulationSaveLoadPlan(
    SimulationSaveModel save,
    IReadOnlyList<ISimulationSaveMigration> migrations)
{
    public SimulationSaveModel Save { get; } = save;

    public void MigrateWorldState(WorldMapState world)
    {
        foreach (var migration in migrations)
        {
            migration.MigrateWorldState(Save, world);
        }
    }
}

internal static class SimulationSaveMigrationManager
{
    public const int CurrentVersion = 36;

    private static readonly ISimulationSaveMigration[] Migrations =
    [
        new SimulationSaveMigration30To31(),
        new SimulationSaveMigration31To32(),
        new SimulationSaveMigration32To33(),
        new SimulationSaveMigration33To34(),
        new SimulationSaveMigration34To35(),
        new SimulationSaveMigration35To36(),
    ];

    public static SimulationSaveLoadPlan Prepare(
        string saveJson,
        JsonSerializerOptions options)
    {
        var save = JsonSerializer.Deserialize<SimulationSaveModel>(saveJson, options)
            ?? throw new InvalidDataException("The save does not contain simulation state.");
        if (save.FormatVersion > CurrentVersion)
        {
            throw new InvalidDataException(
                $"Save format version {save.FormatVersion} is newer than supported version " +
                $"{CurrentVersion}.");
        }

        var applied = new List<ISimulationSaveMigration>();
        while (save.FormatVersion < CurrentVersion)
        {
            var migration = Migrations.SingleOrDefault(candidate =>
                candidate.SourceVersion == save.FormatVersion)
                ?? throw new InvalidDataException(
                    $"No save migration is available from format version {save.FormatVersion}.");
            if (migration.TargetVersion <= migration.SourceVersion)
            {
                throw new InvalidOperationException("A save migration must advance the format version.");
            }

            migration.MigrateModel(save);
            save.FormatVersion = migration.TargetVersion;
            applied.Add(migration);
        }

        return new SimulationSaveLoadPlan(save, applied);
    }
}

internal sealed class SimulationSaveMigration35To36 : ISimulationSaveMigration
{
    public int SourceVersion => 35;

    public int TargetVersion => 36;

    public void MigrateModel(SimulationSaveModel save)
    {
        foreach (var animal in (save.Animals ?? []).Where(animal => Enum.IsDefined(animal.Kind)))
        {
            animal.Fatigue = Math.Min(
                animal.Fatigue,
                SimulationEngine.MaximumAnimalFatigue(animal.Kind));
        }
    }

    public void MigrateWorldState(SimulationSaveModel save, WorldMapState world)
    {
    }
}

internal sealed class SimulationSaveMigration34To35 : ISimulationSaveMigration
{
    public int SourceVersion => 34;

    public int TargetVersion => 35;

    public void MigrateModel(SimulationSaveModel save)
    {
    }

    public void MigrateWorldState(SimulationSaveModel save, WorldMapState world)
    {
    }
}

internal sealed class SimulationSaveMigration33To34 : ISimulationSaveMigration
{
    public int SourceVersion => 33;

    public int TargetVersion => 34;

    public void MigrateModel(SimulationSaveModel save)
    {
    }

    public void MigrateWorldState(SimulationSaveModel save, WorldMapState world)
    {
    }
}

internal sealed class SimulationSaveMigration32To33 : ISimulationSaveMigration
{
    public int SourceVersion => 32;

    public int TargetVersion => 33;

    public void MigrateModel(SimulationSaveModel save)
    {
    }

    public void MigrateWorldState(SimulationSaveModel save, WorldMapState world)
    {
    }
}

internal sealed class SimulationSaveMigration31To32 : ISimulationSaveMigration
{
    public int SourceVersion => 31;

    public int TargetVersion => 32;

    public void MigrateModel(SimulationSaveModel save)
    {
    }

    public void MigrateWorldState(SimulationSaveModel save, WorldMapState world)
    {
    }
}

internal sealed class SimulationSaveMigration30To31 : ISimulationSaveMigration
{
    public int SourceVersion => 30;

    public int TargetVersion => 31;

    public void MigrateModel(SimulationSaveModel save)
    {
        var presentResources = save.ResourcePriorities
            .Select(priority => priority.Resource)
            .ToHashSet();
        foreach (var resource in Enum.GetValues<ResourceKind>().Where(resource =>
                     resource != ResourceKind.Any && !presentResources.Contains(resource)))
        {
            save.ResourcePriorities.Add(new ResourcePrioritySaveModel
            {
                Resource = resource,
                Priority = StoragePriority.Normal,
            });
        }
    }

    public void MigrateWorldState(SimulationSaveModel save, WorldMapState world)
    {
        foreach (var actor in save.Actors)
        {
            var savedPosition = new GridPosition(actor.X, actor.Y, actor.Z);
            if (world.IsTerrainTraversable(savedPosition))
            {
                continue;
            }

            var migratedPosition = FindNearestTraversablePosition(world, savedPosition)
                ?? throw new InvalidDataException(
                    $"Actor {actor.Id} cannot be moved out of an obsolete blocked position.");
            actor.X = migratedPosition.X;
            actor.Y = migratedPosition.Y;
            actor.Z = migratedPosition.Z;
            actor.JobKind = ActorJobKind.None;
            actor.JobPhase = ActorJobPhase.None;
            actor.JobStage = ActorJobStage.None;
            actor.JobTargetX = 0;
            actor.JobTargetY = 0;
            actor.JobTargetZ = 0;
            actor.RemainingWorkTicks = 0;
            actor.SourceStackId = 0;
            actor.DestinationZoneId = 0;
            actor.ReservedQuantity = 0;
            actor.RemainingRoute.Clear();
            actor.SuspendedJobKind = ActorJobKind.None;
            actor.SuspendedTargetX = 0;
            actor.SuspendedTargetY = 0;
            actor.SuspendedTargetZ = 0;
        }
    }

    private static GridPosition? FindNearestTraversablePosition(
        WorldMapState world,
        GridPosition origin) =>
        Enumerable.Range(0, world.Baseline.CellCount)
            .Select(index => new GridPosition(
                index % world.Baseline.Width,
                index / world.Baseline.Width,
                origin.Z))
            .Where(world.IsTerrainTraversable)
            .OrderBy(position =>
                Math.Abs(position.X - origin.X) + Math.Abs(position.Y - origin.Y))
            .ThenBy(position => position.Y)
            .ThenBy(position => position.X)
            .Select(position => (GridPosition?)position)
            .FirstOrDefault();
}
