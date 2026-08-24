using System.Collections.ObjectModel;
using GoblinStronghold.Simulation.Map;
using GoblinStronghold.Simulation.Resources;

namespace GoblinStronghold.Simulation;

public readonly record struct ActorSnapshot(
    EntityId Id,
    GridPosition Position,
    int Hunger,
    EntityId CarriedStackId);

public sealed class SimulationSnapshot
{
    internal SimulationSnapshot(
        WorldSeed worldSeed,
        SimulationTick tick,
        int foodStock,
        ActorSnapshot[] actors,
        ItemStackSnapshot[] itemStacks,
        StorageZoneSnapshot[] storageZones,
        PlantPatchSnapshot[] plantPatches,
        ulong worldVersion,
        int mapGeneratorVersion,
        string mapFingerprint,
        string stateHash)
    {
        WorldSeed = worldSeed;
        Tick = tick;
        FoodStock = foodStock;
        Actors = new ReadOnlyCollection<ActorSnapshot>(actors);
        ItemStacks = new ReadOnlyCollection<ItemStackSnapshot>(itemStacks);
        StorageZones = new ReadOnlyCollection<StorageZoneSnapshot>(storageZones);
        PlantPatches = new ReadOnlyCollection<PlantPatchSnapshot>(plantPatches);
        WorldVersion = worldVersion;
        MapGeneratorVersion = mapGeneratorVersion;
        MapFingerprint = mapFingerprint;
        StateHash = stateHash;
    }

    public WorldSeed WorldSeed { get; }

    public SimulationTick Tick { get; }

    public int FoodStock { get; }

    public IReadOnlyList<ActorSnapshot> Actors { get; }

    public IReadOnlyList<ItemStackSnapshot> ItemStacks { get; }

    public IReadOnlyList<StorageZoneSnapshot> StorageZones { get; }

    public IReadOnlyList<PlantPatchSnapshot> PlantPatches { get; }

    public ulong WorldVersion { get; }

    public int MapGeneratorVersion { get; }

    public string MapFingerprint { get; }

    public string StateHash { get; }
}
