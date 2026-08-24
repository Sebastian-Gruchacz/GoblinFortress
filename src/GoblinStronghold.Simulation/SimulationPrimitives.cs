namespace GoblinStronghold.Simulation;

public readonly record struct SimulationTick(long Value) : IComparable<SimulationTick>
{
    public static SimulationTick Zero => new(0);

    public SimulationTick Next() => new(checked(Value + 1));

    public int CompareTo(SimulationTick other) => Value.CompareTo(other.Value);

    public override string ToString() => Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
}

public readonly record struct EntityId(ulong Value) : IComparable<EntityId>
{
    public static EntityId None => new(0);

    public int CompareTo(EntityId other) => Value.CompareTo(other.Value);

    public override string ToString() => Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
}

public readonly record struct WorldSeed(ulong Value)
{
    public override string ToString() => Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
}
