using GoblinStronghold.Simulation.Construction;
using GoblinStronghold.Simulation.Map;
using GoblinStronghold.Simulation.Resources;
using Xunit;

namespace GoblinStronghold.Simulation.Tests;

public sealed class ConstructedSurfaceTests
{
    [Fact]
    public void FloorBuiltOverVoidBecomesTraversableSurface()
    {
        var engine = CreateEngine(initialWoodStock: 0);
        var position = FindBuildableVoidBesideTerrain(engine);

        Assert.False(engine.World.IsTerrainTraversable(position));
        engine.World.BuildFloor(
            position,
            new SimulationTick(1),
            stone: false,
            ResourceVariant.OakWood);

        Assert.True(engine.World.IsTerrainTraversable(position));
        Assert.Contains(engine.World.GetWorldObjectsAt(position), worldObject =>
            worldObject.Kind == WorldObjectKind.WoodenFloor);
        Assert.Contains(engine.World.GetTerrainNeighbors(position), neighbor =>
            engine.World.IsTerrainTraversable(neighbor));
    }

    [Fact]
    public void FloorBuiltAboveOpenCaveGroundBlocksItsSkyExposure()
    {
        var engine = CreateEngine(initialWoodStock: 0);
        var passage = engine.World.CreateVerticalPassageSnapshot()
            .First(candidate =>
                candidate.Kind == VerticalPassageKind.CaveMouth &&
                candidate.Lower.Z < 0 &&
                engine.World.CanBuildFloors([candidate.Upper]));

        Assert.True(engine.World.IsOpenToSky(passage.Lower));

        engine.World.BuildFloor(
            passage.Upper,
            new SimulationTick(1),
            stone: false,
            ResourceVariant.OakWood);

        Assert.False(engine.World.IsOpenToSky(passage.Lower));
        Assert.True(engine.World.IsOpenToSky(passage.Upper));
    }

    [Fact]
    public void FloorCanCoverNaturalAndExcavatedCaveGround()
    {
        var engine = CreateEngine(initialWoodStock: 0);
        var natural = EnumerateWorldPositions(engine)
            .First(position =>
                position.Z < 0 &&
                engine.Map.IsCavePosition(position) &&
                engine.Map.GetCaveCell(position).Kind == CaveCellKind.Floor &&
                !engine.World.ExcavatedCaveCells.Contains(position) &&
                engine.Map.TryGetInitialGeometry(position, out var geometry) &&
                geometry.Support == CellSupportKind.NaturalFlat &&
                geometry.Fluid == CellFluidKind.None &&
                engine.World.IsTerrainTraversable(position) &&
                !engine.World.GetWorldObjectsAt(position).Any());
        Assert.True(engine.World.CanBuildFloors([natural]));
        engine.World.BuildFloor(
            natural,
            new SimulationTick(1),
            stone: false,
            ResourceVariant.OakWood);

        var excavated = EnumerateWorldPositions(engine)
            .First(engine.World.CanExcavateRock);
        Assert.True(engine.World.TryExcavateRock(
            excavated,
            new SimulationTick(2),
            out _,
            out _,
            out _));
        Assert.True(engine.World.CanBuildFloors([excavated]));
        engine.World.BuildFloor(
            excavated,
            new SimulationTick(3),
            stone: true,
            ResourceVariant.Sandstone);

        Assert.Contains(engine.World.GetWorldObjectsAt(natural), worldObject =>
            worldObject.Kind == WorldObjectKind.WoodenFloor);
        Assert.Contains(engine.World.GetWorldObjectsAt(excavated), worldObject =>
            worldObject.Kind == WorldObjectKind.StoneFloor);
        Assert.True(engine.World.IsTerrainTraversable(excavated));
    }

    [Fact]
    public void WallsCanBeBuiltOnNaturalAndExcavatedCaveGround()
    {
        var engine = CreateEngine(initialWoodStock: 0);
        var natural = EnumerateWorldPositions(engine)
            .First(position =>
                position.Z < 0 &&
                engine.Map.IsCavePosition(position) &&
                engine.Map.GetCaveCell(position).Kind == CaveCellKind.Floor &&
                !engine.World.ExcavatedCaveCells.Contains(position) &&
                engine.Map.TryGetInitialGeometry(position, out var geometry) &&
                geometry.Support == CellSupportKind.NaturalFlat &&
                geometry.Fluid == CellFluidKind.None &&
                !engine.World.GetWorldObjectsAt(position).Any());
        Assert.True(engine.World.CanBuildWoodenWalls([natural]));

        engine.World.BuildFloor(
            natural,
            new SimulationTick(1),
            stone: true,
            ResourceVariant.Sandstone);
        Assert.True(engine.World.CanBuildWoodenWalls([natural]));
        engine.World.BuildWoodenWalls(
            [natural],
            new SimulationTick(2),
            ResourceVariant.OakWood);

        var excavated = EnumerateWorldPositions(engine)
            .First(engine.World.CanExcavateRock);
        Assert.True(engine.World.TryExcavateRock(
            excavated,
            new SimulationTick(3),
            out _,
            out _,
            out _));
        Assert.True(engine.World.CanBuildStoneWalls([excavated]));
        engine.World.BuildStoneWalls(
            [excavated],
            new SimulationTick(4),
            ResourceVariant.Granite);

        Assert.Contains(engine.World.GetWorldObjectsAt(natural), worldObject =>
            worldObject.Kind == WorldObjectKind.WoodenWall);
        Assert.Contains(engine.World.GetWorldObjectsAt(excavated), worldObject =>
            worldObject.Kind == WorldObjectKind.StoneWall);
    }

    [Fact]
    public void FloorCanBePlannedOnExcavatedHillRock()
    {
        var engine = CreateEngine(initialWoodStock: 0);
        var excavatedHill = EnumerateWorldPositions(engine)
            .First(position =>
                position.Z >= 0 &&
                engine.Map.IsHillMassPosition(position) &&
                engine.World.CanExcavateRock(position));
        Assert.True(engine.World.TryExcavateRock(
            excavatedHill,
            new SimulationTick(1),
            out _,
            out _,
            out _));

        Assert.True(engine.World.CanPlanFloorConstruction([excavatedHill]));
        engine.QueueCommand(SimulationCommand.BuildStoneFloor(
            engine.CurrentTick.Next(),
            sequence: 1,
            excavatedHill,
            excavatedHill,
            ResourceVariant.Granite));
        engine.AdvanceTicks(1);

        var site = Assert.Single(engine.CreateSnapshot().ConstructionSites);
        Assert.Equal(excavatedHill, site.Anchor);
        Assert.Equal(ConstructionKind.StoneFloor, site.Kind);
    }

    [Fact]
    public void FloorAtLevelZeroDoesNotBlockFloorOneLevelBelow()
    {
        var engine = CreateEngine(initialWoodStock: 0);
        var lower = EnumerateWorldPositions(engine)
            .Where(position => position.Z == -1)
            .First(position =>
                engine.World.CanBuildFloors([position]) &&
                engine.World.CanBuildFloors([position with { Z = 0 }]));
        var upper = lower with { Z = 0 };
        engine.World.BuildFloor(
            upper,
            new SimulationTick(1),
            stone: false,
            ResourceVariant.OakWood);

        Assert.True(engine.World.CanPlanFloorConstruction([lower]));
        engine.World.BuildFloor(
            lower,
            new SimulationTick(2),
            stone: true,
            ResourceVariant.Granite);

        Assert.Contains(engine.World.GetWorldObjectsAt(upper), worldObject =>
            worldObject.Kind == WorldObjectKind.WoodenFloor);
        Assert.Contains(engine.World.GetWorldObjectsAt(lower), worldObject =>
            worldObject.Kind == WorldObjectKind.StoneFloor);
    }

    [Fact]
    public void PrimitiveWorkshopCanBePlannedOnFloorSpanningLevelMinusOneOpening()
    {
        var engine = CreateEngine(initialWoodStock: 8);
        var passage = engine.World.CreateVerticalPassageSnapshot()
            .First(candidate =>
                candidate.Upper.Z == 0 &&
                candidate.Lower.Z == -1 &&
                engine.World.CanBuildFloors([candidate.Upper]));
        var upper = passage.Upper;
        engine.World.BuildFloor(
            upper,
            new SimulationTick(1),
            stone: false,
            ResourceVariant.OakWood);

        Assert.True(engine.World.CanBuildPrimitiveWorkshop(upper));
        engine.QueueCommand(SimulationCommand.BuildPrimitiveWorkshop(
            engine.CurrentTick.Next(),
            engine.NextAvailableCommandSequence,
            upper));
        engine.AdvanceTicks(1);

        Assert.True(engine.World.HasConstructedFloorSurface(upper));
        var site = Assert.Single(engine.CreateSnapshot().ConstructionSites);
        Assert.Equal(upper, site.Anchor);
        Assert.Equal(ConstructionKind.PrimitiveWorkshop, site.Kind);
    }

    [Fact]
    public void FloorAreaKeepsNaturalCaveGroundAndSkipsAdjacentSolidRock()
    {
        var engine = CreateEngine(initialWoodStock: 0);
        var pair = EnumerateWorldPositions(engine)
            .Where(position =>
                position.Z < 0 &&
                engine.Map.GetCaveCell(position).Kind == CaveCellKind.Floor &&
                !engine.World.ExcavatedCaveCells.Contains(position) &&
                engine.World.CanBuildFloors([position]))
            .SelectMany(floor => engine.World.GetCardinalWorldNeighbors(floor)
                .Where(engine.World.IsSolidCaveRock)
                .Select(rock => (Floor: floor, Rock: rock)))
            .First(pair => pair.Floor.Z == pair.Rock.Z);
        engine.QueueCommand(SimulationCommand.BuildWoodenFloor(
            new SimulationTick(1),
            sequence: 1,
            pair.Floor,
            pair.Rock,
            ResourceVariant.OakWood));

        engine.AdvanceTicks(1);

        var site = Assert.Single(engine.CreateSnapshot().ConstructionSites);
        Assert.Equal(pair.Floor, site.Anchor);
        Assert.Equal(pair.Floor, site.End);
        Assert.Equal(ConstructionKind.WoodenFloor, site.Kind);
    }

    [Fact]
    public void FloorConstructionAllowsBothEndsOfAVerticalPassage()
    {
        var engine = CreateEngine(initialWoodStock: 0);
        var upper = EnumerateWorldPositions(engine)
            .First(position =>
                engine.World.CanCarveRampDown(position) &&
                engine.World.CanBuildFloors([position]));
        var lower = upper with { Z = upper.Z - 1 };
        Assert.True(engine.World.TryCarveVerticalRamp(
            upper,
            carveDown: true,
            SimulationTick.Zero,
            out _,
            out _));

        Assert.True(engine.World.CanPlanFloorConstruction([upper]));
        Assert.True(engine.World.CanPlanFloorConstruction([lower]));
        engine.QueueCommand(SimulationCommand.BuildWoodenFloor(
            new SimulationTick(1),
            sequence: 1,
            lower,
            lower,
            ResourceVariant.OakWood));

        engine.AdvanceTicks(1);

        Assert.Equal(lower, Assert.Single(engine.CreateSnapshot().ConstructionSites).Anchor);
    }

    [Fact]
    public void StandaloneFloorCanBePlacedBelowAnExistingBuilding()
    {
        var engine = CreateEngine(initialWoodStock: 0);
        var anchor = EnumerateWorldPositions(engine)
            .First(engine.World.CanBuildGoblinHut);
        engine.World.BuildGoblinHut(
            anchor,
            SimulationTick.Zero,
            ResourceVariant.OakWood);

        Assert.True(engine.World.CanBuildFloors([anchor]));

        engine.World.BuildFloor(
            anchor,
            SimulationTick.Zero,
            stone: false,
            ResourceVariant.OakWood);

        Assert.Contains(engine.World.GetWorldObjectsAt(anchor), worldObject =>
            worldObject.Kind == WorldObjectKind.WoodenFloor);
        Assert.Contains(engine.World.GetWorldObjectsAt(anchor), worldObject =>
            worldObject.Kind == WorldObjectKind.GoblinHut);
    }

    [Fact]
    public void ConstructedFloorLetsGoblinTakeItsNextStepSooner()
    {
        var floored = CreateEngine(initialWoodStock: 0);
        var actor = Assert.Single(floored.CreateSnapshot().Actors);
        var target = EnumerateWorldPositions(floored)
            .Select(position => new
            {
                Position = position,
                Route = floored.Navigation.FindPath(actor.Position, position),
            })
            .First(candidate => candidate.Route is { Count: > 0 } &&
                floored.World.CanBuildFloors([candidate.Route[0]]));
        var raw = SimulationEngine.Load(
            floored.Save(),
            SimulationDefinitions.Foundation);
        floored.World.BuildFloor(
            target.Route![0],
            new SimulationTick(1),
            stone: false,
            ResourceVariant.OakWood);
        floored.QueueCommand(SimulationCommand.Move(
            floored.CurrentTick.Next(),
            floored.NextAvailableCommandSequence,
            actor.Id,
            target.Position));
        raw.QueueCommand(SimulationCommand.Move(
            raw.CurrentTick.Next(),
            raw.NextAvailableCommandSequence,
            actor.Id,
            target.Position));

        floored.AdvanceTicks(8);
        raw.AdvanceTicks(8);

        Assert.Equal(target.Route[0], Assert.Single(floored.CreateSnapshot().Actors).Position);
        Assert.Equal(actor.Position, Assert.Single(raw.CreateSnapshot().Actors).Position);
    }

    [Fact]
    public void ConstructedRampConnectsLowerAndUpperLevelsAndSurvivesSaveLoad()
    {
        var engine = CreateEngine(initialWoodStock: 0);
        var (lower, upper) = FindRampPlacement(engine);

        engine.World.BuildRamp(
            lower,
            upper,
            new SimulationTick(1),
            stone: false,
            ResourceVariant.OakWood);

        Assert.Contains(upper, engine.World.GetTerrainNeighbors(lower));
        Assert.Contains(lower, engine.World.GetTerrainNeighbors(upper));
        var ramp = Assert.Single(engine.World.GetWorldObjectsAt(lower), worldObject =>
            worldObject.Kind == WorldObjectKind.WoodenRamp);
        Assert.Equal(ResourceVariant.OakWood, ramp.MaterialVariant);

        var restored = SimulationEngine.Load(
            engine.Save(),
            SimulationDefinitions.Foundation);
        Assert.Contains(upper, restored.World.GetTerrainNeighbors(lower));
        Assert.Contains(lower, restored.World.GetTerrainNeighbors(upper));
    }

    [Fact]
    public void WoodenLadderConnectsAWatchtowerPlatformAndSurvivesSaveLoad()
    {
        var engine = CreateEngine(initialWoodStock: 0);
        var actorPosition = Assert.Single(engine.CreateSnapshot().Actors).Position;
        var towerAnchor = EnumerateWorldPositions(engine)
            .Where(position => position.Z == 0)
            .First(position =>
            {
                var footprint = SimulationCommand.GetAreaCells(
                    position,
                    position with { X = position.X + 1, Y = position.Y + 1 });
                return !footprint.Contains(actorPosition) &&
                    engine.World.CanBuildWoodenWatchtower(position) &&
                    footprint.Any(platform => engine.World.GetCardinalWorldNeighbors(platform)
                        .Any(lower => !footprint.Contains(lower) &&
                            engine.World.IsTerrainTraversable(lower)));
            });
        engine.World.BuildWoodenWatchtower(
            towerAnchor,
            new SimulationTick(1),
            ResourceVariant.OakWood);
        var watchtower = Assert.Single(engine.World.GetWorldObjectsAt(towerAnchor),
            worldObject => worldObject.Kind == WorldObjectKind.WoodenWatchtower);
        var placement = watchtower.GetAbsoluteParts()
            .Where(item => item.Part.Kind == WorldObjectPartKind.WatchtowerPlatform)
            .SelectMany(item => engine.World.GetCardinalWorldNeighbors(
                    item.Position with { Z = item.Position.Z - 1 })
                .Select(lower => (Lower: lower, Upper: item.Position)))
            .First(candidate => engine.World.CanBuildWoodenLadder(
                candidate.Lower,
                candidate.Upper));

        Assert.DoesNotContain(
            placement.Upper,
            engine.World.GetTerrainNeighbors(placement.Lower));
        engine.World.BuildWoodenLadder(
            placement.Lower,
            placement.Upper,
            new SimulationTick(2),
            ResourceVariant.OakWood);

        Assert.Contains(placement.Upper, engine.World.GetTerrainNeighbors(placement.Lower));
        Assert.Contains(placement.Lower, engine.World.GetTerrainNeighbors(placement.Upper));
        var ladder = Assert.Single(engine.World.GetWorldObjectsAt(placement.Lower),
            worldObject => worldObject.Kind == WorldObjectKind.WoodenLadder);
        Assert.Equal(2, ladder.Parts.Count);
        Assert.All(ladder.Parts, part =>
        {
            Assert.Equal(SpatialOccupancyChannel.Fixture, part.Channel);
            Assert.Equal(WorldObjectPartKind.Ladder, part.Kind);
        });
        Assert.Equal(ResourceVariant.OakWood, ladder.MaterialVariant);
        var route = engine.World.FindTerrainPath(actorPosition, placement.Upper);
        Assert.NotNull(route);
        Assert.Contains(placement.Lower, route);
        Assert.Equal(placement.Upper, route[^1]);

        engine = SimulationEngine.Load(engine.Save(), SimulationDefinitions.Foundation);
        Assert.Contains(placement.Upper, engine.World.GetTerrainNeighbors(placement.Lower));
        Assert.True(ConstructionDismantlingPolicy.TryGetConstructionKind(
            ladder.Kind,
            out var construction));
        Assert.Equal(ConstructionKind.WoodenLadder, construction);

        engine.World.DismantleWorldObject(ladder.Id, new SimulationTick(3));
        Assert.DoesNotContain(placement.Upper, engine.World.GetTerrainNeighbors(placement.Lower));
        Assert.True(engine.World.CanBuildWoodenLadder(placement.Lower, placement.Upper));
    }

    [Fact]
    public void FloorToolTurnsNaturalSlopeIntoMatchingMaterialRamp()
    {
        var engine = CreateEngine(initialWoodStock: 2);
        var (lower, upper) = FindCoverableNaturalRamp(engine);
        engine.Visibility.Reveal([lower, upper], radius: 1);

        engine.QueueCommand(SimulationCommand.BuildWoodenFloor(
            new SimulationTick(1),
            sequence: 1,
            lower,
            lower,
            ResourceVariant.OakWood));
        engine.AdvanceTicks(1);

        var site = Assert.Single(engine.CreateSnapshot().ConstructionSites);
        Assert.Equal(ConstructionKind.WoodenRamp, site.Kind);
        Assert.Equal(lower, site.Anchor);
        Assert.Equal(upper, site.End);
        var material = Assert.Single(site.Materials);
        Assert.Equal(ResourceVariant.OakWood, material.Variant);
        Assert.Equal(2, material.RequiredQuantity);
    }

    [Fact]
    public void ConstructedCoverMakesNaturalSlopeCleanableInsteadOfLooseDirt()
    {
        var engine = CreateEngine(initialWoodStock: 0);
        var (lower, upper) = FindCoverableNaturalRamp(engine);
        Assert.True(FloorCoveringPlacementPolicy.TryResolve(
            engine.World,
            ConstructionKind.StoneFloor,
            lower,
            out var placement));
        Assert.Equal(ConstructionKind.StoneRamp, placement.Kind);

        engine.World.BuildRamp(
            lower,
            upper,
            new SimulationTick(1),
            stone: true,
            ResourceVariant.Granite);

        Assert.True(engine.World.HasConstructedCleanableSurface(lower));
        Assert.Contains(engine.World.GetWorldObjectsAt(lower), worldObject =>
            worldObject.Kind == WorldObjectKind.StoneRamp &&
            worldObject.MaterialVariant == ResourceVariant.Granite);

        var restored = SimulationEngine.Load(engine.Save(), SimulationDefinitions.Foundation);
        Assert.True(restored.World.HasConstructedCleanableSurface(lower));
    }

    [Fact]
    public void RampConstructionCommandKeepsInferredEndpointAndMaterial()
    {
        var engine = CreateEngine(initialWoodStock: 2);
        var (lower, upper) = FindRampPlacement(engine);
        engine.QueueCommand(SimulationCommand.BuildWoodenRamp(
            new SimulationTick(1),
            sequence: 1,
            lower,
            upper,
            ResourceVariant.OakWood));

        engine.AdvanceTicks(1);

        var site = Assert.Single(engine.CreateSnapshot().ConstructionSites);
        Assert.Equal(ConstructionKind.WoodenRamp, site.Kind);
        Assert.Equal(lower, site.Anchor);
        Assert.Equal(upper, site.End);
        var material = Assert.Single(site.Materials);
        Assert.Equal(ResourceKind.Wood, material.Resource);
        Assert.Equal(ResourceVariant.OakWood, material.Variant);
        Assert.Equal(2, material.RequiredQuantity);
    }

    private static SimulationEngine CreateEngine(int initialWoodStock) =>
        SimulationEngine.Create(
            new WorldSeed(0x5355524641434553UL),
            SimulationDefinitions.Foundation,
            initialGoblinCount: 1,
            initialFoodStock: 0,
            initialWoodStock: initialWoodStock);

    private static GridPosition FindBuildableVoidBesideTerrain(SimulationEngine engine) =>
        EnumerateWorldPositions(engine)
            .First(position =>
                engine.Map.TryGetInitialGeometry(position, out var geometry) &&
                !geometry.IsSolid &&
                geometry.Support == CellSupportKind.None &&
                geometry.Fluid == CellFluidKind.None &&
                engine.World.CanBuildFloors([position]) &&
                engine.World.GetCardinalWorldNeighbors(position)
                    .Any(engine.World.IsTerrainTraversable));

    private static (GridPosition Lower, GridPosition Upper) FindRampPlacement(
        SimulationEngine engine)
    {
        foreach (var lower in EnumerateWorldPositions(engine))
        {
            if (engine.World.TryInferBuildRamp(lower, out var upper))
            {
                return (lower, upper);
            }
        }

        throw new InvalidOperationException("No inferred ramp placement was found.");
    }

    private static (GridPosition Lower, GridPosition Upper) FindCoverableNaturalRamp(
        SimulationEngine engine) =>
        (from y in Enumerable.Range(0, engine.Map.Height)
         from x in Enumerable.Range(0, engine.Map.Width)
         let lower = engine.Map.GetTerrainSurfacePosition(new GridPosition(x, y))
         where engine.World.TryGetNaturalRampUpper(lower, out _)
         let upper = GetNaturalRampUpper(engine, lower)
         where engine.World.CanBuildRamp(lower, upper)
         select (lower, upper)).First();

    private static GridPosition GetNaturalRampUpper(
        SimulationEngine engine,
        GridPosition lower)
    {
        Assert.True(engine.World.TryGetNaturalRampUpper(lower, out var upper));
        return upper;
    }

    private static IEnumerable<GridPosition> EnumerateWorldPositions(SimulationEngine engine) =>
        from z in Enumerable.Range(
            engine.Map.MinimumWorldLevel,
            engine.Map.MaximumWorldLevel - engine.Map.MinimumWorldLevel + 1)
        from y in Enumerable.Range(0, engine.Map.Height)
        from x in Enumerable.Range(0, engine.Map.Width)
        select new GridPosition(x, y, z);
}
