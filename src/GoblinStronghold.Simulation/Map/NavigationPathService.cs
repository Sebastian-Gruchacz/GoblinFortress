namespace GoblinStronghold.Simulation.Map;

public readonly record struct NavigationPathMetrics(
    long Requests,
    long Searches,
    long CacheHits,
    long CacheInvalidations,
    long ExpandedNodes,
    int MaximumExpandedNodes,
    int PendingSearches,
    int CachedRoutes,
    ulong TopologyVersion);

public enum NavigationPathRequestStatus : byte
{
    Pending = 0,
    Complete = 1,
    Unreachable = 2,
}

public readonly record struct NavigationPathRequestResult(
    NavigationPathRequestStatus Status,
    IReadOnlyList<GridPosition>? Path);

public readonly record struct NavigationPathContext(
    ulong OwnerId,
    ulong PersonalKnowledgeVersion,
    ulong SharedKnowledgeVersion,
    long FreshnessBucket = 0,
    ulong ConstraintKey = 0);

public sealed class NavigationPathService
{
    private const int MaximumCachedRoutes = 4_096;
    private const int MaximumPendingSearches = 4_096;
    private readonly WorldMapState _world;
    private readonly Dictionary<PathKey, GridPosition[]?> _routeCache = [];
    private readonly Dictionary<MultiPathKey, GridPosition[]?> _multiRouteCache = [];
    private readonly Dictionary<PathKey, IncrementalTerrainPathSearch> _pendingSearches = [];
    private readonly Dictionary<MultiPathKey, IncrementalTerrainPathSearch> _pendingMultiSearches = [];
    private ulong _cachedTopologyVersion;
    private long _requests;
    private long _searches;
    private long _cacheHits;
    private long _cacheInvalidations;
    private long _expandedNodes;
    private int _maximumExpandedNodes;

    public NavigationPathService(WorldMapState world)
    {
        _world = world ?? throw new ArgumentNullException(nameof(world));
        _cachedTopologyVersion = world.TopologyVersion;
    }

    public IReadOnlyList<GridPosition>? FindSurfacePath(
        GridPosition start,
        GridPosition destination,
        bool canOpenDoors = true)
    {
        EnsureCurrentTopology();
        _requests = checked(_requests + 1);
        var terrain = start.Z != 0 || destination.Z != 0;
        var key = new PathKey(start, destination, canOpenDoors, terrain, default);
        if (_routeCache.TryGetValue(key, out var cached))
        {
            _pendingSearches.Remove(key);
            _cacheHits = checked(_cacheHits + 1);
            return cached;
        }

        _searches = checked(_searches + 1);
        GridPosition[]? route;
        if (terrain)
        {
            route = FindTerrainPath(start, destination, canOpenDoors, null);
        }
        else
        {
            route = _world.FindSurfacePath(start, destination, canOpenDoors)?.ToArray();
        }
        AddToCache(key, route);
        return route;
    }

    public IReadOnlyList<GridPosition>? FindPath(
        GridPosition start,
        GridPosition destination,
        bool canOpenDoors = true)
    {
        EnsureCurrentTopology();
        _requests = checked(_requests + 1);
        var key = new PathKey(start, destination, canOpenDoors, Terrain: true, default);
        if (_routeCache.TryGetValue(key, out var cached))
        {
            _cacheHits = checked(_cacheHits + 1);
            return cached;
        }

        _searches = checked(_searches + 1);
        var route = FindTerrainPath(start, destination, canOpenDoors, null);
        AddToCache(key, route);
        return route;
    }

    public IReadOnlyList<GridPosition>? FindPath(
        GridPosition start,
        GridPosition destination,
        Func<GridPosition, GridPosition, bool> canUseEdge,
        bool canOpenDoors = true)
    {
        ArgumentNullException.ThrowIfNull(canUseEdge);
        EnsureCurrentTopology();
        _requests = checked(_requests + 1);
        _searches = checked(_searches + 1);
        return FindTerrainPath(
            start,
            destination,
            canOpenDoors,
            canUseEdge);
    }

    public IReadOnlyList<GridPosition>? FindPath(
        GridPosition start,
        GridPosition destination,
        NavigationPathContext context,
        Func<GridPosition, GridPosition, bool> canUseEdge,
        bool canOpenDoors = true)
    {
        ArgumentNullException.ThrowIfNull(canUseEdge);
        EnsureCurrentTopology();
        _requests = checked(_requests + 1);
        var key = new PathKey(start, destination, canOpenDoors, Terrain: true, context);
        if (_routeCache.TryGetValue(key, out var cached))
        {
            _cacheHits = checked(_cacheHits + 1);
            return cached;
        }

        _searches = checked(_searches + 1);
        var route = FindTerrainPath(
            start,
            destination,
            canOpenDoors,
            canUseEdge);
        AddToCache(key, route);
        return route;
    }

    public bool HasPath(GridPosition start, GridPosition destination) =>
        FindPath(start, destination) is not null;

    public bool HasSurfacePath(GridPosition start, GridPosition destination) =>
        FindSurfacePath(start, destination) is not null;

    public IReadOnlyList<GridPosition>? FindPathToNearest(
        GridPosition start,
        IReadOnlySet<GridPosition> destinations,
        Func<GridPosition, GridPosition, bool>? canUseEdge = null,
        bool canOpenDoors = true)
    {
        ArgumentNullException.ThrowIfNull(destinations);
        EnsureCurrentTopology();
        _requests = checked(_requests + 1);
        _searches = checked(_searches + 1);
        var route = _world.FindPathToNearestTerrainPosition(
            start,
            destinations,
            canOpenDoors,
            canUseEdge,
            out var expandedNodes)?.ToArray();
        _expandedNodes = checked(_expandedNodes + expandedNodes);
        _maximumExpandedNodes = Math.Max(_maximumExpandedNodes, expandedNodes);
        return route;
    }

    public NavigationPathRequestResult RequestPath(
        GridPosition start,
        GridPosition destination,
        int maximumExpandedNodes,
        bool canOpenDoors = true) =>
        RequestPathCore(
            start,
            destination,
            default,
            canUseEdge: null,
            maximumExpandedNodes,
            canOpenDoors);

    public NavigationPathRequestResult RequestPath(
        GridPosition start,
        GridPosition destination,
        NavigationPathContext context,
        Func<GridPosition, GridPosition, bool> canUseEdge,
        int maximumExpandedNodes,
        bool canOpenDoors = true)
    {
        ArgumentNullException.ThrowIfNull(canUseEdge);
        return RequestPathCore(
            start,
            destination,
            context,
            (Func<GridPosition, GridPosition, bool>?)canUseEdge,
            maximumExpandedNodes,
            canOpenDoors);
    }

    public NavigationPathRequestResult RequestPathToNearest(
        GridPosition start,
        IReadOnlySet<GridPosition> destinations,
        int maximumExpandedNodes,
        Func<GridPosition, GridPosition, bool>? canUseEdge = null,
        bool canOpenDoors = true) =>
        RequestPathToNearestCore(
            start,
            destinations,
            default,
            canUseEdge,
            maximumExpandedNodes,
            canOpenDoors);

    public NavigationPathRequestResult RequestPathToNearest(
        GridPosition start,
        IReadOnlySet<GridPosition> destinations,
        NavigationPathContext context,
        Func<GridPosition, GridPosition, bool> canUseEdge,
        int maximumExpandedNodes,
        bool canOpenDoors = true)
    {
        ArgumentNullException.ThrowIfNull(canUseEdge);
        return RequestPathToNearestCore(
            start,
            destinations,
            context,
            canUseEdge,
            maximumExpandedNodes,
            canOpenDoors);
    }

    public IReadOnlyList<GridPosition>? FindNearestHarvestablePlantPath(
        GridPosition start,
        ISet<GridPosition> excludedTargets,
        Func<GridPosition, bool>? isAllowed = null)
    {
        EnsureCurrentTopology();
        _requests = checked(_requests + 1);
        _searches = checked(_searches + 1);
        return _world.FindNearestHarvestablePlantPath(
            start,
            excludedTargets,
            isAllowed,
            canOpenDoors: true);
    }

    public IReadOnlyList<GridPosition>? FindNearestBerryBushPath(
        GridPosition start,
        ISet<GridPosition> excludedTargets,
        Func<GridPosition, bool>? isAllowed = null)
    {
        EnsureCurrentTopology();
        _requests = checked(_requests + 1);
        _searches = checked(_searches + 1);
        return _world.FindNearestBerryBushPath(
            start,
            excludedTargets,
            isAllowed,
            canOpenDoors: true);
    }

    public NavigationPathMetrics GetMetrics()
    {
        EnsureCurrentTopology();
        return new NavigationPathMetrics(
            _requests,
            _searches,
            _cacheHits,
            _cacheInvalidations,
            _expandedNodes,
            _maximumExpandedNodes,
            checked(_pendingSearches.Count + _pendingMultiSearches.Count),
            checked(_routeCache.Count + _multiRouteCache.Count),
            _cachedTopologyVersion);
    }

    private void EnsureCurrentTopology()
    {
        if (_cachedTopologyVersion == _world.TopologyVersion)
        {
            return;
        }

        _routeCache.Clear();
        _multiRouteCache.Clear();
        _pendingSearches.Clear();
        _pendingMultiSearches.Clear();
        _cachedTopologyVersion = _world.TopologyVersion;
        _cacheInvalidations = checked(_cacheInvalidations + 1);
    }

    private void AddToCache(PathKey key, GridPosition[]? route)
    {
        _pendingSearches.Remove(key);
        if (_routeCache.Count + _multiRouteCache.Count >= MaximumCachedRoutes)
        {
            _routeCache.Clear();
            _multiRouteCache.Clear();
            _cacheInvalidations = checked(_cacheInvalidations + 1);
        }
        _routeCache[key] = route;
    }

    private void AddToCache(MultiPathKey key, GridPosition[]? route)
    {
        _pendingMultiSearches.Remove(key);
        if (_routeCache.Count + _multiRouteCache.Count >= MaximumCachedRoutes)
        {
            _routeCache.Clear();
            _multiRouteCache.Clear();
            _cacheInvalidations = checked(_cacheInvalidations + 1);
        }
        _multiRouteCache[key] = route;
    }

    private GridPosition[]? FindTerrainPath(
        GridPosition start,
        GridPosition destination,
        bool canOpenDoors,
        Func<GridPosition, GridPosition, bool>? canUseEdge)
    {
        var route = _world.FindTerrainPath(
            start,
            destination,
            canOpenDoors,
            canUseEdge,
            out var expandedNodes)?.ToArray();
        _expandedNodes = checked(_expandedNodes + expandedNodes);
        _maximumExpandedNodes = Math.Max(_maximumExpandedNodes, expandedNodes);
        return route;
    }

    private NavigationPathRequestResult RequestPathCore(
        GridPosition start,
        GridPosition destination,
        NavigationPathContext context,
        Func<GridPosition, GridPosition, bool>? canUseEdge,
        int maximumExpandedNodes,
        bool canOpenDoors)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumExpandedNodes);
        EnsureCurrentTopology();
        _requests = checked(_requests + 1);
        var key = new PathKey(start, destination, canOpenDoors, Terrain: true, context);
        if (_routeCache.TryGetValue(key, out var cached))
        {
            _cacheHits = checked(_cacheHits + 1);
            return new NavigationPathRequestResult(
                cached is null
                    ? NavigationPathRequestStatus.Unreachable
                    : NavigationPathRequestStatus.Complete,
                cached);
        }

        if (!_pendingSearches.TryGetValue(key, out var search))
        {
            if (_pendingSearches.Count + _pendingMultiSearches.Count >= MaximumPendingSearches)
            {
                _pendingSearches.Clear();
                _pendingMultiSearches.Clear();
                _cacheInvalidations = checked(_cacheInvalidations + 1);
            }

            search = new IncrementalTerrainPathSearch(
                _world,
                start,
                destination,
                canOpenDoors,
                canUseEdge);
            _pendingSearches.Add(key, search);
            _searches = checked(_searches + 1);
        }

        var expandedBefore = search.ExpandedNodes;
        search.Advance(maximumExpandedNodes);
        _expandedNodes = checked(_expandedNodes + search.ExpandedNodes - expandedBefore);
        _maximumExpandedNodes = Math.Max(_maximumExpandedNodes, search.ExpandedNodes);
        if (search.Status == NavigationPathRequestStatus.Pending)
        {
            return new NavigationPathRequestResult(search.Status, null);
        }

        _pendingSearches.Remove(key);
        AddToCache(key, search.Path);
        return new NavigationPathRequestResult(search.Status, search.Path);
    }

    private NavigationPathRequestResult RequestPathToNearestCore(
        GridPosition start,
        IReadOnlySet<GridPosition> destinations,
        NavigationPathContext context,
        Func<GridPosition, GridPosition, bool>? canUseEdge,
        int maximumExpandedNodes,
        bool canOpenDoors)
    {
        ArgumentNullException.ThrowIfNull(destinations);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumExpandedNodes);
        EnsureCurrentTopology();
        _requests = checked(_requests + 1);
        var key = new MultiPathKey(
            start,
            new DestinationSetKey(destinations),
            canOpenDoors,
            context);
        if (_multiRouteCache.TryGetValue(key, out var cached))
        {
            _pendingMultiSearches.Remove(key);
            _cacheHits = checked(_cacheHits + 1);
            return new NavigationPathRequestResult(
                cached is null
                    ? NavigationPathRequestStatus.Unreachable
                    : NavigationPathRequestStatus.Complete,
                cached);
        }
        if (!_pendingMultiSearches.TryGetValue(key, out var search))
        {
            if (_pendingSearches.Count + _pendingMultiSearches.Count >= MaximumPendingSearches)
            {
                _pendingSearches.Clear();
                _pendingMultiSearches.Clear();
                _cacheInvalidations = checked(_cacheInvalidations + 1);
            }

            search = new IncrementalTerrainPathSearch(
                _world,
                start,
                destinations,
                canOpenDoors,
                canUseEdge);
            _pendingMultiSearches.Add(key, search);
            _searches = checked(_searches + 1);
        }

        var expandedBefore = search.ExpandedNodes;
        search.Advance(maximumExpandedNodes);
        _expandedNodes = checked(_expandedNodes + search.ExpandedNodes - expandedBefore);
        _maximumExpandedNodes = Math.Max(_maximumExpandedNodes, search.ExpandedNodes);
        if (search.Status == NavigationPathRequestStatus.Pending)
        {
            return new NavigationPathRequestResult(search.Status, null);
        }

        _pendingMultiSearches.Remove(key);
        AddToCache(key, search.Path);
        return new NavigationPathRequestResult(search.Status, search.Path);
    }

    private readonly record struct PathKey(
        GridPosition Start,
        GridPosition Destination,
        bool CanOpenDoors,
        bool Terrain,
        NavigationPathContext Context);

    private readonly record struct MultiPathKey(
        GridPosition Start,
        DestinationSetKey Destinations,
        bool CanOpenDoors,
        NavigationPathContext Context);

    private sealed class DestinationSetKey : IEquatable<DestinationSetKey>
    {
        private readonly GridPosition[] _positions;
        private readonly int _hashCode;

        public DestinationSetKey(IEnumerable<GridPosition> positions)
        {
            _positions = positions
                .Distinct()
                .OrderBy(position => position.Z)
                .ThenBy(position => position.Y)
                .ThenBy(position => position.X)
                .ToArray();
            var hash = new HashCode();
            foreach (var position in _positions)
            {
                hash.Add(position);
            }
            _hashCode = hash.ToHashCode();
        }

        public bool Equals(DestinationSetKey? other) =>
            other is not null && _positions.AsSpan().SequenceEqual(other._positions);

        public override bool Equals(object? obj) =>
            obj is DestinationSetKey other && Equals(other);

        public override int GetHashCode() => _hashCode;
    }

    private sealed class IncrementalTerrainPathSearch
    {
        private readonly WorldMapState _world;
        private readonly GridPosition _start;
        private readonly HashSet<GridPosition> _destinations;
        private readonly int _minimumDestinationX;
        private readonly int _maximumDestinationX;
        private readonly int _minimumDestinationY;
        private readonly int _maximumDestinationY;
        private readonly int _minimumDestinationZ;
        private readonly int _maximumDestinationZ;
        private readonly bool _canOpenDoors;
        private readonly Func<GridPosition, GridPosition, bool>? _canUseEdge;
        private readonly HashSet<GridPosition> _visited = [];
        private readonly Dictionary<GridPosition, GridPosition> _predecessors = [];
        private readonly Dictionary<GridPosition, int> _distances = [];
        private readonly PriorityQueue<
            GridPosition,
            (int EstimatedTotal, int Heuristic, long Sequence)> _queue = new();
        private long _sequence;

        public IncrementalTerrainPathSearch(
            WorldMapState world,
            GridPosition start,
            GridPosition destination,
            bool canOpenDoors,
            Func<GridPosition, GridPosition, bool>? canUseEdge)
            : this(world, start, [destination], canOpenDoors, canUseEdge)
        {
        }

        public IncrementalTerrainPathSearch(
            WorldMapState world,
            GridPosition start,
            IEnumerable<GridPosition> destinations,
            bool canOpenDoors,
            Func<GridPosition, GridPosition, bool>? canUseEdge)
        {
            _world = world;
            _start = start;
            _canOpenDoors = canOpenDoors;
            _canUseEdge = canUseEdge;
            Func<GridPosition, bool> canTraverse = canOpenDoors
                ? world.IsTerrainReachable
                : world.IsTerrainTraversable;
            _destinations = destinations.Where(canTraverse).ToHashSet();
            if (!world.IsTerrainTraversable(start) || _destinations.Count == 0)
            {
                Status = NavigationPathRequestStatus.Unreachable;
                return;
            }

            _minimumDestinationX = _destinations.Min(destination => destination.X);
            _maximumDestinationX = _destinations.Max(destination => destination.X);
            _minimumDestinationY = _destinations.Min(destination => destination.Y);
            _maximumDestinationY = _destinations.Max(destination => destination.Y);
            _minimumDestinationZ = _destinations.Min(destination => destination.Z);
            _maximumDestinationZ = _destinations.Max(destination => destination.Z);
            _distances.Add(start, 0);
            var heuristic = EstimateDistance(start);
            _queue.Enqueue(start, (heuristic, heuristic, _sequence++));
        }

        public NavigationPathRequestStatus Status { get; private set; }

        public GridPosition[]? Path { get; private set; }

        public int ExpandedNodes { get; private set; }

        public void Advance(int maximumExpandedNodes)
        {
            if (Status != NavigationPathRequestStatus.Pending)
            {
                return;
            }

            var expansionLimit = checked(ExpandedNodes + maximumExpandedNodes);
            while (ExpandedNodes < expansionLimit && _queue.TryDequeue(out var current, out _))
            {
                if (!_visited.Add(current))
                {
                    continue;
                }
                ExpandedNodes++;
                if (_destinations.Contains(current))
                {
                    var route = new List<GridPosition>();
                    while (current != _start)
                    {
                        route.Add(current);
                        current = _predecessors[current];
                    }
                    route.Reverse();
                    Path = route.ToArray();
                    Status = NavigationPathRequestStatus.Complete;
                    return;
                }

                foreach (var neighbor in _world.GetTerrainNeighbors(current, _canOpenDoors))
                {
                    if ((_canUseEdge is not null && !_canUseEdge(current, neighbor)) ||
                        _visited.Contains(neighbor))
                    {
                        continue;
                    }

                    var distance = checked(_distances[current] + 1);
                    if (_distances.TryGetValue(neighbor, out var knownDistance) &&
                        knownDistance <= distance)
                    {
                        continue;
                    }

                    _distances[neighbor] = distance;
                    _predecessors[neighbor] = current;
                    var heuristic = EstimateDistance(neighbor);
                    _queue.Enqueue(
                        neighbor,
                        (checked(distance + heuristic), heuristic, _sequence++));
                }
            }

            if (_queue.Count == 0)
            {
                Status = NavigationPathRequestStatus.Unreachable;
            }
        }

        private int EstimateDistance(GridPosition position)
        {
            var horizontal = DistanceToRange(
                    position.X,
                    _minimumDestinationX,
                    _maximumDestinationX) +
                DistanceToRange(
                    position.Y,
                    _minimumDestinationY,
                    _maximumDestinationY);
            var vertical = DistanceToRange(
                position.Z,
                _minimumDestinationZ,
                _maximumDestinationZ);
            return Math.Max(horizontal, vertical);
        }

        private static int DistanceToRange(int value, int minimum, int maximum) =>
            value < minimum ? minimum - value : value > maximum ? value - maximum : 0;
    }
}
