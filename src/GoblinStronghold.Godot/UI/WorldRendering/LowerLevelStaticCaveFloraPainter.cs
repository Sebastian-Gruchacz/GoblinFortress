using Godot;
using GoblinStronghold.Simulation.Map.Generation;

namespace GoblinStronghold.GodotClient.UI.WorldRendering;

internal static class LowerLevelStaticCaveFloraPainter
{
    public static void PaintCell(Image target, Vector2I origin, CaveFloraPatch flora)
    {
        ArgumentNullException.ThrowIfNull(target);
        var shift = VariantShift(flora.Variant);
        switch (flora.Kind)
        {
            case CaveFloraKind.GlowcapCluster:
                Dot(target, origin + shift, 5, 7, 3, new Color("52d9e8"));
                Dot(target, origin + shift, 10, 9, 2, new Color("8b7ee8"));
                target.FillRect(new Rect2I(origin + shift + new Vector2I(5, 9), new Vector2I(1, 4)),
                    new Color("a8d8c8"));
                target.FillRect(new Rect2I(origin + shift + new Vector2I(10, 10), new Vector2I(1, 3)),
                    new Color("a8d8c8"));
                break;
            case CaveFloraKind.CaveMoss:
                Dot(target, origin + shift, 5, 10, 3, new Color("527a49"));
                Dot(target, origin + shift, 9, 9, 4, new Color("638f52"));
                Dot(target, origin + shift, 13, 11, 2, new Color("3f6841"));
                break;
            case CaveFloraKind.LichenPatch:
                Dot(target, origin + shift, 5, 8, 2, new Color("91a58b"));
                Dot(target, origin + shift, 9, 11, 3, new Color("687f70"));
                Dot(target, origin + shift, 12, 6, 2, new Color("a0ad8d"));
                break;
            case CaveFloraKind.GnarledCaveTree:
                target.FillRect(new Rect2I(origin + shift + new Vector2I(7, 5), new Vector2I(2, 9)),
                    new Color("756451"));
                Dot(target, origin + shift, 4, 4, 3, new Color("7b8c72"));
                Dot(target, origin + shift, 12, 3, 3, new Color("96a184"));
                break;
            case CaveFloraKind.CaveMushroomCluster:
                target.FillRect(new Rect2I(origin + shift + new Vector2I(5, 9), new Vector2I(1, 4)),
                    new Color("c3bca8"));
                target.FillRect(new Rect2I(origin + shift + new Vector2I(10, 8), new Vector2I(1, 5)),
                    new Color("aaa38f"));
                Dot(target, origin + shift, 5, 8, 4, new Color("a8785b"));
                Dot(target, origin + shift, 10, 7, 5, new Color("806957"));
                break;
        }
    }

    private static Vector2I VariantShift(byte variant) => (variant % 4) switch
    {
        0 => Vector2I.Zero,
        1 => new Vector2I(1, 0),
        2 => new Vector2I(0, 1),
        _ => new Vector2I(-1, 0),
    };

    private static void Dot(
        Image target,
        Vector2I origin,
        int x,
        int y,
        int size,
        Color color) => target.FillRect(
        new Rect2I(origin + new Vector2I(x - size / 2, y - size / 2),
            new Vector2I(size, size)),
        color);
}
