using System.Collections.ObjectModel;
using GoblinStronghold.Simulation.Map;
using GoblinStronghold.Simulation.Resources;

namespace GoblinStronghold.Simulation;

public enum CorpseKind : byte
{
    Goblin = 1,
    Human = 2,
}

[Flags]
public enum CorpseDirective : byte
{
    None = 0,
    LootContents = 1 << 0,
    Consume = 1 << 1,
    RecoverToCamp = 1 << 2,
    RecoverAndBudAtCamp = 1 << 3,
    BudInPlace = 1 << 4,
}

public readonly record struct CorpseItemSnapshot(
    ResourceKind Resource,
    FoodKind FoodKind,
    ResourceVariant Variant,
    int Quantity,
    int UnitWeight);

public readonly record struct GoblinInheritanceImprint(
    GoblinSkill KnownSkills,
    GoblinTrait KnownTraits,
    GoblinExperienceSnapshot Experience,
    GoblinWorkPreferences WorkPreferences);

public sealed class CorpseSnapshot
{
    internal CorpseSnapshot(
        EntityId id,
        CorpseKind kind,
        string name,
        GridPosition position,
        SimulationTick createdAt,
        int containedWater,
        int ediblePortions,
        CorpseDirective directives,
        GoblinInheritanceImprint inheritanceImprint,
        IEnumerable<CorpseItemSnapshot> contents)
    {
        Id = id;
        Kind = kind;
        Name = name;
        Position = position;
        CreatedAt = createdAt;
        ContainedWater = containedWater;
        EdiblePortions = ediblePortions;
        Directives = directives;
        InheritanceImprint = inheritanceImprint;
        Contents = new ReadOnlyCollection<CorpseItemSnapshot>(contents.ToArray());
    }

    public EntityId Id { get; }

    public CorpseKind Kind { get; }

    public string Name { get; }

    public GridPosition Position { get; }

    public SimulationTick CreatedAt { get; }

    public int ContainedWater { get; }

    public int EdiblePortions { get; }

    public CorpseDirective Directives { get; }

    public GoblinInheritanceImprint InheritanceImprint { get; }

    public IReadOnlyList<CorpseItemSnapshot> Contents { get; }

    public int ContentsWeight => Contents.Sum(item => item.Quantity * item.UnitWeight);
}

internal sealed class CorpseState(
    EntityId id,
    CorpseKind kind,
    string name,
    GridPosition position,
    SimulationTick createdAt,
    int containedWater,
    int ediblePortions,
    GoblinInheritanceImprint inheritanceImprint,
    IEnumerable<CorpseItemSnapshot> contents)
{
    public EntityId Id { get; } = id;

    public CorpseKind Kind { get; } = kind;

    public string Name { get; } = name;

    public GridPosition Position { get; set; } = position;

    public SimulationTick CreatedAt { get; } = createdAt;

    public int ContainedWater { get; } = containedWater;

    public int EdiblePortions { get; set; } = ediblePortions;

    public CorpseDirective Directives { get; set; }

    public GoblinInheritanceImprint InheritanceImprint { get; } = inheritanceImprint;

    public List<CorpseItemSnapshot> Contents { get; } = contents.ToList();

    public CorpseSnapshot ToSnapshot() => new(
        Id,
        Kind,
        Name,
        Position,
        CreatedAt,
        ContainedWater,
        EdiblePortions,
        Directives,
        InheritanceImprint,
        Contents);
}
