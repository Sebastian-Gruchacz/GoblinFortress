using GoblinStronghold.Simulation.Map;

namespace GoblinStronghold.Simulation;

public sealed partial class SimulationEngine
{
    private const int SurfaceCleaningWorkTicks = 32;
    private readonly Contamination.SurfaceGrimeState _surfaceGrime = new();
    private readonly Contamination.SurfaceContaminationAreaIndex
        _reportedCleaningAreas = new();

    private bool HasSurfaceGrime(GridPosition position) => _surfaceGrime.Contains(position);

    private Contamination.SurfaceGrimeSnapshot[] CreateSurfaceGrimeSnapshot() =>
        _surfaceGrime.CreateSnapshot();

    private bool IsConstructedCleanableSurface(GridPosition position) =>
        World.HasConstructedCleanableSurface(position);

    private bool IsLooseDirtSource(GridPosition position)
    {
        if (IsConstructedCleanableSurface(position))
        {
            return false;
        }

        if (Map.IsTerrainSurfacePosition(position))
        {
            return Map.GetColumnCell(position).Terrain is TerrainKind.SolidGround or TerrainKind.Mud or
                TerrainKind.Sand;
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

        if (IsConstructedCleanableSurface(destination))
        {
            _surfaceGrime.Deposit(destination, carriedGrime, CurrentTick);
        }

        return carriedGrime - 1;
    }

    private IEnumerable<GridPosition> GetCleanableSurfacePositions() =>
        _bloodStains.Keys
            .Concat(_surfaceGrime.EnumeratePositions())
            .Distinct()
            .Where(IsConstructedCleanableSurface);

    private IEnumerable<Contamination.SurfaceContaminationArea>
        GetAutonomousCleaningAreas() => _reportedCleaningAreas.EnumerateAreas();

    private IReadOnlyList<GridPosition> CreateReportedCleaningSnapshot() =>
        _reportedCleaningAreas.CreatePositionSnapshot();

    private bool HasCleanableSurface(GridPosition position) =>
        IsConstructedCleanableSurface(position) &&
        (HasCleanableBloodOnly(position) || HasSurfaceGrime(position));

    private bool HasAutonomouslyCleanableSurface(GridPosition position) =>
        IsConstructedCleanableSurface(position) &&
        Contamination.SurfaceCleaningPolicy.ShouldStartAutonomousCleaning(
            HasCleanableBloodOnly(position),
            _surfaceGrime.GetVolume(position));

    private int GetSurfaceCleaningWorkTicks(GridPosition position) =>
        HasCleanableBloodOnly(position)
            ? GetBloodCleaningWorkTicks(position)
            : SurfaceCleaningWorkTicks;

    private int CleanSurface(GridPosition position)
    {
        if (!IsConstructedCleanableSurface(position))
        {
            RemoveBloodCleaningDesignations(position);
            return 0;
        }

        var cleaned = CleanBloodOnly(position) + _surfaceGrime.Clean(position, CurrentTick);
        RefreshReportedCleaningRegistration(position);
        if (!HasCleanableSurface(position))
        {
            RemoveBloodCleaningDesignations(position);
        }

        return cleaned;
    }

    private void ReportAutonomousCleaning(GridPosition position)
    {
        if (HasAutonomouslyCleanableSurface(position))
        {
            _reportedCleaningAreas.Add(position);
        }
        else
        {
            _reportedCleaningAreas.Remove(position);
        }
    }

    private void RefreshReportedCleaningRegistration(GridPosition position)
    {
        if (_reportedCleaningAreas.Contains(position) &&
            !HasAutonomouslyCleanableSurface(position))
        {
            _reportedCleaningAreas.Remove(position);
        }
    }

    private void RestoreReportedCleaningAreas(IEnumerable<GridPosition> positions)
    {
        _reportedCleaningAreas.Clear();
        foreach (var position in positions)
        {
            ReportAutonomousCleaning(position);
        }
    }
}
