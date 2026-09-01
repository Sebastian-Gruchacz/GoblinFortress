using GoblinStronghold.Simulation.ContentPacks;

namespace GoblinStronghold.Simulation.Civilizations.Naming;

public enum NameGeneratorKind : byte
{
    SyllableCombination = 1,
    OrderedList = 2,
    NumericPlaceholder = 3,
}

public sealed class NameGeneratorDefinition
{
    public ContentId Id { get; init; }

    public NameGeneratorKind Kind { get; init; }

    public RandomDomain RandomDomain { get; init; } = RandomDomain.GoblinIdentity;

    public ulong FirstSampleKey { get; init; }

    public ulong SecondSampleKey { get; init; }

    public bool AppendSubjectIdOnCollision { get; init; }

    public List<string> Beginnings { get; init; } = [];

    public List<string> FemaleBeginnings { get; init; } = [];

    public List<string> MaleBeginnings { get; init; } = [];

    public List<string> Endings { get; init; } = [];

    public List<string> FemaleEndings { get; init; } = [];

    public List<string> MaleEndings { get; init; } = [];

    public List<string> Names { get; init; } = [];

    public List<string> FemaleNames { get; init; } = [];

    public List<string> MaleNames { get; init; } = [];
}
