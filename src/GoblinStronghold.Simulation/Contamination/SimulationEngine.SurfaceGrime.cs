using GoblinStronghold.Simulation.Map;

namespace GoblinStronghold.Simulation;

public sealed partial class SimulationEngine
{
    private const int SurfaceCleaningWorkTicks = 32;
    private readonly Contamination.SurfaceGrimeState _surfaceGrime = new();
    private readonly Contamination.SurfaceContaminationAreaIndex
        _autonomousCleaningAreas = new();

    private bool HasSurfaceGrime(GridPosition position) => _surfaceGrime.Contains(position);

    private Contamination.SurfaceGrimeSnapshot[] CreateSurfaceGrimeSnapshot() =>
        _surfaceGrime.CreateSnapshot();

    private bool IsConstructedFloorSurface(GridPosition position) =>
        World.HasConstructedFloorSurface(position);

    private bool IsLooseDirtSource(GridPosition position)
    {
        if (IsConstructedFloorSurface(position))
        {
            return false;
        }

        if (Map.IsTerrainSurfacePosition(position))
        {
            return Map.GetColumnCell(position).Terrain is TerrainKind.SolidGround or TerrainKind.Mud;
        }

        return position.Z < 0 && World.IsTerrainTraversable(position);
    }

    private bool IsWashingSurface(GridPosition position) =>
        Map.IsTerrainSurfacePosition(position) &&
        Map.GetColumnCell(position).Terrain is TerrainKind.ShallowWater or TerrainKind.DeepWater;

    private int TrackSurfaceGrime(
        GridPosition source,
        GridPosition destination,
        int carriedGrime)
    {
        if (source == destination)
        {
            return carriedGrime;
        }

        carriedGrime = _surfaceGrime.PickUp(
            source,
            carriedGrime,
            CurrentTick);
        RefreshAutonomousCleaningRegistration(source);
        if (IsLooseDirtSource(source))
        {
            carriedGrime = Contamination.SurfaceGrimeState.MaximumCarriedAmount;
        }

        if (IsWashingSurface(destination))
        {
            return 0;
        }

        if (carriedGrime <= 0)
        {
            return 0;
        }

        if (IsConstructedFloorSurface(destination))
        {
            _surfaceGrime.Deposit(destination, carriedGrime, CurrentTick);
            RefreshAutonomousCleaningRegistration(destination);
        }

        return carriedGrime - 1;
    }

    private IEnumerable<GridPosition> GetCleanableSurfacePositions() =>
        _bloodStains.Keys
            .Concat(_surfaceGrime.EnumeratePositions())
            .Distinct()
            .Where(IsConstructedFloorSurface);

    private IEnumerable<Contamination.SurfaceContaminationArea>
        GetAutonomousCleaningAreas() => _autonomousCleaningAreas.EnumerateAreas();

    private bool HasCleanableSurface(GridPosition position) =>
        IsConstructedFloorSurface(position) &&
        (HasCleanableBloodOnly(position) || HasSurfaceGrime(position));

    private bool HasAutonomouslyCleanableSurface(GridPosition position) =>
        IsConstructedFloorSurface(position) &&
        Contamination.SurfaceCleaningPolicy.ShouldStartAutonomousCleaning(
            HasCleanableBloodOnly(position),
            _surfaceGrime.GetVolume(position));

    private int GetSurfaceCleaningWorkTicks(GridPosition position) =>
        HasCleanableBloodOnly(position)
            ? GetBloodCleaningWorkTicks(position)
            : SurfaceCleaningWorkTicks;

    private int CleanSurface(GridPosition position)
    {
        if (!IsConstructedFloorSurface(position))
        {
            RemoveBloodCleaningDesignations(position);
            return 0;
        }

        var cleaned = CleanBloodOnly(position) + _surfaceGrime.Clean(position, CurrentTick);
        RefreshAutonomousCleaningRegistration(position);
        if (!HasCleanableSurface(position))
        {
            RemoveBloodCleaningDesignations(position);
        }

        return cleaned;
    }

    private void RefreshAutonomousCleaningRegistration(GridPosition position)
    {
        if (HasAutonomouslyCleanableSurface(position))
        {
            _autonomousCleaningAreas.Add(position);
        }
        else
        {
            _autonomousCleaningAreas.Remove(position);
        }
    }

    private void RebuildAutonomousCleaningAreas()
    {
        _autonomousCleaningAreas.Clear();
        foreach (var position in GetCleanableSurfacePositions())
        {
            RefreshAutonomousCleaningRegistration(position);
        }
    }
}
