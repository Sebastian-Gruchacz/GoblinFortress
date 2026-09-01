using System.Reflection;

namespace GoblinStronghold.Simulation.ContentPacks;

public static class CoreContentPack
{
    private const string ResourcePrefix = "GoblinStronghold.Simulation";
    private static readonly Lazy<ContentPack> State = new(Load);

    private static readonly IReadOnlyDictionary<string, string> Resources =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [ContentPack.ManifestPath] = $"{ResourcePrefix}.ContentPacks.core.manifest.json",
            ["content/animal-species.json"] =
                $"{ResourcePrefix}.Content.animal-species.json",
            ["content/crafting-recipes.json"] =
                $"{ResourcePrefix}.Content.crafting-recipes.json",
            ["content/materials.json"] = $"{ResourcePrefix}.Content.materials.json",
            ["content/workshops.json"] = $"{ResourcePrefix}.Content.workshops.json",
            ["localization/en/interface.json"] =
                $"{ResourcePrefix}.Localization.en.interface.json",
            ["localization/en/materials.json"] =
                $"{ResourcePrefix}.Localization.en.materials.json",
            ["localization/en/recipes.json"] =
                $"{ResourcePrefix}.Localization.en.recipes.json",
            ["localization/en/workshops.json"] =
                $"{ResourcePrefix}.Localization.en.workshops.json",
            ["localization/pl/interface.json"] =
                $"{ResourcePrefix}.Localization.pl.interface.json",
            ["localization/pl/materials.json"] =
                $"{ResourcePrefix}.Localization.pl.materials.json",
            ["localization/pl/recipes.json"] =
                $"{ResourcePrefix}.Localization.pl.recipes.json",
            ["localization/pl/workshops.json"] =
                $"{ResourcePrefix}.Localization.pl.workshops.json",
        };

    public static ContentPack Pack => State.Value;

    private static ContentPack Load()
    {
        var assembly = typeof(CoreContentPack).Assembly;
        var files = Resources.ToDictionary(
            item => item.Key,
            item => ReadEmbeddedResource(assembly, item.Value),
            StringComparer.OrdinalIgnoreCase);
        return ContentPack.Create("embedded core-pack", files);
    }

    private static byte[] ReadEmbeddedResource(Assembly assembly, string resourceName)
    {
        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException(
                $"Embedded core-pack resource '{resourceName}' is missing.");
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }
}
