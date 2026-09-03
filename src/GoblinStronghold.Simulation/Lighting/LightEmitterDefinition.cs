using GoblinStronghold.Simulation.ContentPacks;
using GoblinStronghold.Simulation.Map;

namespace GoblinStronghold.Simulation.Lighting;

public enum LightEmitterActivityRequirement : byte
{
    Always = 1,
    WhileWorking = 2,
    WhileCarried = 3,
    ActorTrait = 4,
}

public enum LightEmitterFuelRequirement : byte
{
    None = 0,
    WorkOrderInput = 1,
    StoredFuel = 2,
    PortableCharge = 3,
}

public enum LightEmitterAttachment : byte
{
    World = 1,
    Actor = 2,
}

public readonly record struct LightEmitterActivation(
    LightEmitterActivityRequirement Activity,
    LightEmitterFuelRequirement Fuel);

public readonly record struct LightColor(float Red, float Green, float Blue);

public sealed record LightEmitterDefinition(
    ContentId Id,
    float RadiusCells,
    float Intensity,
    LightColor Color,
    float FlickerAmount,
    LightEmitterActivation Activation,
    LightEmitterAttachment Attachment);

public static class LightEmitterCatalog
{
    public const float MaximumSupportedIntensity = 2f;

    public static readonly ContentId WallTorchId = ContentId.Parse("core:wall-torch");
    public static readonly ContentId LavaId = ContentId.Parse("core:lava");
    public static readonly ContentId CaveGlowcapId = ContentId.Parse("core:cave-glowcap");
    public static readonly ContentId BloomeryId = ContentId.Parse("core:bloomery-fire");
    public static readonly ContentId SmeltingFurnaceId =
        ContentId.Parse("core:smelting-furnace-fire");
    public static readonly ContentId CrucibleFurnaceId =
        ContentId.Parse("core:crucible-furnace-fire");
    public static readonly ContentId CookingFireId = ContentId.Parse("core:cooking-fire");

    private static readonly IReadOnlyDictionary<ContentId, LightEmitterDefinition> Definitions =
        CreateDefinitions();

    private static readonly IReadOnlyDictionary<WorldObjectKind, ContentId> WorldObjectDefinitions =
        new Dictionary<WorldObjectKind, ContentId>
        {
            [WorldObjectKind.WallTorch] = WallTorchId,
            [WorldObjectKind.StandingTorch] = WallTorchId,
            [WorldObjectKind.Bloomery] = BloomeryId,
            [WorldObjectKind.SmeltingFurnace] = SmeltingFurnaceId,
            [WorldObjectKind.CrucibleFurnace] = CrucibleFurnaceId,
            [WorldObjectKind.CookingFire] = CookingFireId,
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
                6.3f,
                1.38f,
                new LightColor(1f, 0.52f, 0.14f),
                0.1f,
                new LightEmitterActivation(
                    LightEmitterActivityRequirement.Always,
                    LightEmitterFuelRequirement.None),
                LightEmitterAttachment.World),
            new LightEmitterDefinition(
                LavaId,
                2.8f,
                0.78f,
                new LightColor(1f, 0.26f, 0.06f),
                0.04f,
                new LightEmitterActivation(
                    LightEmitterActivityRequirement.Always,
                    LightEmitterFuelRequirement.None),
                LightEmitterAttachment.World),
            new LightEmitterDefinition(
                CaveGlowcapId,
                2.2f,
                0.36f,
                new LightColor(0.2f, 0.82f, 0.92f),
                0.02f,
                new LightEmitterActivation(
                    LightEmitterActivityRequirement.Always,
                    LightEmitterFuelRequirement.None),
                LightEmitterAttachment.World),
            new LightEmitterDefinition(
                BloomeryId,
                3.4f,
                0.74f,
                new LightColor(1f, 0.38f, 0.08f),
                0.08f,
                new LightEmitterActivation(
                    LightEmitterActivityRequirement.WhileWorking,
                    LightEmitterFuelRequirement.WorkOrderInput),
                LightEmitterAttachment.World),
            new LightEmitterDefinition(
                SmeltingFurnaceId,
                4.1f,
                0.88f,
                new LightColor(1f, 0.3f, 0.06f),
                0.07f,
                new LightEmitterActivation(
                    LightEmitterActivityRequirement.WhileWorking,
                    LightEmitterFuelRequirement.WorkOrderInput),
                LightEmitterAttachment.World),
            new LightEmitterDefinition(
                CrucibleFurnaceId,
                3.8f,
                0.94f,
                new LightColor(1f, 0.22f, 0.08f),
                0.06f,
                new LightEmitterActivation(
                    LightEmitterActivityRequirement.WhileWorking,
                    LightEmitterFuelRequirement.WorkOrderInput),
                LightEmitterAttachment.World),
            new LightEmitterDefinition(
                CookingFireId,
                3.6f,
                0.82f,
                new LightColor(1f, 0.46f, 0.1f),
                0.12f,
                new LightEmitterActivation(
                    LightEmitterActivityRequirement.WhileWorking,
                    LightEmitterFuelRequirement.WorkOrderInput),
                LightEmitterAttachment.World),
        };
        foreach (var definition in definitions)
        {
            if (definition.RadiusCells <= 0f ||
                definition.Intensity is <= 0f or > MaximumSupportedIntensity ||
                definition.FlickerAmount is < 0f or > 1f ||
                definition.Color.Red is < 0f or > 1f ||
                definition.Color.Green is < 0f or > 1f ||
                definition.Color.Blue is < 0f or > 1f ||
                !LightEmitterActivationPolicy.IsValid(definition))
            {
                throw new InvalidDataException(
                    $"Light emitter definition '{definition.Id}' has invalid parameters.");
            }
        }

        return definitions.ToDictionary(definition => definition.Id);
    }
}
