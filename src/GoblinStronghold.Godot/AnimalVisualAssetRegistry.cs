using Godot;
using GoblinStronghold.Simulation.Animals;
using GoblinStronghold.Simulation.ContentPacks;

namespace GoblinStronghold.GodotClient;

internal static class AnimalVisualAssetRegistry
{
    internal static readonly ContentId ProceduralHareRenderer =
        ContentId.Parse("core:procedural-hare");
    internal static readonly ContentId ProceduralBoarRenderer =
        ContentId.Parse("core:procedural-boar");
    internal static readonly ContentId AtlasSpriteRenderer =
        ContentId.Parse("core:atlas-sprite");
    internal static readonly ContentId UndergroundFaunaAtlas =
        ContentId.Parse("core:underground-fauna");
    internal static readonly ContentId CaveSpiderSprite =
        ContentId.Parse("core:cave-spider");

    internal static void Validate(IAnimalSpeciesCatalog catalog)
    {
        foreach (var species in catalog.All)
        {
            Validate(species);
        }
    }

    private static void Validate(AnimalSpeciesDefinition species)
    {
        var visual = species.Visual;
        if (visual.RendererId == ProceduralHareRenderer)
        {
            RequirePalette(species, "body", "accent", "eye", "threat");
            RequireNoAtlas(species);
            return;
        }
        if (visual.RendererId == ProceduralBoarRenderer)
        {
            RequirePalette(species, "body", "accent", "tusk", "threat");
            RequireNoAtlas(species);
            return;
        }
        if (visual.RendererId == AtlasSpriteRenderer &&
            visual.AtlasId == UndergroundFaunaAtlas &&
            visual.SpriteId == CaveSpiderSprite)
        {
            RequirePalette(species, "edge", "shadow", "midtone", "highlight", "threat");
            if (!ResourceLoader.Exists(UndergroundSprites.FaunaAtlasPath))
            {
                throw new InvalidDataException(
                    $"Animal species '{species.Id}' requires missing atlas " +
                    $"'{visual.AtlasId}'.");
            }
            return;
        }

        throw new InvalidDataException(
            $"Animal species '{species.Id}' references unsupported renderer, atlas, or sprite " +
            $"'{visual.RendererId}/{visual.AtlasId}/{visual.SpriteId}'.");
    }

    private static void RequireNoAtlas(AnimalSpeciesDefinition species)
    {
        if (species.Visual.AtlasId is not null)
        {
            throw new InvalidDataException(
                $"Procedural animal species '{species.Id}' cannot declare an atlas.");
        }
    }

    private static void RequirePalette(
        AnimalSpeciesDefinition species,
        params string[] requiredKeys)
    {
        var missing = requiredKeys.Where(key => !species.Visual.Palette.ContainsKey(key))
            .ToArray();
        if (missing.Length > 0)
        {
            throw new InvalidDataException(
                $"Animal species '{species.Id}' is missing palette keys: " +
                string.Join(", ", missing));
        }
    }
}
