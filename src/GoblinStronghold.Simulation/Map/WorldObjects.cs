using System.Collections.ObjectModel;

namespace GoblinStronghold.Simulation.Map;

public readonly record struct WorldObjectId(ulong Value) : IComparable<WorldObjectId>
{
    public static WorldObjectId None => default;

    public int CompareTo(WorldObjectId other) => Value.CompareTo(other.Value);

    public override string ToString() => Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
}

public enum WorldObjectKind : byte
{
    GoblinHut = 1,
    HumanCottage = 2,
    HumanBarn = 3,
    HumanWell = 4,
    WoodenWalkway = 5,
    HumanStorehouse = 6,
    GoblinFieldCamp = 7,
    Tree = 8,
    DeadTreeStump = 9,
    WoodenWall = 10,
    WoodenDoorFrame = 11,
    WoodenDoorLeaf = 12,
    Boulder = 13,
    StoneWall = 14,
    StoneDoorFrame = 15,
    WallTorch = 16,
    PrimitiveWorkshop = 17,
}

public enum WorldObjectOwner : byte
{
    GoblinTribe = 1,
    HumanVillage = 2,
    Nature = 3,
}

public enum CardinalOrientation : byte
{
    North = 1,
    East = 2,
    South = 3,
    West = 4,
}

public enum SpatialOccupancyChannel : byte
{
    Surface = 1,
    Solid = 2,
    Overhead = 3,
    Subsurface = 4,
    Fixture = 5,
}

public enum WorldObjectPartKind : byte
{
    Floor = 1,
    Wall = 2,
    Door = 3,
    Roof = 4,
    WellRim = 5,
    WellShaft = 6,
    Walkway = 7,
    TreeTrunk = 8,
    TreeCrown = 9,
    TreeStump = 10,
    ClosedDoorLeaf = 11,
    OpenDoorLeaf = 12,
    AutomaticallyOpenedDoorLeaf = 13,
    Boulder = 14,
    WallTorch = 15,
    PrimitiveWorkshop = 16,
}

public readonly record struct WorldObjectPartSnapshot(
    GridPosition RelativePosition,
    SpatialOccupancyChannel Channel,
    WorldObjectPartKind Kind);

public sealed class WorldObjectSnapshot
{
    public WorldObjectSnapshot(
        WorldObjectId id,
        WorldObjectKind kind,
        WorldObjectOwner owner,
        GridPosition anchor,
        CardinalOrientation orientation,
        IEnumerable<WorldObjectPartSnapshot> parts)
    {
        ArgumentNullException.ThrowIfNull(parts);

        Id = id;
        Kind = kind;
        Owner = owner;
        Anchor = anchor;
        Orientation = orientation;
        Parts = new ReadOnlyCollection<WorldObjectPartSnapshot>(parts.ToArray());
    }

    public WorldObjectId Id { get; }

    public WorldObjectKind Kind { get; }

    public WorldObjectOwner Owner { get; }

    public GridPosition Anchor { get; }

    public CardinalOrientation Orientation { get; }

    public IReadOnlyList<WorldObjectPartSnapshot> Parts { get; }

    public IEnumerable<(GridPosition Position, WorldObjectPartSnapshot Part)> GetAbsoluteParts() =>
        Parts.Select(part =>
            (Add(Anchor, part.RelativePosition), part));

    private static GridPosition Add(GridPosition left, GridPosition right) => new(
        checked(left.X + right.X),
        checked(left.Y + right.Y),
        checked(left.Z + right.Z));
}

internal readonly record struct SpatialOccupancyKey(
    GridPosition Position,
    SpatialOccupancyChannel Channel);

internal readonly record struct SpatialOccupancyClaim(
    WorldObjectId ObjectId,
    WorldObjectPartKind PartKind);
