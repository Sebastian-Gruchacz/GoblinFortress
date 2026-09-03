using GoblinStronghold.Simulation.Map;
using GoblinStronghold.Simulation.Resources;

namespace GoblinStronghold.Simulation;

public sealed partial class SimulationEngine
{
    private void UpdateFoodSpoilage()
    {
        var spoiledUnits = 0;
        foreach (var stack in _itemStacks.Values
                     .Where(stack =>
                         stack.Resource == ResourceKind.Food &&
                         stack.FreshUntilTick <= CurrentTick.Value)
                     .ToArray())
        {
            spoiledUnits = checked(spoiledUnits + stack.Quantity);
            foreach (var actor in _actors.Values.Where(actor =>
                         actor.CarriedStackId == stack.Id || actor.SourceStackId == stack.Id))
            {
                if (actor.CarriedStackId == stack.Id)
                {
                    actor.CarriedStackId = EntityId.None;
                }
                actor.ClearJob();
            }
            RemoveItemStack(stack.Id);
        }

        foreach (var actor in _actors.Values)
        {
            for (var index = actor.PersonalFoodKinds.Count - 1; index >= 0; index--)
            {
                if (actor.PersonalFoodFreshUntilTicks[index] > CurrentTick.Value)
                {
                    continue;
                }

                actor.PersonalFoodKinds.RemoveAt(index);
                actor.PersonalFoodFreshUntilTicks.RemoveAt(index);
                spoiledUnits++;
            }
        }

        foreach (var order in _craftingOrders.Values)
        {
            spoiledUnits = checked(
                spoiledUnits + order.SpoilExpiredFood(CurrentTick.Value));
        }

        if (spoiledUnits > 0 && World.CountWorldObjects(
                WorldObjectKind.GoblinCompost,
                WorldObjectOwner.GoblinTribe) > 0)
        {
            _compostNutrients = checked(_compostNutrients + spoiledUnits);
        }
    }
}
