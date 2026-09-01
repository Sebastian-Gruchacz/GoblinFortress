using GoblinStronghold.Simulation.Animals;
using GoblinStronghold.Simulation.Map;

namespace GoblinStronghold.Simulation;

public static class AnimalCombatPolicy
{
    public static int GetAttackDamage(AnimalKind kind, GridPosition position) =>
        AnimalAttackPolicy.GetDamage(AnimalSpeciesCatalog.Current.Get(kind), position);
}
