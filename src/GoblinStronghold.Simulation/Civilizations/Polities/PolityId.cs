using GoblinStronghold.Simulation.ContentPacks;

namespace GoblinStronghold.Simulation.Civilizations.Polities;

public readonly record struct PolityId
{
    private PolityId(string value)
    {
        Value = value;
    }

    public string Value { get; }

    public static PolityId Parse(string value) =>
        new(ContentId.Parse(value).Value);

    public static bool TryParse(string? value, out PolityId polityId)
    {
        if (ContentId.TryParse(value, out var contentId))
        {
            polityId = new PolityId(contentId.Value);
            return true;
        }
        polityId = default;
        return false;
    }

    public override string ToString() => Value ?? string.Empty;
}

public static class CorePolityIds
{
    public static PolityId PlayerTribe { get; } = PolityId.Parse("core:player-tribe");

    public static PolityId HumanVillage { get; } = PolityId.Parse("core:human-village");

    public static PolityId CaveDwarfClan(ulong factionId) =>
        PolityId.Parse($"core:cave-dwarf-clan.{factionId}");
}
