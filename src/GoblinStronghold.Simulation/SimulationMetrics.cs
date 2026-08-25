namespace GoblinStronghold.Simulation;

using GoblinStronghold.Simulation.Map;

public readonly record struct SimulationMetrics(
    long TicksExecuted,
    long CommandsExecuted,
    long EventsPublished,
    long ActorUpdates,
    int ActiveActors,
    int ItemStacks,
    int StorageZones,
    int PlantPatches,
    int WorldObjects,
    int PendingCommands,
    int UndeliveredEvents,
    int UndeliveredWorldChanges,
    NavigationPathMetrics Navigation,
    TimeSpan LastTickDuration,
    TimeSpan TotalTickDuration);
