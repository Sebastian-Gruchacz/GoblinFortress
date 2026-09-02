using GoblinStronghold.Simulation.ContentPacks;

namespace GoblinStronghold.Simulation.Map.Generation;

public enum CaveMacroFeatureMaterializationPolicy : byte
{
    LayerByLayer = 1,
    CompleteOnExposure = 2,
}

public readonly record struct CaveMacroFeatureHandle
{
    public CaveMacroFeatureHandle(ContentId definitionId, ulong instanceId)
    {
        if (string.IsNullOrWhiteSpace(definitionId.PackageId) ||
            string.IsNullOrWhiteSpace(definitionId.LocalId))
        {
            throw new ArgumentException(
                "A cave macro-feature requires a valid content definition ID.",
                nameof(definitionId));
        }
        if (instanceId == 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(instanceId),
                "A cave macro-feature instance ID must be non-zero.");
        }

        DefinitionId = definitionId;
        InstanceId = instanceId;
    }

    public ContentId DefinitionId { get; }
    public ulong InstanceId { get; }
}

public sealed class CaveMacroFeatureSlice
{
    private readonly GridPosition[] _reservedCells;
    private readonly VerticalPassage[] _verticalPassages;

    public CaveMacroFeatureSlice(
        int level,
        IEnumerable<GridPosition> reservedCells,
        IEnumerable<VerticalPassage>? verticalPassages = null)
    {
        if (level >= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(level),
                "Cave macro-feature levels must be below the surface.");
        }

        ArgumentNullException.ThrowIfNull(reservedCells);
        _reservedCells = reservedCells.Distinct().ToArray();
        if (_reservedCells.Length == 0)
        {
            throw new ArgumentException(
                "A cave macro-feature slice must reserve at least one cell.",
                nameof(reservedCells));
        }
        if (_reservedCells.Any(position => position.Z != level))
        {
            throw new ArgumentException(
                "Every reserved cell must belong to the slice level.",
                nameof(reservedCells));
        }

        _verticalPassages = verticalPassages?.Distinct().ToArray() ?? [];
        if (_verticalPassages.Any(passage =>
                passage.Upper.Z != level && passage.Lower.Z != level))
        {
            throw new ArgumentException(
                "Every passage in a slice must touch the slice level.",
                nameof(verticalPassages));
        }

        Level = level;
    }

    public int Level { get; }
    public IReadOnlyList<GridPosition> ReservedCells => _reservedCells;
    public IReadOnlyList<VerticalPassage> VerticalPassages => _verticalPassages;
}

public sealed class CaveMacroFeaturePlan
{
    private readonly CaveMacroFeatureSlice[] _slices;
    private readonly Dictionary<int, CaveMacroFeatureSlice> _slicesByLevel;

    public CaveMacroFeaturePlan(
        CaveMacroFeatureHandle handle,
        CaveMacroFeatureMaterializationPolicy materializationPolicy,
        IEnumerable<CaveMacroFeatureSlice> slices)
    {
        ArgumentNullException.ThrowIfNull(slices);
        if (string.IsNullOrWhiteSpace(handle.DefinitionId.PackageId) ||
            string.IsNullOrWhiteSpace(handle.DefinitionId.LocalId) ||
            handle.InstanceId == 0)
        {
            throw new ArgumentException(
                "A cave macro-feature plan requires a valid handle.",
                nameof(handle));
        }
        _slices = slices.OrderByDescending(slice => slice.Level).ToArray();
        if (_slices.Length < 2)
        {
            throw new ArgumentException(
                "A cave macro-feature must span at least two levels.",
                nameof(slices));
        }
        if (!Enum.IsDefined(materializationPolicy))
        {
            throw new ArgumentOutOfRangeException(nameof(materializationPolicy));
        }

        for (var index = 1; index < _slices.Length; index++)
        {
            if (_slices[index].Level != _slices[index - 1].Level - 1)
            {
                throw new ArgumentException(
                    "Cave macro-feature slices must cover distinct, contiguous levels.",
                    nameof(slices));
            }
        }

        var reservedCells = _slices
            .SelectMany(slice => slice.ReservedCells)
            .ToHashSet();
        if (_slices.SelectMany(slice => slice.VerticalPassages).Any(passage =>
                !reservedCells.Contains(passage.Upper) ||
                !reservedCells.Contains(passage.Lower) ||
                passage.Upper.X != passage.Lower.X ||
                passage.Upper.Y != passage.Lower.Y ||
                passage.Upper.Z - passage.Lower.Z != 1))
        {
            throw new ArgumentException(
                "Passages must connect reserved cells in one vertical column one level apart.",
                nameof(slices));
        }

        Handle = handle;
        MaterializationPolicy = materializationPolicy;
        _slicesByLevel = _slices.ToDictionary(slice => slice.Level);
    }

    public CaveMacroFeatureHandle Handle { get; }
    public CaveMacroFeatureMaterializationPolicy MaterializationPolicy { get; }
    public int HighestLevel => _slices[0].Level;
    public int LowestLevel => _slices[^1].Level;
    public IReadOnlyList<CaveMacroFeatureSlice> Slices => _slices;

    public bool TryGetSlice(int level, out CaveMacroFeatureSlice slice) =>
        _slicesByLevel.TryGetValue(level, out slice!);
}

public readonly record struct CaveMacroFeatureCell(
    GridPosition Position,
    CaveCellKind Kind,
    CellFluidKind Fluid = CellFluidKind.None);

public sealed class CaveMacroFeatureLayout
{
    private readonly Dictionary<GridPosition, CaveMacroFeatureCell> _cells;

    public CaveMacroFeatureLayout(
        CaveMacroFeaturePlan plan,
        IEnumerable<CaveMacroFeatureCell> cells)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(cells);

        var reservedCells = plan.Slices
            .SelectMany(slice => slice.ReservedCells)
            .ToHashSet();
        _cells = cells.ToDictionary(cell => cell.Position);
        if (_cells.Count != reservedCells.Count ||
            _cells.Keys.Any(position => !reservedCells.Contains(position)))
        {
            throw new ArgumentException(
                "A cave macro-feature layout must define every reserved cell exactly once.",
                nameof(cells));
        }
        if (_cells.Values.Any(cell =>
                cell.Kind == CaveCellKind.SolidRock ||
                cell.Fluid != CellFluidKind.None && cell.Kind != CaveCellKind.Floor))
        {
            throw new ArgumentException(
                "Macro-feature cells must describe open cave floor, ramps, or fluid floor.",
                nameof(cells));
        }

        Plan = plan;
    }

    public CaveMacroFeaturePlan Plan { get; }

    public bool TryGetCell(GridPosition position, out CaveMacroFeatureCell cell) =>
        _cells.TryGetValue(position, out cell);
}

public sealed class CaveMacroFeatureMaterializationRegistry
{
    private readonly Dictionary<CaveMacroFeatureHandle, PendingFeature> _pending = [];
    private readonly Dictionary<GridPosition, CaveMacroFeatureHandle> _reservations = [];

    public int PendingCount => _pending.Count;

    public void Register(CaveMacroFeaturePlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (_pending.ContainsKey(plan.Handle))
        {
            throw new InvalidOperationException(
                $"Cave macro-feature instance {plan.Handle.InstanceId} is already registered.");
        }

        foreach (var position in plan.Slices.SelectMany(slice => slice.ReservedCells))
        {
            if (_reservations.TryGetValue(position, out var owner))
            {
                throw new InvalidOperationException(
                    $"Cell {position} is already reserved by cave macro-feature " +
                    $"{owner.InstanceId}.");
            }
        }

        _pending.Add(plan.Handle, new PendingFeature(plan));
        foreach (var position in plan.Slices.SelectMany(slice => slice.ReservedCells))
        {
            _reservations.Add(position, plan.Handle);
        }
    }

    public IReadOnlyList<int> GetLevelsToMaterialize(
        CaveMacroFeatureHandle handle,
        int approachedLevel)
    {
        var pending = GetPending(handle);
        if (!pending.Plan.TryGetSlice(approachedLevel, out _))
        {
            throw new ArgumentOutOfRangeException(
                nameof(approachedLevel),
                "The approached level does not intersect this cave macro-feature.");
        }

        if (pending.Plan.MaterializationPolicy ==
                CaveMacroFeatureMaterializationPolicy.LayerByLayer &&
            pending.MaterializedLevels.Contains(approachedLevel))
        {
            return [];
        }

        return pending.Plan.MaterializationPolicy ==
                CaveMacroFeatureMaterializationPolicy.CompleteOnExposure
            ? pending.Plan.Slices
                .Select(slice => slice.Level)
                .Where(level => !pending.MaterializedLevels.Contains(level))
                .ToArray()
            : [approachedLevel];
    }

    public bool MarkMaterialized(CaveMacroFeatureHandle handle, int level)
    {
        var pending = GetPending(handle);
        if (!pending.Plan.TryGetSlice(level, out _))
        {
            throw new ArgumentOutOfRangeException(
                nameof(level),
                "The materialized level does not belong to this cave macro-feature.");
        }

        pending.MaterializedLevels.Add(level);
        if (pending.MaterializedLevels.Count != pending.Plan.Slices.Count)
        {
            return false;
        }

        _pending.Remove(handle);
        foreach (var position in pending.Plan.Slices.SelectMany(slice => slice.ReservedCells))
        {
            _reservations.Remove(position);
        }
        return true;
    }

    public bool IsReserved(GridPosition position) => _reservations.ContainsKey(position);

    public bool TryGetPlan(
        CaveMacroFeatureHandle handle,
        out CaveMacroFeaturePlan plan)
    {
        if (_pending.TryGetValue(handle, out var pending))
        {
            plan = pending.Plan;
            return true;
        }

        plan = null!;
        return false;
    }

    private PendingFeature GetPending(CaveMacroFeatureHandle handle) =>
        _pending.TryGetValue(handle, out var pending)
            ? pending
            : throw new KeyNotFoundException(
                $"Cave macro-feature instance {handle.InstanceId} is not pending.");

    private sealed class PendingFeature(CaveMacroFeaturePlan plan)
    {
        public CaveMacroFeaturePlan Plan { get; } = plan;
        public HashSet<int> MaterializedLevels { get; } = [];
    }
}
