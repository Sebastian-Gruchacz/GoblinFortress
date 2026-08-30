using GoblinStronghold.Simulation.Map;

namespace GoblinStronghold.Simulation;

public static class AnimalCombatPolicy
{
    public static int GetAttackDamage(AnimalKind kind, GridPosition position) => kind switch
    {
        AnimalKind.SwampBoar => 90,
        AnimalKind.CaveSpider => 45 + (Math.Abs(position.Z) * 25),
        AnimalKind.DeepCrawler => 260 + (Math.Abs(position.Z) * 30),
        AnimalKind.MagmaWyrm => 600 + (Math.Abs(position.Z) * 50),
        _ => 0,
    };
}
