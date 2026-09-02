using GoblinStronghold.Simulation.ContentPacks;
using GoblinStronghold.Simulation.Map;

namespace GoblinStronghold.Simulation.Lighting;

public enum LightEmitterActivation : byte
{
    Always = 1,
    WhileWorking = 2,
}

public readonly record struct LightColor(float Red, float Green, float Blue);

public sealed record LightEmitterDefinition(
    ContentId Id,
    float RadiusCells,
    float Intensity,
    LightColor Color,
    float FlickerAmount,
    LightEmitterActivation Activation);

public static class LightEmitterCatalog
{
    public static readonly ContentId WallTorchId = ContentId.Parse("core:wall-torch");
    public static readonly ContentId LavaId = ContentId.Parse("core:lava");
    public static readonly ContentId BloomeryId = ContentId.Parse("core:bloomery-fire");
    public static readonly ContentId SmeltingFurnaceId =
        ContentId.Parse("core:smelting-furnace-fire");
    public static readonly ContentId CrucibleFurnaceId =
        ContentId.Parse("core:crucible-furnace-fire");

    private static readonly IReadOnlyDictionary<ContentId, LightEmitterDefinition> Definitions =
        CreateDefinitions();

    private static readonly IReadOnlyDictionary<WorldObjectKind, ContentId> WorldObjectDefinitions =
        new Dictionary<WorldObjectKind, ContentId>
        {
            [WorldObjectKind.WallTorch] = WallTorchId,
            [WorldObjectKind.Bloomery] = BloomeryId,
            [WorldObjectKind.SmeltingFurnace] = SmeltingFurnaceId,
            [WorldObjectKind.CrucibleFurnace] = CrucibleFurnaceId,
        };

    public static IReadOnlyCollection<LightEmitterDefinition> All =>
        Definitions.Values.ToArray();

    public static LightEmitterDefinition Get(ContentId id) =>
        Definitions.TryGetValue(id, out var definition)
            ? definition
            : throw new KeyNotFoundException($"Unknown light emitter definition '{id}'.");

    public static bool TryGet(
        WorldObjectKind kind,
        out LightEmitterDefinition definition)
    {
        if (WorldObjectDefinitions.TryGetValue(kind, out var id))
        {
            definition = Get(id);
            return true;
        }

        definition = null!;
        return false;
    }

    private static IReadOnlyDictionary<ContentId, LightEmitterDefinition> CreateDefinitions()
    {
        var definitions = new[]
        {
            new LightEmitterDefinition(
                WallTorchId,
                4.2f,
                0.92f,
                new LightColor(1f, 0.52f, 0.14f),
                0.1f,
                LightEmitterActivation.Always),
            new LightEmitterDefinition(
                LavaId,
                2.8f,
                0.78f,
                new LightColor(1f, 0.26f, 0.06f),
                0.04f,
                LightEmitterActivation.Always),
            new LightEmitterDefinition(
                BloomeryId,
                3.4f,
                0.74f,
                new LightColor(1f, 0.38f, 0.08f),
                0.08f,
                LightEmitterActivation.WhileWorking),
            new LightEmitterDefinition(
                SmeltingFurnaceId,
                4.1f,
                0.88f,
                new LightColor(1f, 0.3f, 0.06f),
                0.07f,
                LightEmitterActivation.WhileWorking),
            new LightEmitterDefinition(
                CrucibleFurnaceId,
                3.8f,
                0.94f,
                new LightColor(1f, 0.22f, 0.08f),
                0.06f,
                LightEmitterActivation.WhileWorking),
        };
        foreach (var definition in definitions)
        {
            if (definition.RadiusCells <= 0f ||
                definition.Intensity is <= 0f or > 1f ||
                definition.FlickerAmount is < 0f or > 1f ||
                definition.Color.Red is < 0f or > 1f ||
                definition.Color.Green is < 0f or > 1f ||
                definition.Color.Blue is < 0f or > 1f)
            {
                throw new InvalidDataException(
                    $"Light emitter definition '{definition.Id}' has invalid parameters.");
            }
        }

        return definitions.ToDictionary(definition => definition.Id);
    }
}
