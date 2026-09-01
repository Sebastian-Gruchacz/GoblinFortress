using GoblinStronghold.Simulation.ContentPacks;
using GoblinStronghold.Simulation.Map;

namespace GoblinStronghold.Simulation.Animals;

public static class AnimalDispositionPolicy
{
    private static readonly ContentId GoblinGroupId = ContentId.Parse("core:goblins");
    private static readonly IReadOnlyDictionary<ContentId, Func<int, bool>> Models =
        new Dictionary<ContentId, Func<int, bool>>
        {
            [ContentId.Parse("core:passive-prey")] = _ => false,
            [ContentId.Parse("core:territorial")] = nearbyActorCount =>
                nearbyActorCount == 1,
            [ContentId.Parse("core:aggressive-predator")] = nearbyActorCount =>
                nearbyActorCount > 0,
        };

    public static bool Supports(ContentId modelId) => Models.ContainsKey(modelId);

    public static bool ConsidersGoblinsEnemies(AnimalBehaviorDefinition behavior) =>
        behavior.Enemies.Any(enemy =>
            enemy.Kind == AnimalEnemySelectorKind.Group && enemy.Id == GoblinGroupId);

    public static bool ShouldAttack(AnimalBehaviorDefinition behavior, int nearbyActorCount) =>
        behavior.Aggression == 0
            ? false
            : Models.TryGetValue(behavior.ModelId, out var shouldAttack)
                ? shouldAttack(nearbyActorCount)
                : throw new KeyNotFoundException(
                    $"Unknown animal behavior model '{behavior.ModelId}'.");
}

public static class AnimalAttackPolicy
{
    public static int GetDamage(
        AnimalSpeciesDefinition species,
        GridPosition position) => checked(
            species.Attack.BaseDamage +
            Math.Abs(position.Z) * species.Attack.DamagePerDepth);
}
