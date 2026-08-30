using GoblinStronghold.Simulation.Resources;

namespace GoblinStronghold.Simulation;

public sealed partial class SimulationEngine
{
    private EquipmentLoadoutSnapshot CreateEquipmentLoadout(ActorState actor)
    {
        var equipped = EquipmentCatalog.GetDefinitions(actor.Equipment)
            .Select(definition => new EquippedItemSnapshot(
                definition.Equipment,
                definition.Slot,
                definition.Variant,
                definition.Weight));
        var cargoWeight = actor.CarriedStackId != EntityId.None &&
            _itemStacks.TryGetValue(actor.CarriedStackId, out var carried)
                ? carried.Quantity
                : 0;
        if (actor.CarriedCorpseId != EntityId.None &&
            _corpses.TryGetValue(actor.CarriedCorpseId, out var corpse))
        {
            cargoWeight = checked(cargoWeight + 2 + corpse.EdiblePortions +
                corpse.Contents.Sum(item => item.Quantity * item.UnitWeight));
        }
        var packWeight = checked(
            actor.PersonalFood + actor.PersonalWater + actor.PersonalStoneAmmo);
        return new EquipmentLoadoutSnapshot(
            equipped,
            packWeight,
            cargoWeight,
            Definitions.ActorCarryCapacity);
    }

    private void CreateGoblinCorpse(ActorState actor)
    {
        var contents = EquipmentCatalog.GetDefinitions(actor.Equipment)
            .Select(definition => new CorpseItemSnapshot(
                ResourceKind.Equipment,
                FoodKind.None,
                definition.Variant,
                1,
                definition.Weight))
            .ToList();
        foreach (var food in actor.PersonalFoodKinds.GroupBy(kind => kind))
        {
            contents.Add(new CorpseItemSnapshot(
                ResourceKind.Food,
                food.Key,
                ResourceVariant.None,
                food.Count(),
                1));
        }
        if (actor.PersonalStoneAmmo > 0)
        {
            contents.Add(new CorpseItemSnapshot(
                ResourceKind.Stone,
                FoodKind.None,
                ResourceVariant.None,
                actor.PersonalStoneAmmo,
                1));
        }
        if (actor.CarriedStackId != EntityId.None &&
            _itemStacks.TryGetValue(actor.CarriedStackId, out var carried))
        {
            contents.Add(new CorpseItemSnapshot(
                carried.Resource,
                carried.FoodKind,
                carried.Variant,
                carried.Quantity,
                1));
            RemoveItemStack(carried.Id);
            actor.CarriedStackId = EntityId.None;
        }

        AddCorpse(
            CorpseKind.Goblin,
            actor.Name,
            actor.Position,
            actor.PersonalWater,
            new GoblinInheritanceImprint(
                actor.KnownSkills,
                actor.KnownTraits,
                new GoblinExperienceSnapshot(
                    actor.ForagingExperience,
                    actor.HaulingExperience,
                    actor.BuildingExperience),
                actor.WorkPreferences),
            contents);
    }

    private void CreateHumanCorpse(HumanVillagerSnapshot villager)
    {
        var contents = new List<CorpseItemSnapshot>();
        AddHumanTool(HumanTool.WoodenHoe, ResourceVariant.EquipmentWoodenHoe, 2);
        AddHumanTool(HumanTool.WoodenAxe, ResourceVariant.EquipmentHumanWoodenAxe, 3);
        AddHumanTool(HumanTool.WoodenBucket, ResourceVariant.EquipmentWoodenBucket, 2);
        AddHumanTool(HumanTool.WoodenSpear, ResourceVariant.EquipmentWoodenSpear, 3);
        AddCorpse(
            CorpseKind.Human,
            villager.Name,
            villager.Position,
            containedWater: 0,
            CreateHumanInheritanceImprint(villager),
            contents);

        void AddHumanTool(HumanTool tool, ResourceVariant variant, int weight)
        {
            if (villager.Tools.HasFlag(tool))
            {
                contents.Add(new CorpseItemSnapshot(
                    ResourceKind.Equipment,
                    FoodKind.None,
                    variant,
                    1,
                    weight));
            }
        }
    }

    private void AddCorpse(
        CorpseKind kind,
        string name,
        Map.GridPosition position,
        int containedWater,
        GoblinInheritanceImprint inheritanceImprint,
        IEnumerable<CorpseItemSnapshot> contents)
    {
        var id = AllocateEntityId();
        _corpses.Add(id, new CorpseState(
            id,
            kind,
            name,
            position,
            CurrentTick,
            containedWater,
            GetInitialCorpseEdiblePortions(kind),
            inheritanceImprint,
            contents));
    }

    private static GoblinInheritanceImprint CreateHumanInheritanceImprint(
        HumanVillagerSnapshot villager)
    {
        var skills = villager.Role switch
        {
            HumanCohortRole.Farmers => GoblinSkill.Foraging | GoblinSkill.Survival,
            HumanCohortRole.Workers => GoblinSkill.Hauling | GoblinSkill.Building,
            HumanCohortRole.Guards => GoblinSkill.Survival,
            _ => GoblinSkill.None,
        };
        var experience = Math.Max(0, villager.SkillLevel * 100);
        return new GoblinInheritanceImprint(
            skills,
            GoblinTrait.None,
            new GoblinExperienceSnapshot(
                skills.HasFlag(GoblinSkill.Foraging) ? experience : 0,
                skills.HasFlag(GoblinSkill.Hauling) ? experience : 0,
                skills.HasFlag(GoblinSkill.Building) ? experience : 0),
            villager.Role switch
            {
                HumanCohortRole.Farmers => new GoblinWorkPreferences(2, 0, -1),
                HumanCohortRole.Workers => new GoblinWorkPreferences(-1, 1, 2),
                _ => new GoblinWorkPreferences(0, 0, 0),
            });
    }

    private void LoadCorpses(IEnumerable<CorpseSaveModel> models)
    {
        foreach (var model in models)
        {
            var id = new EntityId(model.Id);
            var position = new Map.GridPosition(model.X, model.Y, model.Z);
            var ediblePortions = model.EdiblePortions ??
                GetInitialCorpseEdiblePortions(model.Kind);
            if (id == EntityId.None || _corpses.ContainsKey(id) ||
                !Enum.IsDefined(model.Kind) || string.IsNullOrWhiteSpace(model.Name) ||
                !IsAddressableMapPosition(position) || model.CreatedAtTick < 0 ||
                model.CreatedAtTick > CurrentTick.Value || model.ContainedWater < 0 ||
                ediblePortions < 0 ||
                ediblePortions > GetInitialCorpseEdiblePortions(model.Kind) ||
                !AreValidCorpseDirectives(model.Directives) ||
                (model.Kind != CorpseKind.Human &&
                    (model.Directives & CorpseBuddingDirectives) != 0) ||
                !HasOnlyKnownFlags(model.InheritableSkills, GoblinSkill.Building) ||
                !HasOnlyKnownFlags(model.InheritableTraits, GoblinTrait.Fastidious) ||
                model.InheritableForagingExperience < 0 ||
                model.InheritableHaulingExperience < 0 ||
                model.InheritableBuildingExperience < 0 ||
                !new GoblinWorkPreferences(
                    model.InheritableForagingPreference,
                    model.InheritableHaulingPreference,
                    model.InheritableBuildingPreference).IsValid ||
                model.Contents.Any(item =>
                    !Enum.IsDefined(item.Resource) ||
                    !IsValidFoodKind(item.Resource, item.FoodKind) ||
                    !IsValidResourceVariant(item.Resource, item.Variant, allowLegacyDefault: true) ||
                    item.Quantity <= 0 || item.UnitWeight <= 0))
            {
                throw new InvalidDataException("The save contains an invalid corpse.");
            }

            _corpses.Add(id, new CorpseState(
                id,
                model.Kind,
                model.Name,
                position,
                new SimulationTick(model.CreatedAtTick),
                model.ContainedWater,
                ediblePortions,
                new GoblinInheritanceImprint(
                    model.InheritableSkills,
                    model.InheritableTraits,
                    new GoblinExperienceSnapshot(
                        model.InheritableForagingExperience,
                        model.InheritableHaulingExperience,
                        model.InheritableBuildingExperience),
                    new GoblinWorkPreferences(
                        model.InheritableForagingPreference,
                        model.InheritableHaulingPreference,
                        model.InheritableBuildingPreference)),
                model.Contents.Select(item => new CorpseItemSnapshot(
                    item.Resource,
                    item.FoodKind,
                    item.Variant,
                    item.Quantity,
                    item.UnitWeight))));
            _corpses[id].Directives = model.Directives;
        }
    }

    private const CorpseDirective CorpseHandlingDirectives =
        CorpseDirective.RecoverToCamp |
        CorpseDirective.RecoverAndBudAtCamp |
        CorpseDirective.BudInPlace;

    private const CorpseDirective CorpseBuddingDirectives =
        CorpseDirective.RecoverAndBudAtCamp |
        CorpseDirective.BudInPlace;

    private static bool AreValidCorpseDirectives(CorpseDirective directives)
    {
        const CorpseDirective known = CorpseDirective.LootContents |
            CorpseDirective.Consume |
            CorpseHandlingDirectives;
        var handling = directives & CorpseHandlingDirectives;
        return (directives & ~known) == 0 && handling is
            CorpseDirective.None or
            CorpseDirective.RecoverToCamp or
            CorpseDirective.RecoverAndBudAtCamp or
            CorpseDirective.BudInPlace;
    }

    private static int GetInitialCorpseEdiblePortions(CorpseKind kind) => kind switch
    {
        CorpseKind.Goblin => 5,
        CorpseKind.Human => 8,
        _ => 0,
    };
}
