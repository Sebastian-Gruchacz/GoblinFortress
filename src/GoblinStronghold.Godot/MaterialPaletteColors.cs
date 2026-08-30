using Godot;
using GoblinStronghold.Simulation.Resources;

namespace GoblinStronghold.GodotClient;

internal readonly record struct MaterialPaletteColors(
    Color Edge,
    Color Shadow,
    Color Midtone,
    Color Highlight)
{
    public static MaterialPaletteColors For(ResourceVariant variant) =>
        From(MaterialCatalog.Get(variant));

    public static MaterialPaletteColors For(string id) =>
        From(MaterialCatalog.Get(id));

    private static MaterialPaletteColors From(MaterialDefinition material) => new(
        Color.FromHtml(material.Palette.Edge),
        Color.FromHtml(material.Palette.KeyColors[0]),
        Color.FromHtml(material.Palette.KeyColors[1]),
        Color.FromHtml(material.Palette.KeyColors[2]));
}
