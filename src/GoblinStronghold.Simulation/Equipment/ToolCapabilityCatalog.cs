namespace GoblinStronghold.Simulation.Equipment;

[Flags]
public enum ToolFunction : byte
{
    None = 0,
    Construction = 1 << 0,
    Mining = 1 << 1,
    Felling = 1 << 2,
    Earthmoving = 1 << 3,
}

public readonly record struct ToolCapabilityDefinition(
    PersonalEquipment Equipment,
    int Level,
    ToolFunction Functions);

public static class ToolCapabilityCatalog
{
    private static readonly ToolCapabilityDefinition[] Definitions =
    [
        new(PersonalEquipment.WoodenHammer, 1, ToolFunction.Construction),
        new(PersonalEquipment.WoodenAxe, 1, ToolFunction.Felling),
        new(PersonalEquipment.WoodenShovel, 1, ToolFunction.Earthmoving),
        new(PersonalEquipment.PrimitivePickaxe, 2,
            ToolFunction.Mining | ToolFunction.Earthmoving),
        new(PersonalEquipment.ReinforcedPickaxe, 3,
            ToolFunction.Mining | ToolFunction.Earthmoving),
    ];

    public static IReadOnlyList<ToolCapabilityDefinition> All => Definitions;

    public static int GetLevel(PersonalEquipment equipment)
    {
        var level = 0;
        foreach (var definition in Definitions)
        {
            if (equipment.HasFlag(definition.Equipment) && definition.Level > level)
            {
                level = definition.Level;
            }
        }
        return level;
    }

    public static ToolFunction GetFunctions(PersonalEquipment equipment)
    {
        var functions = ToolFunction.None;
        foreach (var definition in Definitions)
        {
            if (equipment.HasFlag(definition.Equipment))
            {
                functions |= definition.Functions;
            }
        }
        return functions;
    }

    public static bool MeetsRequirement(
        PersonalEquipment equipment,
        ToolFunction function,
        int minimumLevel)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(minimumLevel);
        if (function == ToolFunction.None)
        {
            return minimumLevel == 0;
        }

        foreach (var definition in Definitions)
        {
            if (equipment.HasFlag(definition.Equipment) &&
                definition.Functions.HasFlag(function) &&
                definition.Level >= minimumLevel)
            {
                return true;
            }
        }
        return false;
    }
}
