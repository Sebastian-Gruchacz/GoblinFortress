using System.Collections.ObjectModel;
using GoblinStronghold.Simulation.Map.Generation;

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
                        (cell.FloorLevel >= cell.SurfaceLevel || cell.IsTraversable))
                    {
                        errors.Add("Deep water must expose a submerged lower-level floor and be impassable.");
                        return new MapValidationReport(errors);
                    }

                    var expectedFloorLevel = map.GeneratorVersion >= 5
                        ? cell.SurfaceLevel
                        : 0;
                    if (cell.Terrain != TerrainKind.DeepWater &&
                        cell.FloorLevel != expectedFloorLevel)
                    {
                        errors.Add("Surface terrain and shallow water must keep their floor on the surface level.");
                        return new MapValidationReport(errors);
                    }

                    if (cell.RampDirection != TerrainRampDirection.None &&
                        !HasMatchingUphillNeighbor(map, new GridPosition(x, y), cell))
                    {
                        errors.Add("Every terrain ramp must point to a traversable surface exactly one level higher.");
                        return new MapValidationReport(errors);
                    }
                }
            }
        }

        ValidateSurfaceRoutes(map, errors);

        if (map.GeneratorVersion >= 6)
        {
            ValidateCaves(map, errors);
        }

        return new MapValidationReport(errors);
    }

    private static void ValidateSurfaceRoutes(GeneratedMap map, List<string> errors)
    {
        var expectedRouteCount = map.RoadMode switch
        {
            RoadGenerationMode.Absent => 0,
            RoadGenerationMode.ThroughRoad => 1,
            RoadGenerationMode.Junction => 2,
            _ => -1,
        };
        if (expectedRouteCount < 0 || map.SurfaceRoutes.Count != expectedRouteCount)
        {
            errors.Add("The generated logical route network does not match its road mode.");
            return;
        }

        if (map.SurfaceRoutes.Select(route => route.Role).Distinct().Count() !=
            map.SurfaceRoutes.Count)
        {
            errors.Add("Generated logical routes must have unique roles.");
            return;
        }

        foreach (var route in map.SurfaceRoutes)
        {
            if (!Enum.IsDefined(route.Role) ||
                !Enum.IsDefined(route.Entry) ||
                !Enum.IsDefined(route.Exit) ||
                !MatchesEndpoint(map, route.EntryPosition, route.Entry) ||
                !MatchesEndpoint(map, route.ExitPosition, route.Exit))
            {
                errors.Add("A generated logical route has invalid endpoint metadata.");
                return;
            }

            for (var index = 0; index < route.Path.Count; index++)
            {
                var position = route.Path[index];
                if (!map.IsTerrainSurfacePosition(position) ||
                    !map.GetColumnCell(position).IsTraversable ||
                    map.GetColumnCell(position).SurfaceRoute == SurfaceRouteKind.None)
                {
                    errors.Add("Every logical route position must use traversable road surface.");
                    return;
                }
                if (index == 0)
                {
                    continue;
                }

                var previous = route.Path[index - 1];
                if (Math.Abs(previous.X - position.X) + Math.Abs(previous.Y - position.Y) != 1 ||
                    Math.Abs(previous.Z - position.Z) > 1)
                {
                    errors.Add("Generated logical route paths must remain cardinally contiguous.");
                    return;
                }
            }
        }

        if (map.RoadMode == RoadGenerationMode.Junction)
        {
            var throughRoad = map.FindSurfaceRoute(GeneratedSurfaceRouteRole.ThroughRoad);
            var branch = map.FindSurfaceRoute(GeneratedSurfaceRouteRole.JunctionBranch);
            if (throughRoad is null || branch is null ||
                !throughRoad.Path.Contains(branch.EntryPosition))
            {
                errors.Add("The generated junction branch must start on the through-road.");
            }
        }
    }

    private static bool MatchesEndpoint(
        GeneratedMap map,
        GridPosition position,
        SurfaceRouteEndpoint endpoint) => endpoint switch
        {
            SurfaceRouteEndpoint.Junction => true,
            SurfaceRouteEndpoint.NorthEdge => position.Y == 0,
            SurfaceRouteEndpoint.EastEdge => position.X == map.Width - 1,
            SurfaceRouteEndpoint.SouthEdge => position.Y == map.Height - 1,
            SurfaceRouteEndpoint.WestEdge => position.X == 0,
            _ => false,
        };

    private static void ValidateCaves(GeneratedMap map, List<string> errors)
    {
        var expectedLevelCount = map.GeneratorVersion >= 13
            ? Math.Max(
                SwampMapGenerator.MinimumInitialCaveLevelCount,
                map.GeneratorVersion >= 18
                    ? -map.MinimumTerrainLevel - 1
                    : -map.MinimumTerrainLevel)
            : 2;
        if (map.CaveLevelCount != expectedLevelCount || map.CaveEntrances.Count == 0)
        {
            errors.Add(
                $"Maps from generator version {map.GeneratorVersion} must contain " +
                $"{expectedLevelCount} underground levels and a cave entrance.");
            return;
        }

        foreach (var passage in map.VerticalPassages)
        {
            if (passage.Upper.X != passage.Lower.X ||
                passage.Upper.Y != passage.Lower.Y ||
                passage.Upper.Z - passage.Lower.Z != 1 ||
                !map.IsTerrainTraversable(passage.Upper) ||
                !map.IsTerrainTraversable(passage.Lower))
            {
                errors.Add("Every vertical passage must connect traversable cells one level apart.");
                return;
            }
        }

        GridPosition? deepestGeneratedFloor = null;
        for (var z = -1; z >= map.DeepestCaveLevel; z--)
        {
            for (var y = 0; y < map.Height; y++)
            {
                for (var x = 0; x < map.Width; x++)
                {
                    var position = new GridPosition(x, y, z);
                    var cell = map.GetCaveCell(position);
                    if (!Enum.IsDefined(cell.Rock) ||
                        !Enum.IsDefined(cell.Kind) ||
                        !Enum.IsDefined(cell.Deposit) ||
                        !Enum.IsDefined(cell.Fluid) ||
                        (cell.Fluid != CellFluidKind.None && !cell.IsOpen))
                    {
                        errors.Add("Cave cells must use known rock materials and spatial kinds.");
                        return;
                    }

                    if (cell.IsOpen && map.IsTerrainTraversable(position) &&
                        (deepestGeneratedFloor is null || z < deepestGeneratedFloor.Value.Z))
                    {
                        deepestGeneratedFloor = position;
                    }
                }
            }
        }

        if (deepestGeneratedFloor is null ||
            map.FindTerrainPath(map.CaveEntrances[0], deepestGeneratedFloor.Value) is null)
        {
            errors.Add("The cave entrance must connect to the deepest initially open cave level.");
        }
    }

    private static bool HasMatchingUphillNeighbor(
        GeneratedMap map,
        GridPosition position,
        MapCell cell)
    {
        var uphill = cell.RampDirection switch
        {
            TerrainRampDirection.North => position with { Y = position.Y - 1 },
            TerrainRampDirection.East => position with { X = position.X + 1 },
            TerrainRampDirection.South => position with { Y = position.Y + 1 },
            TerrainRampDirection.West => position with { X = position.X - 1 },
            _ => position,
        };
        return map.IsWithin(uphill) &&
            map.GetCell(uphill).IsTraversable &&
            map.GetCell(uphill).SurfaceLevel == cell.SurfaceLevel + 1;
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
