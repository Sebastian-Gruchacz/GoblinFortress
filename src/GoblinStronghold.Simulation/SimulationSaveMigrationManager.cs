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
    public const int CurrentVersion = 59;

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
        new SimulationSaveMigration42To43(),
        new SimulationSaveMigration43To44(),
        new SimulationSaveMigration44To45(),
        new SimulationSaveMigration45To46(),
        new SimulationSaveMigration46To47(),
        new SimulationSaveMigration47To48(),
        new SimulationSaveMigration48To49(),
        new SimulationSaveMigration49To50(),
        new SimulationSaveMigration50To51(),
        new SimulationSaveMigration51To52(),
        new SimulationSaveMigration52To53(),
        new SimulationSaveMigration53To54(),
        new SimulationSaveMigration54To55(),
        new SimulationSaveMigration55To56(),
        new SimulationSaveMigration56To57(),
        new SimulationSaveMigration57To58(),
        new SimulationSaveMigration58To59(),
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

internal sealed class SimulationSaveMigration58To59 : ISimulationSaveMigration
{
    public int SourceVersion => 58;

    public int TargetVersion => 59;

    public void MigrateModel(SimulationSaveModel save)
    {
        save.ExcavatedTerrainRamps = [];
        foreach (var site in save.ConstructionSites)
        {
            site.OrderId = site.Id;
            site.SequenceIndex = 0;
        }
    }

    public void MigrateWorldState(SimulationSaveModel save, WorldMapState world)
    {
    }
}

internal sealed class SimulationSaveMigration57To58 : ISimulationSaveMigration
{
    public int SourceVersion => 57;

    public int TargetVersion => 58;

    public void MigrateModel(SimulationSaveModel save)
    {
        save.UndergroundFactions = [];
    }

    public void MigrateWorldState(SimulationSaveModel save, WorldMapState world)
    {
    }
}

internal sealed class SimulationSaveMigration56To57 : ISimulationSaveMigration
{
    public int SourceVersion => 56;

    public int TargetVersion => 57;

    public void MigrateModel(SimulationSaveModel save)
    {
        foreach (var animal in save.Animals ?? [])
        {
            animal.Sex = animal.Id % 2 == 0 ? AnimalSex.Male : AnimalSex.Female;
        }
    }

    public void MigrateWorldState(SimulationSaveModel save, WorldMapState world)
    {
    }
}

internal sealed class SimulationSaveMigration55To56 : ISimulationSaveMigration
{
    public int SourceVersion => 55;

    public int TargetVersion => 56;

    public void MigrateModel(SimulationSaveModel save)
    {
        // Version 55 did not retain biological knowledge in corpses or corpse-origin buds.
        // Default zero-valued imprints preserve those existing saves without inventing a donor.
    }

    public void MigrateWorldState(SimulationSaveModel save, WorldMapState world)
    {
    }
}

internal sealed class SimulationSaveMigration54To55 : ISimulationSaveMigration
{
    public int SourceVersion => 54;

    public int TargetVersion => 55;

    public void MigrateModel(SimulationSaveModel save)
    {
        save.Corpses = [];
    }

    public void MigrateWorldState(SimulationSaveModel save, WorldMapState world)
    {
    }
}

internal sealed class SimulationSaveMigration53To54 : ISimulationSaveMigration
{
    public int SourceVersion => 53;

    public int TargetVersion => 54;

    public void MigrateModel(SimulationSaveModel save)
    {
        var legacyDefault = SimulationEngine.DefaultRaidDirectives |
            RaidDirective.AutoLaunchWhenReady;
        if (save.RaidDirectives == legacyDefault)
        {
            save.RaidDirectives &= ~RaidDirective.AutoLaunchWhenReady;
        }
    }

    public void MigrateWorldState(SimulationSaveModel save, WorldMapState world)
    {
    }
}

internal sealed class SimulationSaveMigration52To53 : ISimulationSaveMigration
{
    public int SourceVersion => 52;

    public int TargetVersion => 53;

    public void MigrateModel(SimulationSaveModel save)
    {
        if (save.ResourcePriorities.All(priority => priority.Resource != ResourceKind.Equipment))
        {
            save.ResourcePriorities.Add(new ResourcePrioritySaveModel
            {
                Resource = ResourceKind.Equipment,
                Priority = StoragePriority.Normal,
            });
        }
    }

    public void MigrateWorldState(SimulationSaveModel save, WorldMapState world)
    {
    }
}

internal sealed class SimulationSaveMigration51To52 : ISimulationSaveMigration
{
    public int SourceVersion => 51;

    public int TargetVersion => 52;

    public void MigrateModel(SimulationSaveModel save)
    {
        save.HumanVillage.StorehouseSiteX = null;
        save.HumanVillage.StorehouseSiteY = null;
        save.HumanVillage.StorehouseSiteZ = null;
        save.HumanVillage.StorehouseWorkProgress = 0;
    }

    public void MigrateWorldState(SimulationSaveModel save, WorldMapState world)
    {
    }
}

internal sealed class SimulationSaveMigration50To51 : ISimulationSaveMigration
{
    public int SourceVersion => 50;

    public int TargetVersion => 51;

    public void MigrateModel(SimulationSaveModel save)
    {
        save.HumanVillage.GoodsWorkProgress = 0;
    }

    public void MigrateWorldState(SimulationSaveModel save, WorldMapState world)
    {
    }
}

internal sealed class SimulationSaveMigration49To50 : ISimulationSaveMigration
{
    public int SourceVersion => 49;

    public int TargetVersion => 50;

    public void MigrateModel(SimulationSaveModel save)
    {
        save.HumanVillage.TreeFellingX = null;
        save.HumanVillage.TreeFellingY = null;
        save.HumanVillage.TreeFellingZ = null;
        save.HumanVillage.TreeFellingProgress = 0;
    }

    public void MigrateWorldState(SimulationSaveModel save, WorldMapState world)
    {
    }
}

internal sealed class SimulationSaveMigration48To49 : ISimulationSaveMigration
{
    public int SourceVersion => 48;

    public int TargetVersion => 49;

    public void MigrateModel(SimulationSaveModel save)
    {
        foreach (var field in save.HumanVillage.Fields)
        {
            field.WorkProgress = 0;
        }
    }

    public void MigrateWorldState(SimulationSaveModel save, WorldMapState world)
    {
    }
}

internal sealed class SimulationSaveMigration47To48 : ISimulationSaveMigration
{
    public int SourceVersion => 47;

    public int TargetVersion => 48;

    public void MigrateModel(SimulationSaveModel save)
    {
        foreach (var villager in save.HumanVillage.Villagers)
        {
            villager.WorkProgress = 0;
        }
    }

    public void MigrateWorldState(SimulationSaveModel save, WorldMapState world)
    {
    }
}

internal sealed class SimulationSaveMigration46To47 : ISimulationSaveMigration
{
    public int SourceVersion => 46;

    public int TargetVersion => 47;

    public void MigrateModel(SimulationSaveModel save)
    {
        foreach (var villager in save.HumanVillage.Villagers)
        {
            villager.Hunger = 0;
            villager.Thirst = 0;
        }
    }

    public void MigrateWorldState(SimulationSaveModel save, WorldMapState world)
    {
    }
}

internal sealed class SimulationSaveMigration45To46 : ISimulationSaveMigration
{
    public int SourceVersion => 45;

    public int TargetVersion => 46;

    public void MigrateModel(SimulationSaveModel save)
    {
        if (save.HumanVillage.Villagers.Count == 0)
        {
            save.HumanVillage.Villagers = CreateVillagers(save.HumanVillage);
        }
    }

    public void MigrateWorldState(SimulationSaveModel save, WorldMapState world)
    {
        var occupied = new HashSet<GridPosition>();
        foreach (var villager in save.HumanVillage.Villagers.OrderBy(item => item.Id))
        {
            var current = new GridPosition(villager.X, villager.Y, villager.Z);
            if (world.IsSurfaceTraversable(current) && occupied.Add(current))
            {
                continue;
            }

            var replacement = Enumerable.Range(0, world.Baseline.Height)
                .SelectMany(y => Enumerable.Range(0, world.Baseline.Width)
                    .Select(x => new GridPosition(x, y)))
                .Where(position => world.IsSurfaceTraversable(position) &&
                    !occupied.Contains(position) &&
                    Distance(position, world.Baseline.HumanVillage) <=
                        SimulationDefinitions.Foundation.HumanVillageActivityRadius + 4)
                .OrderBy(position => Distance(position, current))
                .ThenBy(position => position.Y)
                .ThenBy(position => position.X)
                .Select(position => (GridPosition?)position)
                .FirstOrDefault();
            if (replacement is null)
            {
                throw new InvalidDataException(
                    "The migrated human village has too few materialization positions.");
            }
            villager.X = replacement.Value.X;
            villager.Y = replacement.Value.Y;
            villager.Z = replacement.Value.Z;
            occupied.Add(replacement.Value);
        }
    }

    private static int Distance(GridPosition left, GridPosition right) =>
        Math.Abs(left.X - right.X) + Math.Abs(left.Y - right.Y);

    private static List<HumanVillagerSaveModel> CreateVillagers(HumanVillageSaveModel village)
    {
        var result = new List<HumanVillagerSaveModel>(village.Population);
        var id = 1;
        foreach (var cohort in village.Cohorts.OrderBy(item => item.Id))
        {
            var remainingGuardHealth = cohort.Role == HumanCohortRole.Guards
                ? village.GuardHitPoints
                : 0;
            for (var index = 0; index < cohort.Population; index++)
            {
                var maximumHealth = HumanVillageState.GetMaximumHealth(
                    cohort.Role,
                    SimulationDefinitions.Foundation);
                var health = cohort.Role == HumanCohortRole.Guards
                    ? Math.Min(maximumHealth, remainingGuardHealth)
                    : maximumHealth;
                remainingGuardHealth = Math.Max(0, remainingGuardHealth - health);
                result.Add(new HumanVillagerSaveModel
                {
                    Id = id++,
                    Role = cohort.Role,
                    X = cohort.X,
                    Y = cohort.Y,
                    Z = cohort.Z,
                    Task = cohort.Task,
                    SkillLevel = cohort.SkillLevel,
                    Tools = HumanVillageState.GetIndividualTools(cohort.Role, index),
                    Health = health,
                    Fatigue = 0,
                });
            }
        }
        return result;
    }
}

internal sealed class SimulationSaveMigration44To45 : ISimulationSaveMigration
{
    public int SourceVersion => 44;

    public int TargetVersion => 45;

    public void MigrateModel(SimulationSaveModel save)
    {
    }

    public void MigrateWorldState(SimulationSaveModel save, WorldMapState world)
    {
        var generatedHuts = GeneratedSettlementStructureGenerator.Generate(world.Baseline)
            .Where(worldObject =>
                worldObject.Kind == WorldObjectKind.GoblinHut &&
                worldObject.Owner == WorldObjectOwner.GoblinTribe)
            .Select(worldObject => worldObject.Anchor)
            .ToHashSet();
        var constructedHutCount = world.EnumerateWorldObjects().Count(worldObject =>
            worldObject.Kind == WorldObjectKind.GoblinHut &&
            worldObject.Owner == WorldObjectOwner.GoblinTribe &&
            !generatedHuts.Contains(worldObject.Anchor));
        save.PopulationTarget = Math.Min(
            1_000,
            checked(save.PopulationTarget +
                (constructedHutCount * SimulationDefinitions.GoblinHutCapacity)));
    }
}

internal sealed class SimulationSaveMigration43To44 : ISimulationSaveMigration
{
    public int SourceVersion => 43;

    public int TargetVersion => 44;

    public void MigrateModel(SimulationSaveModel save)
    {
    }

    public void MigrateWorldState(SimulationSaveModel save, WorldMapState world)
    {
    }
}

internal sealed class SimulationSaveMigration42To43 : ISimulationSaveMigration
{
    public int SourceVersion => 42;

    public int TargetVersion => 43;

    public void MigrateModel(SimulationSaveModel save)
    {
        save.RaidTargetRadius = 0;
        save.RaidDirectives = SimulationEngine.DefaultRaidDirectives;
    }

    public void MigrateWorldState(SimulationSaveModel save, WorldMapState world)
    {
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
                     resource is not (ResourceKind.Any or ResourceKind.Materials) &&
                     !presentResources.Contains(resource)))
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
