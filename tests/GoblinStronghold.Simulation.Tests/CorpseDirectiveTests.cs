using System.Text.Json.Nodes;
using GoblinStronghold.Simulation.Map;
using Xunit;

namespace GoblinStronghold.Simulation.Tests;

public sealed class CorpseDirectiveTests
{
    [Fact]
    public void SpecificCorpseCanBeLootedWithoutActiveRaid()
    {
        var engine = CreateEngine();
        var corpseId = AddGoblinCorpse(
            ref engine,
            engine.Map.GoblinSpawn,
            includeLoot: true);

        engine.QueueCommand(SimulationCommand.ConfigureCorpseDirectives(
            engine.CurrentTick.Next(),
            sequence: 1,
            corpseId,
            CorpseDirective.LootContents));
        engine.AdvanceTicks(1);
        for (var tick = 0; tick < 2_000; tick++)
        {
            if (engine.CreateSnapshot().Corpses.Single().Contents.Count == 0)
            {
                break;
            }
            engine.AdvanceTicks(1);
        }

        var snapshot = engine.CreateSnapshot();
        var corpse = Assert.Single(snapshot.Corpses);
        Assert.Equal(GoblinRaidPhase.None, snapshot.RaidPhase);
        Assert.Empty(corpse.Contents);
        Assert.False(corpse.Directives.HasFlag(CorpseDirective.LootContents));
        Assert.Contains(snapshot.ItemStacks, stack =>
            stack.Resource == Resources.ResourceKind.Stone && stack.Quantity == 2);
    }

    [Fact]
    public void WeaponTakenFromCorpseRemainsCargoInsteadOfBeingEquipped()
    {
        var engine = CreateEngine();
        var save = JsonNode.Parse(engine.Save())!.AsObject();
        const PersonalEquipment mainHandEquipment =
            PersonalEquipment.BoneKnife |
            PersonalEquipment.WoodenAxe |
            PersonalEquipment.PrimitivePickaxe |
            PersonalEquipment.ReinforcedPickaxe |
            PersonalEquipment.FightingStick |
            PersonalEquipment.StoneClub;
        foreach (var actor in save["actors"]!.AsArray())
        {
            actor!["equipment"] =
                (actor["equipment"]!.GetValue<int>() & ~(int)mainHandEquipment) |
                (int)PersonalEquipment.FightingStick;
        }
        engine = SimulationEngine.Load(save.ToJsonString(), SimulationDefinitions.Foundation);
        var corpseId = AddGoblinCorpse(
            ref engine,
            engine.Map.GoblinSpawn,
            includeLoot: true,
            Resources.ResourceKind.Equipment,
            Resources.ResourceVariant.EquipmentStoneClub,
            lootQuantity: 1);

        engine.QueueCommand(SimulationCommand.ConfigureCorpseDirectives(
            engine.CurrentTick.Next(),
            sequence: 1,
            corpseId,
            CorpseDirective.LootContents));
        engine.AdvanceTicks(1);
        for (var tick = 0; tick < 2_000; tick++)
        {
            if (engine.CreateSnapshot().Corpses.Single().Contents.Count == 0)
            {
                break;
            }
            engine.AdvanceTicks(1);
        }

        var snapshot = engine.CreateSnapshot();
        Assert.Empty(Assert.Single(snapshot.Corpses).Contents);
        Assert.All(snapshot.Actors, actor =>
        {
            Assert.True(actor.Equipment.HasFlag(PersonalEquipment.FightingStick));
            Assert.False(actor.Equipment.HasFlag(PersonalEquipment.StoneClub));
        });
        var carriedWeapon = Assert.Single(snapshot.ItemStacks, stack =>
            stack.Resource == Resources.ResourceKind.Equipment &&
            stack.Variant == Resources.ResourceVariant.EquipmentStoneClub);
        Assert.Equal(Resources.ItemLocationKind.ActorInventory, carriedWeapon.Location.Kind);
        Assert.Contains(snapshot.Actors, actor => actor.CarriedStackId == carriedWeapon.Id);
    }

    [Fact]
    public void GoblinCorpseCanBeConsumedWithoutActiveRaid()
    {
        var engine = CreateEngine();
        var corpseId = AddGoblinCorpse(ref engine, engine.Map.GoblinSpawn);

        engine.QueueCommand(SimulationCommand.ConfigureCorpseDirectives(
            engine.CurrentTick.Next(),
            sequence: 1,
            corpseId,
            CorpseDirective.Consume));
        engine.AdvanceTicks(1);

        for (var tick = 0; tick < 2_000; tick++)
        {
            if (engine.CreateSnapshot().Corpses.Single().EdiblePortions < 5)
            {
                break;
            }
            engine.AdvanceTicks(1);
        }

        var snapshot = engine.CreateSnapshot();
        var corpse = Assert.Single(snapshot.Corpses);
        Assert.Equal(GoblinRaidPhase.None, snapshot.RaidPhase);
        Assert.True(corpse.EdiblePortions < 5);
        Assert.True(corpse.Directives.HasFlag(CorpseDirective.Consume));
        var restored = SimulationEngine.Load(engine.Save(), SimulationDefinitions.Foundation);
        Assert.Equal(engine.ComputeStateHash(), restored.ComputeStateHash());
        Assert.Equal(corpse.Directives, Assert.Single(restored.CreateSnapshot().Corpses).Directives);
    }

    [Fact]
    public void GoblinCorpseCanBeRecoveredToFieldCampWithoutActiveRaid()
    {
        var engine = CreateEngine();
        var campPosition = FindCampPosition(engine);
        engine.QueueCommand(SimulationCommand.BuildGoblinFieldCamp(
            new SimulationTick(1), sequence: 1, campPosition));
        engine.AdvanceTicks(1);
        SimulationTestSteps.AdvanceUntilConstructionCompletes(engine);
        var corpseId = AddGoblinCorpse(ref engine, engine.Map.GoblinSpawn);

        engine.QueueCommand(SimulationCommand.ConfigureCorpseDirectives(
            engine.CurrentTick.Next(),
            sequence: 2,
            corpseId,
            CorpseDirective.RecoverToCamp));
        engine.AdvanceTicks(1);
        for (var tick = 0; tick < 4_000; tick++)
        {
            var corpse = engine.CreateSnapshot().Corpses.Single();
            if (corpse.Position == campPosition &&
                corpse.Directives == CorpseDirective.None)
            {
                break;
            }
            engine.AdvanceTicks(1);
        }

        var snapshot = engine.CreateSnapshot();
        var recovered = Assert.Single(snapshot.Corpses);
        Assert.Equal(GoblinRaidPhase.None, snapshot.RaidPhase);
        Assert.Equal(campPosition, recovered.Position);
        Assert.Equal(CorpseDirective.None, recovered.Directives);
        Assert.All(snapshot.Actors, actor =>
            Assert.NotEqual(corpseId, actor.CarriedCorpseId));
    }

    [Fact]
    public void GoblinCorpseCanBeBuddedInPlaceWithoutActiveRaid()
    {
        var engine = CreateEngine();
        var corpsePosition = engine.Map.GoblinSpawn;
        var corpseId = AddGoblinCorpse(ref engine, corpsePosition);

        engine.QueueCommand(SimulationCommand.ConfigureCorpseDirectives(
            engine.CurrentTick.Next(),
            sequence: 1,
            corpseId,
            CorpseDirective.BudInPlace));
        engine.AdvanceTicks(1);
        Assert.True(Assert.Single(engine.CreateSnapshot().Corpses).Directives.HasFlag(
            CorpseDirective.BudInPlace));

        for (var tick = 0; tick < 4_000 && engine.CreateSnapshot().GoblinBuds.Count == 0; tick++)
        {
            engine.AdvanceTicks(1);
        }

        var snapshot = engine.CreateSnapshot();
        var bud = Assert.Single(snapshot.GoblinBuds);
        Assert.Equal(corpseId, bud.OriginCorpseId);
        Assert.Equal(corpsePosition, bud.Position);
        Assert.DoesNotContain(snapshot.Corpses, corpse => corpse.Id == corpseId);
        var restored = SimulationEngine.Load(engine.Save(), SimulationDefinitions.Foundation);
        Assert.Equal(engine.ComputeStateHash(), restored.ComputeStateHash());
    }

    private static SimulationEngine CreateEngine() => SimulationEngine.Create(
        new WorldSeed(0x434F52505345UL),
        SimulationDefinitions.Foundation,
        initialGoblinCount: 2,
        initialFoodStock: 20,
        initialWoodStock: 12);

    private static EntityId AddGoblinCorpse(
        ref SimulationEngine engine,
        GridPosition position,
        bool includeLoot = false,
        Resources.ResourceKind lootResource = Resources.ResourceKind.Stone,
        Resources.ResourceVariant lootVariant = Resources.ResourceVariant.None,
        int lootQuantity = 2)
    {
        var save = JsonNode.Parse(engine.Save())!.AsObject();
        var corpseId = save["nextEntityId"]!.GetValue<ulong>();
        save["nextEntityId"] = corpseId + 1;
        save["corpses"]!.AsArray().Add(new JsonObject
        {
            ["id"] = corpseId,
            ["kind"] = (int)CorpseKind.Goblin,
            ["name"] = "Glek",
            ["x"] = position.X,
            ["y"] = position.Y,
            ["z"] = position.Z,
            ["createdAtTick"] = save["currentTick"]!.GetValue<long>(),
            ["containedWater"] = 0,
            ["ediblePortions"] = 5,
            ["contents"] = includeLoot
                ? new JsonArray(new JsonObject
                {
                    ["resource"] = (int)lootResource,
                    ["foodKind"] = (int)Resources.FoodKind.None,
                    ["variant"] = (int)lootVariant,
                    ["quantity"] = lootQuantity,
                    ["unitWeight"] = 1,
                })
                : new JsonArray(),
        });
        engine = SimulationEngine.Load(save.ToJsonString(), SimulationDefinitions.Foundation);
        return new EntityId(corpseId);
    }

    private static GridPosition FindCampPosition(SimulationEngine engine) =>
        Enumerable.Range(0, engine.Map.Height)
            .SelectMany(y => Enumerable.Range(0, engine.Map.Width)
                .Select(x => new GridPosition(x, y)))
            .Where(engine.World.CanBuildGoblinFieldCamp)
            .OrderBy(position => Math.Abs(position.X - engine.Map.GoblinSpawn.X) +
                Math.Abs(position.Y - engine.Map.GoblinSpawn.Y))
            .ThenBy(position => position.Y)
            .ThenBy(position => position.X)
            .First();
}
