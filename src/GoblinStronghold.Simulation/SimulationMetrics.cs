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
    TimeSpan TotalTickDuration,
    SimulationTickBreakdown LastTickBreakdown);

public readonly record struct SimulationTickBreakdown(
    TimeSpan World,
    TimeSpan Commands,
    TimeSpan ActorJobs,
    TimeSpan Animals,
    TimeSpan Doors,
    TimeSpan HumanVillage,
    TimeSpan Combat,
    TimeSpan Actors,
    TimeSpan Raid,
    TimeSpan Visibility);

public readonly record struct ActorJobUpdateProfile(
    TimeSpan Reproduction,
    TimeSpan Reservations,
    TimeSpan NeedInterrupts,
    TimeSpan IdlePlanning,
    TimeSpan ActiveJobs,
    TimeSpan Finalization);
