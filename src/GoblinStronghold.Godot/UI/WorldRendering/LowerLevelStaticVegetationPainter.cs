using Godot;
using GoblinStronghold.Simulation.Map;

namespace GoblinStronghold.GodotClient.UI.WorldRendering;

internal static class LowerLevelStaticVegetationPainter
{
    public static void PaintCell(
        Image target,
        Vector2I origin,
        PlantPatchSnapshot plant,
        Image environmentAtlas)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(environmentAtlas);
        if (plant.Biomass <= 0 && plant.Kind != PlantKind.BerryBush)
        {
            return;
        }

        var sprite = plant.Kind switch
        {
            PlantKind.BerryBush when plant.Biomass > 0 =>
                EnvironmentSprite.FruitingBerryBush,
            PlantKind.BerryBush => EnvironmentSprite.BareBerryBush,
            PlantKind.MushroomCluster => EnvironmentSprite.MushroomCluster,
            PlantKind.EdibleRoots => EnvironmentSprite.EdibleRoots,
            PlantKind.ReedBed => EnvironmentSprite.Reeds,
            PlantKind.FishShoal => EnvironmentSprite.FishShoal,
            _ => throw new ArgumentOutOfRangeException(nameof(plant), plant.Kind, null),
        };
        var cellSize = LowerLevelChunkTextureCache.PixelsPerCell;
        var spriteSize = plant.Kind is PlantKind.MushroomCluster or PlantKind.EdibleRoots
            ? cellSize * 3 / 5
            : cellSize;
        using var image = environmentAtlas.GetRegion(
            EnvironmentSprites.GetRegionFromImage(environmentAtlas, sprite));
        image.Resize(spriteSize, spriteSize, Image.Interpolation.Bilinear);
        image.Convert(Image.Format.Rgba8);
        var inset = (cellSize - spriteSize) / 2;
        target.BlendRect(
            image,
            new Rect2I(Vector2I.Zero, image.GetSize()),
            origin + new Vector2I(inset, inset));
    }
}
