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
    public const int CurrentVersion = 42;

    private static readonly ISimulationSaveMigration[] Migrations =
    [
        new SimulationSaveMigration30To31(),
        new SimulationSaveMigration31To32(),
        new SimulationSaveMigration32To33(),
        new SimulationSaveMigration33To34(),
        new SimulationSaveMigration34To35(),
        new SimulationSaveMigration35To36(),
        new SimulationSaveMigration36To37(),
        new SimulationSaveMigration37To38(),
        new SimulationSaveMigration38To39(),
        new SimulationSaveMigration39To40(),
        new SimulationSaveMigration40To41(),
        new SimulationSaveMigration41To42(),
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

internal sealed class SimulationSaveMigration41To42 : ISimulationSaveMigration
{
    public int SourceVersion => 41;

    public int TargetVersion => 42;

    public void MigrateModel(SimulationSaveModel save)
    {
    }

    public void MigrateWorldState(SimulationSaveModel save, WorldMapState world)
    {
    }
}

internal sealed class SimulationSaveMigration40To41 : ISimulationSaveMigration
{
    public int SourceVersion => 40;

    public int TargetVersion => 41;

    public void MigrateModel(SimulationSaveModel save)
    {
    }

    public void MigrateWorldState(SimulationSaveModel save, WorldMapState world)
    {
    }
}

internal sealed class SimulationSaveMigration39To40 : ISimulationSaveMigration
{
    public int SourceVersion => 39;

    public int TargetVersion => 40;

    public void MigrateModel(SimulationSaveModel save)
    {
    }

    public void MigrateWorldState(SimulationSaveModel save, WorldMapState world)
    {
    }
}

internal sealed class SimulationSaveMigration38To39 : ISimulationSaveMigration
{
    public int SourceVersion => 38;

    public int TargetVersion => 39;

    public void MigrateModel(SimulationSaveModel save)
    {
    }

    public void MigrateWorldState(SimulationSaveModel save, WorldMapState world)
    {
    }
}

internal sealed class SimulationSaveMigration37To38 : ISimulationSaveMigration
{
    public int SourceVersion => 37;

    public int TargetVersion => 38;

    public void MigrateModel(SimulationSaveModel save)
    {
        save.BloodStains ??= [];
    }

    public void MigrateWorldState(SimulationSaveModel save, WorldMapState world)
    {
    }
}

internal sealed class SimulationSaveMigration36To37 : ISimulationSaveMigration
{
    public int SourceVersion => 36;

    public int TargetVersion => 37;

    public void MigrateModel(SimulationSaveModel save)
    {
    }

    public void MigrateWorldState(SimulationSaveModel save, WorldMapState world)
    {
        foreach (var actor in save.Actors)
        {
            if (!TryProjectLegacySurfacePosition(
                    world,
                    new GridPosition(actor.X, actor.Y, actor.Z),
                    out var projected))
            {
                continue;
            }

            actor.X = projected.X;
            actor.Y = projected.Y;
            actor.Z = projected.Z;
            actor.RemainingRoute.Clear();
            actor.JobKind = ActorJobKind.None;
            actor.JobPhase = ActorJobPhase.None;
            actor.JobStage = ActorJobStage.None;
        }

        foreach (var zone in save.StorageZones)
        {
            if (!TryProjectLegacySurfacePosition(
                    world,
                    new GridPosition(zone.X, zone.Y, zone.Z),
                    out var projected))
            {
                continue;
            }

            zone.X = projected.X;
            zone.Y = projected.Y;
            zone.Z = projected.Z;
            foreach (var stack in save.ItemStacks.Where(stack =>
                         stack.LocationKind == ItemLocationKind.StorageZone &&
                         stack.OwnerId == zone.Id))
            {
                stack.X = projected.X;
                stack.Y = projected.Y;
                stack.Z = projected.Z;
            }
        }

        foreach (var stack in save.ItemStacks.Where(stack =>
                     stack.LocationKind == ItemLocationKind.Ground))
        {
            if (!TryProjectLegacySurfacePosition(
                    world,
                    new GridPosition(stack.X, stack.Y, stack.Z),
                    out var projected))
            {
                continue;
            }

            stack.X = projected.X;
            stack.Y = projected.Y;
            stack.Z = projected.Z;
        }
    }

    private static bool TryProjectLegacySurfacePosition(
        WorldMapState world,
        GridPosition position,
        out GridPosition projected)
    {
        projected = position;
        if (position.Z != 0 ||
            world.IsTerrainTraversable(position) ||
            !world.Baseline.IsColumnWithin(position))
        {
            return false;
        }

        projected = world.Baseline.GetTerrainSurfacePosition(position);
        return projected != position && world.IsTerrainTraversable(projected);
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
