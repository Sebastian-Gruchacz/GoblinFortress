using System.Collections.ObjectModel;

namespace GoblinStronghold.Simulation.Map;

public sealed class MapValidationReport
{
    internal MapValidationReport(List<string> errors)
    {
        Errors = new ReadOnlyCollection<string>(errors);
    }

    public bool IsValid => Errors.Count == 0;

    public IReadOnlyList<string> Errors { get; }
}

public static class SwampMapValidator
{
    public static MapValidationReport Validate(GeneratedMap map)
    {
        ArgumentNullException.ThrowIfNull(map);
        var errors = new List<string>();

        if (!map.IsWithin(map.GoblinSpawn) || !map.GetCell(map.GoblinSpawn).IsTraversable)
        {
            errors.Add("Goblin spawn is outside the map or impassable.");
        }

        if (!map.IsWithin(map.HumanVillage) || !map.GetCell(map.HumanVillage).IsTraversable)
        {
            errors.Add("Human village is outside the map or impassable.");
        }

        if (map.GoblinSpawn == map.HumanVillage)
        {
            errors.Add("Goblin spawn and human village overlap.");
        }

        if (!map.HasTraversablePath(map.GoblinSpawn, map.HumanVillage))
        {
            errors.Add("No traversable route connects the two settlements.");
        }

        if (map.GetCell(map.GoblinSpawn).Moisture < 60)
        {
            errors.Add("Goblin spawn is too dry for a primitive fungal settlement.");
        }

        if (!HasWaterAccess(map, map.GoblinSpawn, radius: 3))
        {
            errors.Add("Goblin spawn has no nearby water access.");
        }

        if (!HasWaterAccess(map, map.HumanVillage, radius: 3))
        {
            errors.Add("Human village has no nearby water access.");
        }

        if (map.CountTerrain(TerrainKind.SolidGround) == 0 ||
            map.CountTerrain(TerrainKind.Mud) == 0 ||
            map.CountTerrain(TerrainKind.ShallowWater) == 0 ||
            map.CountTerrain(TerrainKind.DeepWater) == 0)
        {
            errors.Add("The swamp does not contain every foundational terrain type.");
        }

        if (map.GeneratorVersion >= 3)
        {
            for (var y = 0; y < map.Height; y++)
            {
                for (var x = 0; x < map.Width; x++)
                {
                    var cell = map.GetCell(new GridPosition(x, y));
                    if (cell.Terrain == TerrainKind.DeepWater &&
                        (cell.FloorLevel >= 0 || cell.IsTraversable))
                    {
                        errors.Add("Deep water must expose a submerged lower-level floor and be impassable.");
                        return new MapValidationReport(errors);
                    }

                    if (cell.Terrain != TerrainKind.DeepWater && cell.FloorLevel != 0)
                    {
                        errors.Add("Surface terrain and shallow water must keep their floor on the surface level.");
                        return new MapValidationReport(errors);
                    }
                }
            }
        }

        return new MapValidationReport(errors);
    }

    private static bool HasWaterAccess(GeneratedMap map, GridPosition origin, int radius)
    {
        for (var y = origin.Y - radius; y <= origin.Y + radius; y++)
        {
            for (var x = origin.X - radius; x <= origin.X + radius; x++)
            {
                var position = new GridPosition(x, y, origin.Z);
                if (!map.IsWithin(position) || Math.Abs(origin.X - x) + Math.Abs(origin.Y - y) > radius)
                {
                    continue;
                }

                var terrain = map.GetCell(position).Terrain;
                if (terrain == TerrainKind.ShallowWater)
                {
                    return true;
                }
            }
        }

        return false;
    }
}
