using GoblinStronghold.Simulation.Map;
using System.Text.Json.Nodes;
using Xunit;

namespace GoblinStronghold.Simulation.Tests;

public sealed class NavigationKnowledgeTests
{
    [Fact]
    public void EdgeIdentityIsIndependentOfTravelDirection()
    {
        var lower = new GridPosition(4, 5, -1);
        var upperRamp = new GridPosition(5, 5, 0);

        Assert.Equal(
            NavigationEdge.Between(lower, upperRamp),
            NavigationEdge.Between(upperRamp, lower));
        Assert.Throws<ArgumentException>(() => NavigationEdge.Between(
            lower,
            new GridPosition(6, 5, -1)));
    }

    [Fact]
    public void NewerObservationReplacesOldBeliefWithoutWorldKnowledge()
    {
        var knowledge = new NavigationKnowledgeState();
        var edge = NavigationEdge.Between(
            new GridPosition(1, 1),
            new GridPosition(2, 1));
        knowledge.Observe(
            new EntityId(2),
            edge.First,
            edge.Second,
            NavigationBeliefStatus.Passable,
            new SimulationTick(10));
        knowledge.Observe(
            new EntityId(2),
            edge.Second,
            edge.First,
            NavigationBeliefStatus.Blocked,
            new SimulationTick(20));

        Assert.True(knowledge.TryGet(edge, out var belief));
        Assert.Equal(NavigationBeliefStatus.Blocked, belief.Status);
        Assert.Equal(new SimulationTick(20), belief.ObservedAt);
        Assert.True(knowledge.HasBlockedBeliefs);
    }

    [Fact]
    public void PersonalBeliefOverridesTribalFallbackForTheSameEdge()
    {
        var personal = new NavigationKnowledgeState();
        var tribe = new NavigationKnowledgeState();
        var from = new GridPosition(1, 1);
        var to = new GridPosition(2, 1);
        tribe.Observe(
            new EntityId(2),
            from,
            to,
            NavigationBeliefStatus.Blocked,
            new SimulationTick(10));
        personal.Observe(
            new EntityId(1),
            from,
            to,
            NavigationBeliefStatus.Passable,
            new SimulationTick(5));

        Assert.False(personal.HasBlockedBeliefs);
        Assert.True(tribe.HasBlockedBeliefs);
        Assert.True(personal.AllowsTraversal(from, to, tribe));
        Assert.False(new NavigationKnowledgeState().AllowsTraversal(from, to, tribe));
    }

    [Fact]
    public void NewerPassableObservationRemovesRoutingBlock()
    {
        var knowledge = new NavigationKnowledgeState();
        var from = new GridPosition(5, 5);
        var to = new GridPosition(6, 5);
        knowledge.Observe(
            new EntityId(1),
            from,
            to,
            NavigationBeliefStatus.Blocked,
            new SimulationTick(10));
        knowledge.Observe(
            new EntityId(1),
            from,
            to,
            NavigationBeliefStatus.Passable,
            new SimulationTick(20));

        Assert.False(knowledge.HasBlockedBeliefs);
        Assert.True(knowledge.AllowsTraversal(from, to));
    }

    [Fact]
    public void DirectObservationWinsSameTickConflictAndOldReportCannotRevertIt()
    {
        var personal = new NavigationKnowledgeState();
        var tribe = new NavigationKnowledgeState();
        var from = new GridPosition(3, 3);
        var to = new GridPosition(4, 3);
        var sameTickReport = new NavigationBelief(
            NavigationEdge.Between(from, to),
            NavigationBeliefStatus.Passable,
            new SimulationTick(50),
            new SimulationTick(50),
            new EntityId(2),
            Confidence: 100,
            IsDirectObservation: false);
        Assert.True(personal.ReceiveReport(sameTickReport, new SimulationTick(50)));
        var direct = personal.Observe(
            new EntityId(7),
            from,
            to,
            NavigationBeliefStatus.Blocked,
            new SimulationTick(50),
            confidence: 90);
        tribe.ReceiveReport(direct, new SimulationTick(65));
        var oldPassable = new NavigationBelief(
            NavigationEdge.Between(from, to),
            NavigationBeliefStatus.Passable,
            new SimulationTick(40),
            new SimulationTick(40),
            new EntityId(2),
            Confidence: 100,
            IsDirectObservation: true);

        Assert.False(tribe.ReceiveReport(oldPassable, new SimulationTick(70)));
        Assert.True(tribe.TryGet(direct.Edge, out var shared));
        Assert.Equal(NavigationBeliefStatus.Blocked, shared.Status);
        Assert.False(shared.IsDirectObservation);
        Assert.Equal(new SimulationTick(50), shared.ObservedAt);
        Assert.Equal(new SimulationTick(65), shared.ReceivedAt);
    }

    [Theory]
    [InlineData(110, NavigationBeliefFreshness.Current)]
    [InlineData(111, NavigationBeliefFreshness.Aging)]
    [InlineData(130, NavigationBeliefFreshness.Aging)]
    [InlineData(131, NavigationBeliefFreshness.Stale)]
    public void FreshnessUsesObservationAgeRatherThanReportDelivery(
        long currentTick,
        NavigationBeliefFreshness expected)
    {
        var knowledge = new NavigationKnowledgeState();
        var observed = knowledge.Observe(
            new EntityId(1),
            new GridPosition(1, 2),
            new GridPosition(1, 3),
            NavigationBeliefStatus.Passable,
            new SimulationTick(100));
        knowledge.ReceiveReport(observed, new SimulationTick(109));
        Assert.True(knowledge.TryGet(observed.Edge, out var report));

        Assert.Equal(
            expected,
            report.GetFreshness(
                new SimulationTick(currentTick),
                currentDurationTicks: 10,
                agingDurationTicks: 20));
    }

    [Fact]
    public void SnapshotOrderDoesNotDependOnInsertionOrder()
    {
        var first = new NavigationKnowledgeState();
        var second = new NavigationKnowledgeState();
        var observations = new[]
        {
            (new GridPosition(8, 2), new GridPosition(8, 3)),
            (new GridPosition(2, 4, -1), new GridPosition(2, 4, 0)),
            (new GridPosition(1, 1), new GridPosition(2, 1)),
        };
        foreach (var (from, to) in observations)
        {
            first.Observe(
                new EntityId(1),
                from,
                to,
                NavigationBeliefStatus.Passable,
                new SimulationTick(1));
        }
        foreach (var (from, to) in observations.Reverse())
        {
            second.Observe(
                new EntityId(1),
                from,
                to,
                NavigationBeliefStatus.Passable,
                new SimulationTick(1));
        }

        Assert.Equal(first.CreateSnapshot().ToArray(), second.CreateSnapshot().ToArray());
    }

    [Fact]
    public void PersonalBlockedBeliefCanRouteAroundAnAuthoritativelyOpenEdge()
    {
        var seed = new WorldSeed(0x42454C494546UL);
        var engine = SimulationEngine.Create(
            seed,
            SimulationDefinitions.Foundation,
            SwampMapGenerator.Generate(seed, 64, 64),
            initialGoblinCount: 0,
            initialFoodStock: 0);
        var world = engine.World;
        var navigation = engine.Navigation;
        var square = FindTraversableSquare(world);
        var knowledge = new NavigationKnowledgeState();
        knowledge.Observe(
            new EntityId(1),
            square.Start,
            square.Destination,
            NavigationBeliefStatus.Blocked,
            SimulationTick.Zero);

        var route = navigation.FindPath(
            square.Start,
            square.Destination,
            knowledge.AllowsTraversal);

        Assert.NotNull(route);
        Assert.True(route.Count >= 3);
        var steps = route.Prepend(square.Start).Zip(route, NavigationEdge.Between);
        Assert.DoesNotContain(
            NavigationEdge.Between(square.Start, square.Destination),
            steps);
    }

    [Fact]
    public void PendingPersonalReportBecomesTribalKnowledgeOnlyAtGoblinShelter()
    {
        var seed = new WorldSeed(0x5245504F5254UL);
        var engine = SimulationEngine.Create(
            seed,
            SimulationDefinitions.Foundation,
            initialGoblinCount: 1,
            initialFoodStock: 8);
        var shelter = engine.World.CreateWorldObjectSnapshot()
            .Where(worldObject =>
                worldObject.Kind == WorldObjectKind.GoblinHut &&
                worldObject.Owner == WorldObjectOwner.GoblinTribe)
            .SelectMany(worldObject => worldObject.GetAbsoluteParts())
            .First(part =>
                part.Part.Kind is WorldObjectPartKind.Floor or WorldObjectPartKind.Door &&
                engine.World.IsTerrainTraversable(part.Position))
            .Position;
        var neighboringCell = shelter with { X = shelter.X + 1 };
        var save = JsonNode.Parse(engine.Save())!.AsObject();
        var actor = save["actors"]![0]!.AsObject();
        actor["x"] = shelter.X;
        actor["y"] = shelter.Y;
        actor["z"] = shelter.Z;
        var actorId = actor["id"]!.GetValue<ulong>();
        actor["navigationBeliefs"] = new JsonArray(new JsonObject
        {
            ["firstX"] = shelter.X,
            ["firstY"] = shelter.Y,
            ["firstZ"] = shelter.Z,
            ["secondX"] = neighboringCell.X,
            ["secondY"] = neighboringCell.Y,
            ["secondZ"] = neighboringCell.Z,
            ["status"] = (int)NavigationBeliefStatus.Blocked,
            ["observedAt"] = 0,
            ["receivedAt"] = 0,
            ["sourceActorId"] = actorId,
            ["confidence"] = 100,
            ["isDirectObservation"] = true,
        });
        actor["pendingNavigationReports"] = new JsonArray(new JsonObject
        {
            ["firstX"] = shelter.X,
            ["firstY"] = shelter.Y,
            ["firstZ"] = shelter.Z,
            ["secondX"] = neighboringCell.X,
            ["secondY"] = neighboringCell.Y,
            ["secondZ"] = neighboringCell.Z,
        });
        engine = SimulationEngine.Load(save.ToJsonString(), SimulationDefinitions.Foundation);
        Assert.Empty(engine.CreateTribeNavigationKnowledgeSnapshot());

        engine.AdvanceTicks(1);

        var shared = Assert.Single(engine.CreateTribeNavigationKnowledgeSnapshot());
        Assert.Equal(NavigationBeliefStatus.Blocked, shared.Status);
        Assert.Equal(SimulationTick.Zero, shared.ObservedAt);
        Assert.Equal(new SimulationTick(1), shared.ReceivedAt);
        Assert.False(shared.IsDirectObservation);
        var restored = SimulationEngine.Load(engine.Save(), SimulationDefinitions.Foundation);
        Assert.Equal(engine.ComputeStateHash(), restored.ComputeStateHash());
    }

    [Fact]
    public void TribalKnowledgeRetainsProvenanceAfterItsSourceActorIsGone()
    {
        var seed = new WorldSeed(0x4C4547414359UL);
        var engine = SimulationEngine.Create(
            seed,
            SimulationDefinitions.Foundation,
            initialGoblinCount: 0,
            initialFoodStock: 0);
        var save = JsonNode.Parse(engine.Save())!.AsObject();
        save["tribeNavigationBeliefs"] = new JsonArray(new JsonObject
        {
            ["firstX"] = 1,
            ["firstY"] = 1,
            ["firstZ"] = 0,
            ["secondX"] = 2,
            ["secondY"] = 1,
            ["secondZ"] = 0,
            ["status"] = (int)NavigationBeliefStatus.Blocked,
            ["observedAt"] = 0,
            ["receivedAt"] = 0,
            ["sourceActorId"] = 999UL,
            ["confidence"] = 80,
            ["isDirectObservation"] = false,
        });

        var restored = SimulationEngine.Load(
            save.ToJsonString(),
            SimulationDefinitions.Foundation);

        var belief = Assert.Single(restored.CreateTribeNavigationKnowledgeSnapshot());
        Assert.Equal(new EntityId(999), belief.SourceActorId);
        Assert.False(belief.IsDirectObservation);
    }

    private static (GridPosition Start, GridPosition Destination) FindTraversableSquare(
        WorldMapState world)
    {
        for (var y = 0; y < world.Baseline.Height - 1; y++)
        {
            for (var x = 0; x < world.Baseline.Width - 1; x++)
            {
                var upperLeft = new GridPosition(x, y);
                var upperRight = new GridPosition(x + 1, y);
                var lowerLeft = new GridPosition(x, y + 1);
                var lowerRight = new GridPosition(x + 1, y + 1);
                if (world.CanTraverseTerrainEdge(upperLeft, upperRight, canOpenDoors: true) &&
                    world.CanTraverseTerrainEdge(upperLeft, lowerLeft, canOpenDoors: true) &&
                    world.CanTraverseTerrainEdge(lowerLeft, lowerRight, canOpenDoors: true) &&
                    world.CanTraverseTerrainEdge(lowerRight, upperRight, canOpenDoors: true))
                {
                    return (upperLeft, upperRight);
                }
            }
        }

        throw new InvalidOperationException("Generated map has no traversable square.");
    }
}
