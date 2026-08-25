namespace GoblinStronghold.Simulation.Map;

public readonly record struct NavigationPathMetrics(
    long Requests,
    long Searches,
    long CacheHits,
    long CacheInvalidations,
    int CachedRoutes,
    ulong TopologyVersion);

public sealed class NavigationPathService
{
    private const int MaximumCachedRoutes = 4_096;
    private readonly WorldMapState _world;
    private readonly Dictionary<PathKey, GridPosition[]?> _routeCache = [];
    private ulong _cachedTopologyVersion;
    private long _requests;
    private long _searches;
    private long _cacheHits;
    private long _cacheInvalidations;

    public NavigationPathService(WorldMapState world)
    {
        _world = world ?? throw new ArgumentNullException(nameof(world));
        _cachedTopologyVersion = world.TopologyVersion;
    }

    public IReadOnlyList<GridPosition>? FindSurfacePath(
        GridPosition start,
        GridPosition destination)
    {
        EnsureCurrentTopology();
        _requests = checked(_requests + 1);
        var key = new PathKey(start, destination);
        if (_routeCache.TryGetValue(key, out var cached))
        {
            _cacheHits = checked(_cacheHits + 1);
            return cached;
        }

        _searches = checked(_searches + 1);
        var route = _world.FindSurfacePath(start, destination)?.ToArray();
        if (_routeCache.Count >= MaximumCachedRoutes)
        {
            _routeCache.Clear();
            _cacheInvalidations = checked(_cacheInvalidations + 1);
        }
        _routeCache.Add(key, route);
        return route;
    }

    public bool HasSurfacePath(GridPosition start, GridPosition destination) =>
        FindSurfacePath(start, destination) is not null;

    public IReadOnlyList<GridPosition>? FindNearestHarvestablePlantPath(
        GridPosition start,
        ISet<GridPosition> excludedTargets,
        Func<GridPosition, bool>? isAllowed = null)
    {
        EnsureCurrentTopology();
        _requests = checked(_requests + 1);
        _searches = checked(_searches + 1);
        return _world.FindNearestHarvestablePlantPath(start, excludedTargets, isAllowed);
    }

    public IReadOnlyList<GridPosition>? FindNearestBerryBushPath(
        GridPosition start,
        ISet<GridPosition> excludedTargets,
        Func<GridPosition, bool>? isAllowed = null)
    {
        EnsureCurrentTopology();
        _requests = checked(_requests + 1);
        _searches = checked(_searches + 1);
        return _world.FindNearestBerryBushPath(start, excludedTargets, isAllowed);
    }

    public NavigationPathMetrics GetMetrics()
    {
        EnsureCurrentTopology();
        return new NavigationPathMetrics(
            _requests,
            _searches,
            _cacheHits,
            _cacheInvalidations,
            _routeCache.Count,
            _cachedTopologyVersion);
    }

    private void EnsureCurrentTopology()
    {
        if (_cachedTopologyVersion == _world.TopologyVersion)
        {
            return;
        }

        _routeCache.Clear();
        _cachedTopologyVersion = _world.TopologyVersion;
        _cacheInvalidations = checked(_cacheInvalidations + 1);
    }

    private readonly record struct PathKey(
        GridPosition Start,
        GridPosition Destination);
}
