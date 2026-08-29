using GoblinStronghold.Simulation.Map;
using GoblinStronghold.Simulation.Resources;

namespace GoblinStronghold.Simulation;

public sealed partial class SimulationEngine
{
    private VillageLootContainerSnapshot[] CreateVillageLootSnapshot()
    {
        var village = _humanVillage.CreateSnapshot();
        var buildings = World.EnumerateWorldObjects()
            .Where(item => item.Owner == WorldObjectOwner.HumanVillage &&
                item.Kind is WorldObjectKind.HumanBarn or WorldObjectKind.HumanCottage or
                    WorldObjectKind.HumanStorehouse)
            .OrderBy(item => item.Kind == WorldObjectKind.HumanBarn ? 0 : 1)
            .ThenBy(item => item.Id)
            .ToArray();
        if (buildings.Length == 0)
        {
            return [];
        }

        var contents = buildings.ToDictionary(item => item.Id, _ => new List<CorpseItemSnapshot>());
        var barn = buildings.FirstOrDefault(item => item.Kind == WorldObjectKind.HumanBarn) ?? buildings[0];
        var timberStore = buildings.FirstOrDefault(item => item.Kind == WorldObjectKind.HumanCottage) ?? barn;
        AddStock(barn, ResourceKind.Food, FoodKind.DriedRations,
            ResourceVariant.None, village.FoodStock);
        AddStock(timberStore, ResourceKind.Wood, FoodKind.None,
            ResourceVariant.OakWood, village.WoodStock);
        AddStock(barn, ResourceKind.Hide, FoodKind.None,
            ResourceVariant.None, village.GoodsStock / 2);
        AddStock(barn, ResourceKind.Reeds, FoodKind.None,
            ResourceVariant.None, village.GoodsStock - village.GoodsStock / 2);
        AddEquipment(buildings[0], ResourceVariant.EquipmentWoodenHoe, 2);
        AddEquipment(buildings[0], ResourceVariant.EquipmentWoodenBucket, 2);
        AddEquipment(buildings[Math.Min(1, buildings.Length - 1)],
            ResourceVariant.EquipmentHumanWoodenAxe, 3);
        AddEquipment(buildings[Math.Min(2, buildings.Length - 1)],
            ResourceVariant.EquipmentWoodenSpear, 3);

        return buildings.Select(item => (Building: item, Access: GetLootAccessPosition(item)))
            .Where(item => item.Access is not null)
            .Select(item => new VillageLootContainerSnapshot(
                item.Building.Id,
                item.Access!.Value,
                contents[item.Building.Id]))
            .ToArray();

        void AddStock(
            WorldObjectSnapshot building,
            ResourceKind resource,
            FoodKind foodKind,
            ResourceVariant variant,
            int quantity)
        {
            if (quantity > 0)
            {
                contents[building.Id].Add(new CorpseItemSnapshot(
                    resource, foodKind, variant, quantity, 1));
            }
        }

        void AddEquipment(WorldObjectSnapshot building, ResourceVariant variant, int weight)
        {
            if (!_stolenVillageEquipment.Contains(variant))
            {
                contents[building.Id].Add(new CorpseItemSnapshot(
                    ResourceKind.Equipment, FoodKind.None, variant, 1, weight));
            }
        }

        GridPosition? GetLootAccessPosition(WorldObjectSnapshot building) => building
            .GetAbsoluteParts()
            .SelectMany(part => new[]
            {
                part.Position,
                part.Position with { X = part.Position.X - 1 },
                part.Position with { X = part.Position.X + 1 },
                part.Position with { Y = part.Position.Y - 1 },
                part.Position with { Y = part.Position.Y + 1 },
            })
            .Distinct()
            .Where(World.IsTerrainTraversable)
            .OrderBy(position => Math.Abs(position.X - building.Anchor.X) +
                Math.Abs(position.Y - building.Anchor.Y))
            .ThenBy(position => position.Y)
            .ThenBy(position => position.X)
            .Select(position => (GridPosition?)position)
            .FirstOrDefault();
    }
}
