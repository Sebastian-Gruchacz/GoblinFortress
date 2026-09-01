using GoblinStronghold.Simulation.Map;

namespace GoblinStronghold.Simulation.Animals;

public static class AnimalHabitatPolicy
{
    public static bool Accepts(
        AnimalSpeciesDefinition species,
        GeneratedMap map,
        WorldMapState world,
        GridPosition position)
    {
        ArgumentNullException.ThrowIfNull(species);
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(world);

        if (!map.IsColumnWithin(position))
        {
            return false;
        }
        if (species.Habitat.Kind == AnimalHabitatKind.Cave)
        {
            return position.Z <= -species.Habitat.MinimumDepthBelowSurface &&
                map.IsCavePosition(position) && world.IsTerrainTraversable(position);
        }
        if (position.Z != 0)
        {
            return false;
        }

        var cell = map.GetCell(position);
        return species.Habitat.Kind switch
        {
            AnimalHabitatKind.FertileGround =>
                cell.IsTraversable && cell.Terrain == TerrainKind.SolidGround &&
                cell.Fertility >= 45,
            AnimalHabitatKind.Wetland =>
                cell.IsTraversable && cell.Moisture >= 60 &&
                cell.Terrain is TerrainKind.Mud or TerrainKind.ShallowWater,
            _ => false,
        };
    }

    public static IEnumerable<GridPosition> GetTraversableNeighbors(
        AnimalSpeciesDefinition species,
        GeneratedMap map,
        WorldMapState world,
        GridPosition position) => species.Habitat.Kind == AnimalHabitatKind.Cave
        ? world.GetTerrainNeighbors(position).Where(candidate =>
            Accepts(species, map, world, candidate))
        : map.GetCardinalNeighbors(position).Where(candidate =>
            Accepts(species, map, world, candidate) &&
            world.IsSurfaceTraversable(candidate) &&
            map.CanTraverseSurfaceEdge(position, candidate));
}
